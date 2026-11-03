/**
 * @file office-tiles.ts
 * Kenney CC0 素材（Roguelike Indoors）與語意貼圖鍵的對照。
 * 沒有列在這裡的鍵（深色主題的地板與牆、冰箱等）使用 office-textures.ts 的程式繪製版本；素材載入失敗時全部改用程式繪製。
 * 角色沒有 Kenney 素材（未取得 Roguelike Characters），一律使用程式繪製。
 */

/** Kenney 素材包的 spritesheet 設定（16×16 圖塊、圖塊之間 1px 間隔，共 27 欄 18 列，frame = 列 × 27 + 欄）。 */
export const KENNEY_SHEETS = {
  indoor: { key: 'kenney-indoor', url: '/office/kenney/roguelike-indoors/roguelikeIndoor_transparent.png' },
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
