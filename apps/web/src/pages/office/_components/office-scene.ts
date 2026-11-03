/**
 * @file office-scene.ts
 * 虛擬辦公室的 Phaser 場景：只負責畫地圖、角色、補間、泡泡與鍵盤／點擊走動。
 * 不直接連 hub；自己的每一步透過 handlers.step 交給外層送出，其他人的變化由外層呼叫公開方法更新。
 * 效能：上限 30 fps；分頁在背景時 Phaser 自動暫停；一分鐘沒有任何變化就讓迴圈休眠，有事件或操作再喚醒。
 */
import Phaser from 'phaser';
import type { Facing, OfficeMap, OfficeOccupant, OfficeSnapshot } from '@/shared/services/office-service';
import { ensureTextures, TILE } from './office-textures';
import { KENNEY_SHEETS, OFFICE_TILE_FRAMES } from './office-tiles';
import { facingTo, findPath, isWalkable, zoneAt, type Point } from './office-grid';

const STEP_MS = 140;
const TWEEN_MS = 150;
const SLEEP_AFTER_MS = 60_000;
const LABEL_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '6px', color: '#ffffff', stroke: '#000000', strokeThickness: 2, resolution: 4 };
const SEAT_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '5px', color: '#f5f5f5', resolution: 4 };
const BUBBLE_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '6px', color: '#1f1f1f', resolution: 4, wordWrap: { width: 90, useAdvancedWrap: true } };
const ICON_STYLE: Phaser.Types.GameObjects.Text.TextStyle = { fontFamily: 'sans-serif', fontSize: '8px', resolution: 4 };
const STATUS_ICONS: Record<string, string> = { available: '', busy: '⛔', meeting: '📅', break: '☕', away: '🚶' };
const DIRECTIONS: Record<Facing, Point> = { up: { x: 0, y: -1 }, down: { x: 0, y: 1 }, left: { x: -1, y: 0 }, right: { x: 1, y: 0 } };

/** 場景對外層的回呼。 */
export interface OfficeSceneHandlers {
  /**
   * 自己的角色走了一步或原地轉向，由外層送給 hub 的 Move。
   * @param x 新的格子 X。
   * @param y 新的格子 Y。
   * @param facing 新的朝向。
   */
  step(x: number, y: number, facing: Facing): void;
}

interface AvatarView {
  occupant: OfficeOccupant;
  container: Phaser.GameObjects.Container;
  sprite: Phaser.GameObjects.Image;
  status: Phaser.GameObjects.Text;
  bubble?: Phaser.GameObjects.Container;
}

const toPixel = (point: Point) => ({ x: (point.x + 0.5) * TILE, y: (point.y + 1) * TILE });
const textureOf = (occupant: Pick<OfficeOccupant, 'avatarKey' | 'facing'>) => `avatar-${occupant.avatarKey}-${occupant.facing}`;

// 可輸入文字的欄位：唯讀（antd Select 沒開搜尋時的輸入框）與停用的不算，否則選完下拉後方向鍵會失效。
function focusedTextField(): HTMLElement | null {
  const element = document.activeElement;
  if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) return element.readOnly || element.disabled ? null : element;
  return element instanceof HTMLElement && element.isContentEditable ? element : null;
}

// 聊天輸入框有焦點時方向鍵要留給輸入框，不能同時讓角色亂走。
const isTyping = () => focusedTextField() !== null;

/** 辦公室場景：持有地圖與所有在場角色的畫面物件。 */
export class OfficeScene extends Phaser.Scene {
  private office: OfficeMap | null = null;
  private meId = '';
  private readonly avatars = new Map<string, AvatarView>();
  private readonly seatLabels = new Map<string, Phaser.GameObjects.Text>();
  private mapLayer: Phaser.GameObjects.Container | null = null;
  private route: Point[] = [];
  private nextStepAt = 0;
  private lastActiveAt = performance.now();
  private sleeping = false;
  private keys: Record<string, Phaser.Input.Keyboard.Key> = {};
  private assetsFailed = false;

  constructor(
    private readonly handlers: OfficeSceneHandlers,
    private readonly onReady: (scene: OfficeScene) => void,
    private readonly onAssetsFailed: () => void = () => {},
  ) {
    super('office');
  }

  preload(): void {
    // 素材壞了不能讓整個辦公室打不開：標記失敗後 tile() 會全部改用程式繪製的貼圖。
    this.load.on('loaderror', () => { this.assetsFailed = true; });
    for (const sheet of Object.values(KENNEY_SHEETS)) this.load.spritesheet(sheet.key, sheet.url, { frameWidth: TILE, frameHeight: TILE, spacing: 1 });
  }

  create(): void {
    if (this.assetsFailed) this.onAssetsFailed();
    // 不攔截按鍵（enableCapture=false），聊天輸入框才能正常打字；打字時 update 會忽略方向鍵。
    this.keys = this.input.keyboard!.addKeys({ up: 'UP', down: 'DOWN', left: 'LEFT', right: 'RIGHT', w: 'W', s: 'S', a: 'A', d: 'D' }, false) as Record<string, Phaser.Input.Keyboard.Key>;
    this.input.on('pointerdown', (pointer: Phaser.Input.Pointer) => {
      // Phaser 對 canvas 的 mousedown 會 preventDefault，焦點不會自己離開輸入框，得手動放開，方向鍵才會恢復。
      focusedTextField()?.blur();
      this.walkTo({ x: Math.floor(pointer.worldX / TILE), y: Math.floor(pointer.worldY / TILE) });
    });
    this.scale.on('resize', () => this.fitCamera());
    this.onReady(this);
  }

  /**
   * 載入（或重新載入）整張辦公室：清掉舊地圖與角色，依快照重畫。
   * @param snapshot JoinOffice 回傳的快照（地圖與在場者）。
   * @param meId 自己的 membershipId，鏡頭會跟隨這個角色。
   */
  loadOffice(snapshot: OfficeSnapshot, meId: string): void {
    this.wake();
    this.mapLayer?.destroy();
    this.avatars.forEach((view) => view.container.destroy());
    this.avatars.clear();
    this.seatLabels.clear();
    this.route = [];
    this.office = snapshot.map;
    this.meId = meId;
    const { map } = snapshot;
    ensureTextures(this, map.theme);
    const layer = this.add.container(0, 0);
    for (let y = 0; y < map.height; y++) {
      for (let x = 0; x < map.width; x++) {
        const key = map.grid[y][x] === '#' ? `wall-${map.theme}` : `floor-${map.theme}-${zoneAt(map, x, y)?.type ?? 'desk'}`;
        layer.add(this.tile(x * TILE, y * TILE, key));
      }
    }
    for (const item of map.furniture) layer.add(this.tile(item.x * TILE, item.y * TILE, `furniture-${item.kind}`));
    // 座位主人不在場時，空椅子上淡淡顯示名字。
    for (const seat of map.seats) {
      const label = this.add.text((seat.x + 0.5) * TILE, (seat.y + 0.5) * TILE, seat.name, SEAT_STYLE).setOrigin(0.5).setAlpha(0.6);
      layer.add(label);
      this.seatLabels.set(seat.membershipId, label);
    }
    this.mapLayer = layer;
    snapshot.occupants.forEach((occupant) => this.upsertOccupant(occupant));
    this.fitCamera();
    const me = this.avatars.get(meId);
    if (me) this.cameras.main.startFollow(me.container, true, 0.2, 0.2);
  }

  /**
   * 新增或覆寫一個角色（直接就定位，不做補間）。
   * @param occupant 在場者資料。
   */
  upsertOccupant(occupant: OfficeOccupant): void {
    this.wake();
    const { x, y } = toPixel(occupant);
    const existing = this.avatars.get(occupant.membershipId);
    if (existing) {
      existing.occupant = occupant;
      existing.sprite.setTexture(textureOf(occupant));
      existing.status.setText(STATUS_ICONS[occupant.status] ?? '');
      existing.container.setPosition(x, y).setDepth(occupant.y + 10);
      return;
    }
    const sprite = this.add.image(0, 0, textureOf(occupant)).setOrigin(0.5, 1);
    const name = this.add.text(0, -TILE, occupant.name, LABEL_STYLE).setOrigin(0.5, 1);
    const status = this.add.text(TILE * 0.8, -TILE * 0.35, STATUS_ICONS[occupant.status] ?? '', ICON_STYLE).setOrigin(0.5, 1);
    const container = this.add.container(x, y, [sprite, name, status]).setDepth(occupant.y + 10);
    this.avatars.set(occupant.membershipId, { occupant, container, sprite, status });
    this.seatLabels.get(occupant.membershipId)?.setVisible(false);
  }

  /**
   * 移除離開的角色，並讓他的座位名牌重新顯示。
   * @param membershipId 離開者的 membershipId。
   */
  removeOccupant(membershipId: string): void {
    this.avatars.get(membershipId)?.container.destroy();
    this.avatars.delete(membershipId);
    this.seatLabels.get(membershipId)?.setVisible(true);
    this.wake();
  }

  /**
   * 移動角色；animate=false 用於自己被後端拉回或瞬移（回座位、接受敲門）。
   * @param membershipId 要移動的角色。
   * @param x 目標格子 X。
   * @param y 目標格子 Y。
   * @param facing 移動後的朝向。
   * @param animate 是否以補間滑過去；false 時直接定位，且自己的待走路徑會被清掉。
   */
  moveOccupant(membershipId: string, x: number, y: number, facing: Facing, animate = true): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    this.wake();
    if (membershipId === this.meId && !animate) this.route = [];
    view.occupant = { ...view.occupant, x, y, facing };
    view.sprite.setTexture(textureOf(view.occupant));
    view.container.setDepth(y + 10);
    const target = toPixel({ x, y });
    this.tweens.killTweensOf(view.container);
    if (animate) this.tweens.add({ targets: view.container, x: target.x, y: target.y, duration: TWEEN_MS });
    else view.container.setPosition(target.x, target.y);
  }

  /**
   * 更新角色頭上的狀態圖示。
   * @param membershipId 角色。
   * @param status 新狀態。
   */
  setStatus(membershipId: string, status: OfficeOccupant['status']): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    view.occupant = { ...view.occupant, status };
    view.status.setText(STATUS_ICONS[status] ?? '');
    this.wake();
  }

  /**
   * 更換角色外觀。
   * @param membershipId 角色。
   * @param avatarKey 新的角色代號（a1 至 a8）。
   */
  setAvatar(membershipId: string, avatarKey: string): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    view.occupant = { ...view.occupant, avatarKey };
    view.sprite.setTexture(textureOf(view.occupant));
    this.wake();
  }

  /**
   * 在角色頭上顯示一個表情符號，約三秒後淡出。
   * @param membershipId 角色。
   * @param icon 要顯示的表情符號。
   */
  showEmote(membershipId: string, icon: string): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    this.wake();
    const emote = this.add.text(0, -TILE * 1.8, icon, { ...ICON_STYLE, fontSize: '10px' }).setOrigin(0.5, 1);
    view.container.add(emote);
    this.tweens.add({ targets: emote, y: emote.y - 6, alpha: 0, delay: 2400, duration: 600, onComplete: () => emote.destroy() });
  }

  /**
   * 在角色頭上顯示對話泡泡（5 秒後消失；新的泡泡會取代舊的）。
   * @param membershipId 發話者。
   * @param text 訊息內容，超過 60 字會截斷。
   */
  showBubble(membershipId: string, text: string): void {
    const view = this.avatars.get(membershipId);
    if (!view) return;
    this.wake();
    view.bubble?.destroy();
    const content = this.add.text(0, 0, text.length > 60 ? `${text.slice(0, 60)}…` : text, BUBBLE_STYLE).setOrigin(0.5, 1);
    const padding = 3;
    const background = this.add.rectangle(0, padding, content.width + padding * 2, content.height + padding * 2, 0xffffff, 0.95).setOrigin(0.5, 1).setStrokeStyle(0.5, 0x8c8c8c);
    const bubble = this.add.container(0, -TILE * 1.9, [background, content]);
    view.container.add(bubble);
    view.bubble = bubble;
    this.time.delayedCall(5000, () => {
      if (view.bubble === bubble) view.bubble = undefined;
      bubble.destroy();
    });
  }

  /**
   * 讓自己的角色尋路走向目標格子（目標是家具時走到旁邊）。
   * @param target 目標格子。
   */
  walkTo(target: Point): void {
    const me = this.avatars.get(this.meId);
    if (!this.office || !me) return;
    this.wake();
    this.route = findPath(this.office, me.occupant, target);
  }

  /** 喚醒休眠中的遊戲迴圈並重設閒置計時；有事件或使用者操作時呼叫。 */
  wake(): void {
    this.lastActiveAt = performance.now();
    if (!this.sleeping) return;
    this.sleeping = false;
    this.game.loop.wake(true);
  }

  update(time: number): void {
    const me = this.avatars.get(this.meId);
    if (!this.office || !me) return;
    const direction = isTyping() ? null : this.pressedDirection();
    if (direction) {
      this.route = [];
      this.lastActiveAt = performance.now();
    }
    if (time >= this.nextStepAt) {
      const from = me.occupant;
      const next = direction ? { x: from.x + DIRECTIONS[direction].x, y: from.y + DIRECTIONS[direction].y } : this.route.shift();
      if (next) {
        const facing = direction ?? facingTo(from, next);
        const target = isWalkable(this.office, next.x, next.y) ? next : { x: from.x, y: from.y };
        if (target.x !== from.x || target.y !== from.y || facing !== from.facing) {
          this.moveOccupant(this.meId, target.x, target.y, facing);
          this.handlers.step(target.x, target.y, facing);
        }
        this.nextStepAt = time + STEP_MS;
        this.lastActiveAt = performance.now();
      }
    }
    if (performance.now() - this.lastActiveAt > SLEEP_AFTER_MS && this.tweens.getTweens().length === 0) {
      this.sleeping = true;
      this.game.loop.sleep();
    }
  }

  // 有對照 frame 且素材載入成功就用 Kenney 圖塊，否則用程式繪製的貼圖（ensureTextures 已先登錄）。
  private tile(x: number, y: number, key: string): Phaser.GameObjects.Image {
    const frame = this.assetsFailed ? undefined : OFFICE_TILE_FRAMES[key];
    return frame === undefined ? this.add.image(x, y, key).setOrigin(0) : this.add.image(x, y, KENNEY_SHEETS.indoor.key, frame).setOrigin(0);
  }

  private pressedDirection(): Facing | null {
    const keys = this.keys;
    if (keys.up?.isDown || keys.w?.isDown) return 'up';
    if (keys.down?.isDown || keys.s?.isDown) return 'down';
    if (keys.left?.isDown || keys.a?.isDown) return 'left';
    if (keys.right?.isDown || keys.d?.isDown) return 'right';
    return null;
  }

  private fitCamera(): void {
    if (!this.office) return;
    const fit = Math.min(this.scale.width / (this.office.width * TILE), this.scale.height / (this.office.height * TILE));
    this.cameras.main.setZoom(Math.min(4, Math.max(2, Math.floor(fit * 2) / 2)));
    this.cameras.main.setBounds(0, 0, this.office.width * TILE, this.office.height * TILE);
  }
}

/**
 * 建立 Phaser 遊戲並等場景 create 完成；呼叫端以動態 import 載入此模組，Phaser 只在 /office 下載。
 * @param parent 放 canvas 的容器元素，遊戲尺寸會跟著它縮放。
 * @param handlers 場景對外層的回呼。
 * @param onAssetsFailed 素材（Kenney spritesheet）載入失敗時呼叫一次，讓外層提示使用者已改用簡易圖案。
 * @returns 場景 create 完成後 resolve，附上 game（離開頁面時 destroy）與 scene。
 */
export function createOfficeGame(parent: HTMLElement, handlers: OfficeSceneHandlers, onAssetsFailed?: () => void): Promise<{ game: Phaser.Game; scene: OfficeScene }> {
  return new Promise((resolve) => {
    const game: Phaser.Game = new Phaser.Game({
      type: Phaser.AUTO,
      parent,
      pixelArt: true,
      backgroundColor: '#1b1f24',
      banner: false,
      audio: { noAudio: true },
      fps: { target: 30, limit: 30 },
      scale: { mode: Phaser.Scale.RESIZE, width: parent.clientWidth, height: parent.clientHeight },
      scene: new OfficeScene(handlers, (scene) => resolve({ game, scene }), onAssetsFailed),
    });
  });
}
