/**
 * @file office-textures.ts
 * 以程式繪製 16×16 圖塊與角色；Kenney 素材（Task 11）載入失敗或尚未加入時使用。
 * 貼圖鍵名：floor-{theme}-{zone}、wall-{theme}、furniture-{kind}、avatar-{key}-{facing}。
 */
import type Phaser from 'phaser';

/** 單一圖塊的像素邊長。 */
export const TILE = 16;

const PALETTES = {
  wood: { desk: 0xc8a27a, meeting: 0xd9c7a3, pantry: 0xe8dcc8, entrance: 0xb9a48a, wall: 0x5b4636 },
  carpet: { desk: 0x7f93a8, meeting: 0x96a6b8, pantry: 0xd4d8dc, entrance: 0x6f7f90, wall: 0x37474f },
  dark: { desk: 0x3a4048, meeting: 0x454c55, pantry: 0x525a63, entrance: 0x30353c, wall: 0x1c1f24 },
} as const;

/** 角色代號對應的衣服顏色，鍵名須與 OFFICE_AVATARS 一致。 */
export const AVATAR_COLORS: Record<string, number> = {
  a1: 0xe57373, a2: 0x64b5f6, a3: 0x81c784, a4: 0xffb74d,
  a5: 0xba68c8, a6: 0x4db6ac, a7: 0xf06292, a8: 0x90a4ae,
};

const FURNITURE: Record<string, (g: Phaser.GameObjects.Graphics) => void> = {
  desk: (g) => { g.fillStyle(0x8d6e63); g.fillRect(0, 1, 16, 14); g.fillStyle(0x263238); g.fillRect(4, 3, 8, 6); g.fillStyle(0x4fc3f7); g.fillRect(5, 4, 6, 4); },
  chair: (g) => { g.fillStyle(0x546e7a); g.fillRoundedRect(4, 4, 8, 8, 2); },
  'meeting-table': (g) => { g.fillStyle(0x6d4c41); g.fillRect(0, 1, 16, 14); },
  coffee: (g) => { g.fillStyle(0x424242); g.fillRect(3, 2, 10, 12); g.fillStyle(0xd32f2f); g.fillRect(5, 4, 6, 3); g.fillStyle(0xffffff); g.fillRect(6, 10, 4, 3); },
  fridge: (g) => { g.fillStyle(0xeceff1); g.fillRect(3, 0, 10, 16); g.fillStyle(0xb0bec5); g.fillRect(3, 6, 10, 1); g.fillRect(11, 2, 1, 3); },
  counter: (g) => { g.fillStyle(0x90a4ae); g.fillRect(0, 2, 16, 13); g.fillStyle(0xcfd8dc); g.fillRect(0, 2, 16, 3); },
  table: (g) => { g.fillStyle(0xa1887f); g.fillRect(1, 2, 14, 12); },
  plant: (g) => { g.fillStyle(0x8d6e63); g.fillRect(5, 10, 6, 5); g.fillStyle(0x43a047); g.fillCircle(8, 7, 4); g.fillCircle(5, 6, 3); g.fillCircle(11, 6, 3); },
  whiteboard: (g) => { g.fillStyle(0xffffff); g.fillRect(1, 4, 14, 9); g.lineStyle(1, 0x9e9e9e); g.strokeRect(1.5, 4.5, 13, 8); g.fillStyle(0x1e88e5); g.fillRect(3, 7, 6, 1); },
  rug: (g) => { g.fillStyle(0x8d3b3b, 0.6); g.fillRect(1, 3, 14, 10); },
};

function paint(scene: Phaser.Scene, key: string, draw: (g: Phaser.GameObjects.Graphics) => void): void {
  if (scene.textures.exists(key)) return;
  const graphics = scene.make.graphics({}, false);
  draw(graphics);
  graphics.generateTexture(key, TILE, TILE);
  graphics.destroy();
}

function drawAvatar(g: Phaser.GameObjects.Graphics, color: number, facing: string): void {
  g.fillStyle(0x000000, 0.2);
  g.fillEllipse(8, 14.5, 10, 3);
  g.fillStyle(color);
  g.fillRect(4, 8, 8, 6);
  g.fillStyle(0xf1c27d);
  g.fillCircle(8, 5.5, 4);
  g.fillStyle(0x4e342e);
  if (facing === 'up') {
    g.fillCircle(8, 5, 4);
    return;
  }
  g.fillRect(4, 1.5, 8, 2);
  g.fillStyle(0x212121);
  if (facing === 'down') {
    g.fillRect(6, 5, 1, 2);
    g.fillRect(9, 5, 1, 2);
  } else {
    g.fillRect(facing === 'left' ? 5 : 10, 5, 1, 2);
  }
}

/**
 * 確保場景已有指定主題所需的全部貼圖（地板、牆、家具、角色）；已存在的鍵名不會重畫。
 * @param scene 要登錄貼圖的 Phaser 場景。
 * @param theme 地圖主題（wood／carpet／dark）；未知主題以 wood 配色，但貼圖鍵名仍用傳入值。
 */
export function ensureTextures(scene: Phaser.Scene, theme: string): void {
  const palette = PALETTES[(theme in PALETTES ? theme : 'wood') as keyof typeof PALETTES];
  for (const zone of ['desk', 'meeting', 'pantry', 'entrance'] as const) {
    paint(scene, `floor-${theme}-${zone}`, (g) => {
      g.fillStyle(palette[zone]);
      g.fillRect(0, 0, TILE, TILE);
      g.lineStyle(1, 0x000000, 0.08);
      g.strokeRect(0.5, 0.5, TILE - 1, TILE - 1);
    });
  }
  paint(scene, `wall-${theme}`, (g) => {
    g.fillStyle(palette.wall);
    g.fillRect(0, 0, TILE, TILE);
    g.fillStyle(0xffffff, 0.08);
    g.fillRect(0, 0, TILE, 3);
  });
  for (const [kind, draw] of Object.entries(FURNITURE)) paint(scene, `furniture-${kind}`, draw);
  for (const [key, color] of Object.entries(AVATAR_COLORS)) {
    for (const facing of ['up', 'down', 'left', 'right']) paint(scene, `avatar-${key}-${facing}`, (g) => drawAvatar(g, color, facing));
  }
}
