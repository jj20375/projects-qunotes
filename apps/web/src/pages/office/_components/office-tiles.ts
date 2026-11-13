/**
 * @file office-tiles.ts
 * 專案自製的辦公室圖塊（AI 協助以腳本逐像素繪製，CC0）與 Kenney 角色圖資（CC0）與語意貼圖鍵的對照。
 * 對應的素材載入失敗時改用 office-textures.ts 的程式繪製版本。
 * 角色由 office-avatars.ts 依 OFFICE_AVATAR_LAYERS 把多層 frame 疊成單張貼圖。
 */
import { withBasePath } from '@/shared/utils/base-path';

/** spritesheet 設定（16×16 圖塊、圖塊之間 1px 間隔）。 */
export const OFFICE_SHEETS = {
  /** 專案自製的辦公室圖塊（授權見該資料夾的 License.txt）：16×16 圖塊、1px 間隔，共 10 欄 5 列（frame = 列 × 10 + 欄）。 */
  tileset: { key: 'office-tileset', url: withBasePath('/office/office-tileset/office_tileset.png') },
  /** Roguelike Characters：16×16 圖塊、1px 間隔，共 54 欄 12 列（frame = 列 × 54 + 欄）；只有正面圖，且身體、衣服、髮型各自是獨立圖層。 */
  characters: { key: 'kenney-characters', url: withBasePath('/office/kenney/roguelike-characters/roguelikeChar_transparent.png') },
} as const;

/**
 * 語意貼圖鍵 → 辦公室圖塊（OFFICE_SHEETS.tileset）的 frame 編號。
 * 鍵名規則與 office-textures.ts 相同：floor-{theme}-{zone}、wall-{theme}、furniture-{kind}。
 */
export const OFFICE_TILE_FRAMES: Partial<Record<string, number>> = {
  // 現代木質主題（wood）
  'floor-wood-desk': 0,
  'floor-wood-meeting': 1,
  'floor-wood-pantry': 2,
  'floor-wood-entrance': 3,
  'wall-wood': 4,
  // 商務灰藍主題（carpet）
  'floor-carpet-desk': 5,
  'floor-carpet-meeting': 6,
  'floor-carpet-pantry': 7,
  'floor-carpet-entrance': 8,
  'wall-carpet': 9,
  // 極客深色主題（dark）
  'floor-dark-desk': 10,
  'floor-dark-meeting': 11,
  'floor-dark-pantry': 12,
  'floor-dark-entrance': 13,
  'wall-dark': 14,
  // 綠意新創主題（park）
  'floor-park-desk': 15,
  'floor-park-meeting': 16,
  'floor-park-pantry': 17,
  'floor-park-entrance': 18,
  'wall-park': 19,
  // 星際太空艙主題（space）
  'floor-space-desk': 20,
  'floor-space-meeting': 21,
  'floor-space-pantry': 22,
  'floor-space-entrance': 23,
  'wall-space': 24,
  // 現代辦公室家具
  'furniture-desk': 30,
  'furniture-desk-side': 31,
  'furniture-chair': 32,
  'furniture-meeting-table': 33,
  'furniture-coffee': 34,
  'furniture-fridge': 35,
  'furniture-counter': 36,
  'furniture-water': 37,
  'furniture-table': 38,
  'furniture-sofa': 39,
  'furniture-bookshelf': 40,
  'furniture-plant': 41,
  'furniture-whiteboard': 42,
  'furniture-rug': 43,
  'furniture-cabinet': 44,
};

/**
 * 角色代號 → characters sheet 的圖層 frame 編號，由下往上疊：身體膚色、褲子與鞋、上衣、髮型。
 * 衣服色系對應 office-textures.ts 的 AVATAR_COLORS（見 OFFICE_AVATAR_RECOLOR），讓角色選擇按鈕的顏色仍能代表角色。
 */
export const OFFICE_AVATAR_LAYERS: Record<string, number[]> = {
  a1: [0, 3, 7, 19],
  a2: [54, 3, 11, 239],
  a3: [0, 57, 277, 21],
  a4: [108, 3, 6, 23],
  a5: [54, 3, 15, 236],
  a6: [0, 3, 11, 21],
  a7: [54, 3, 15, 20],
  a8: [0, 3, 286, 452],
};

/**
 * 角色代號 → { 圖層 frame 編號: 色相旋轉角度 }。
 * 素材只有橘、青綠、薰衣草紫、綠與灰色系的上衣，紅、藍、粉紅要靠旋轉衣服圖層的色相取得；膚色與髮型維持原色。
 */
export const OFFICE_AVATAR_RECOLOR: Record<string, Partial<Record<number, number>>> = {
  a1: { 7: -20 },
  a2: { 11: 32 },
  a4: { 6: 12 },
  a7: { 15: 70 },
};
