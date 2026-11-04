/**
 * @file office-textures.ts
 * 以程式繪製 16×16 圖塊與角色；office-tiles.ts 的圖塊或角色素材載入失敗時作為備援。
 * 貼圖鍵名：floor-{theme}-{zone}、wall-{theme}、furniture-{kind}、avatar-{key}-{facing}。
 */
import type Phaser from 'phaser';

/** 單一圖塊的像素邊長。 */
export const TILE = 16;

const PALETTES = {
  wood: { desk: 0xdcc3a1, meeting: 0x3b6e8c, pantry: 0xf2ece1, entrance: 0xd8cfc2, wall: 0x4a5568 },
  carpet: { desk: 0x64748b, meeting: 0x334155, pantry: 0xe2e8f0, entrance: 0x94a3b8, wall: 0x1e293b },
  dark: { desk: 0x262b36, meeting: 0x1e293b, pantry: 0x333947, entrance: 0x1f242d, wall: 0x0f172a },
  park: { desk: 0xe3d2b4, meeting: 0x4d7c5d, pantry: 0xede6d6, entrance: 0xc7d3c0, wall: 0x365314 },
  space: { desk: 0x475569, meeting: 0x1e3a5f, pantry: 0xdbeafe, entrance: 0x334155, wall: 0x0f172a },
} as const;

/** 角色代號對應的衣服顏色：角色選擇按鈕的顏色，也是程式繪製角色的衣服色；Kenney 角色的衣服色系依此調整（見 office-tiles.ts 的 OFFICE_AVATAR_RECOLOR）。鍵名須與 OFFICE_AVATARS 一致。 */
export const AVATAR_COLORS: Record<string, number> = {
  a1: 0xe57373, a2: 0x64b5f6, a3: 0x81c784, a4: 0xffb74d,
  a5: 0xba68c8, a6: 0x4db6ac, a7: 0xf06292, a8: 0x90a4ae,
};

const FURNITURE: Record<string, (g: Phaser.GameObjects.Graphics) => void> = {
  desk: (g) => { g.fillStyle(0xf1f5f9); g.fillRect(0, 2, 16, 10); g.fillStyle(0x1e293b); g.fillRect(2, 1, 5, 5); g.fillRect(9, 1, 5, 5); g.fillStyle(0x38bdf8); g.fillRect(3, 2, 3, 3); g.fillRect(10, 2, 3, 3); },
  'desk-side': (g) => { g.fillStyle(0xf1f5f9); g.fillRect(0, 2, 16, 10); g.fillStyle(0x3b82f6); g.fillRect(2, 4, 5, 5); g.fillStyle(0xef4444); g.fillRect(10, 5, 3, 3); },
  chair: (g) => { g.fillStyle(0x0284c7); g.fillRoundedRect(4, 4, 8, 8, 2); },
  'meeting-table': (g) => { g.fillStyle(0x92400e); g.fillRect(0, 1, 16, 13); },
  coffee: (g) => { g.fillStyle(0x424242); g.fillRect(3, 2, 10, 12); g.fillStyle(0xd32f2f); g.fillRect(5, 4, 6, 3); g.fillStyle(0xffffff); g.fillRect(6, 10, 4, 3); },
  fridge: (g) => { g.fillStyle(0xeceff1); g.fillRect(3, 0, 10, 16); g.fillStyle(0xb0bec5); g.fillRect(3, 6, 10, 1); g.fillRect(11, 2, 1, 3); },
  counter: (g) => { g.fillStyle(0x90a4ae); g.fillRect(0, 2, 16, 13); g.fillStyle(0xcfd8dc); g.fillRect(0, 2, 16, 3); },
  water: (g) => { g.fillStyle(0x38bdf8); g.fillRect(5, 0, 6, 6); g.fillStyle(0xf8fafc); g.fillRect(4, 6, 8, 9); },
  table: (g) => { g.fillStyle(0xf59e0b); g.fillRect(2, 2, 12, 11); },
  sofa: (g) => { g.fillStyle(0x0f766e); g.fillRect(1, 2, 14, 11); g.fillStyle(0x14b8a6); g.fillRect(3, 6, 10, 6); },
  bookshelf: (g) => { g.fillStyle(0x78350f); g.fillRect(1, 0, 14, 15); g.fillStyle(0x3b82f6); g.fillRect(3, 2, 10, 4); g.fillStyle(0xf59e0b); g.fillRect(3, 8, 10, 4); },
  plant: (g) => { g.fillStyle(0xf8fafc); g.fillRect(5, 10, 6, 5); g.fillStyle(0x22c55e); g.fillCircle(8, 7, 4); g.fillCircle(5, 6, 3); g.fillCircle(11, 6, 3); },
  whiteboard: (g) => { g.fillStyle(0xffffff); g.fillRect(1, 3, 14, 9); g.lineStyle(1, 0x334155); g.strokeRect(1.5, 3.5, 13, 8); g.fillStyle(0x3b82f6); g.fillRect(3, 6, 6, 2); },
  rug: (g) => { g.fillStyle(0xb91c1c, 0.85); g.fillRect(1, 2, 14, 12); },
  cabinet: (g) => { g.fillStyle(0xcbd5e1); g.fillRect(2, 7, 12, 8); g.fillStyle(0x334155); g.fillRect(3, 2, 10, 5); },
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
 * @param theme 地圖主題（wood／carpet／dark／park／space）；未知主題以 wood 配色，但貼圖鍵名仍用傳入值。
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
