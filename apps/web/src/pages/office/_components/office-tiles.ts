/**
 * @file office-tiles.ts
 * Kenney CC0 素材（Roguelike Indoors 與 Roguelike Characters）與語意貼圖鍵的對照。
 * 沒有列在這裡的鍵（深色主題的地板與牆、冰箱等）使用 office-textures.ts 的程式繪製版本；對應的素材載入失敗時改用程式繪製。
 * 角色由 office-avatars.ts 依 OFFICE_AVATAR_LAYERS 把多層 frame 疊成單張貼圖。
 */

/** Kenney 素材包的 spritesheet 設定（16×16 圖塊、圖塊之間 1px 間隔，共 27 欄 18 列，frame = 列 × 27 + 欄）。 */
export const KENNEY_SHEETS = {
  indoor: { key: 'kenney-indoor', url: '/office/kenney/roguelike-indoors/roguelikeIndoor_transparent.png' },
  /** Roguelike Characters：16×16 圖塊、1px 間隔，共 54 欄 12 列（frame = 列 × 54 + 欄）；只有正面圖，且身體、衣服、髮型各自是獨立圖層。 */
  characters: { key: 'kenney-characters', url: '/office/kenney/roguelike-characters/roguelikeChar_transparent.png' },
} as const;

/**
 * 語意貼圖鍵 → indoor sheet 的 frame 編號。
 * 鍵名規則與 office-textures.ts 相同：floor-{theme}-{zone}、wall-{theme}、furniture-{kind}。
 */
export const OFFICE_TILE_FRAMES: Partial<Record<string, number>> = {
  // 木質主題：直紋木地板為主，會議區鋪橘色地毯，茶水間用方格地磚
  'floor-wood-desk': 32,
  'floor-wood-meeting': 24,
  'floor-wood-pantry': 5,
  'floor-wood-entrance': 32,
  'wall-wood': 147,
  // 地毯主題：綠色橫紋地毯，茶水間同樣用方格地磚；素材沒有第二種牆，沿用奶油色牆面
  'wall-carpet': 147,
  'floor-carpet-desk': 132,
  'floor-carpet-meeting': 159,
  'floor-carpet-pantry': 5,
  'floor-carpet-entrance': 132,
  // 家具（Kenney 沒有冰箱，furniture-fridge 與深色主題維持程式繪製）
  'furniture-desk': 58,
  'furniture-chair': 54,
  'furniture-meeting-table': 1,
  'furniture-coffee': 330,
  'furniture-counter': 324,
  'furniture-table': 7,
  'furniture-plant': 16,
  'furniture-whiteboard': 18,
  'furniture-rug': 425,
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
