/**
 * @file office-avatars.ts
 * 把 Kenney Roguelike Characters 的分層 frame（身體、褲子、上衣、髮型）疊成單張 16×16 貼圖，
 * 登錄成場景原本使用的 avatar-{key}-{facing} 名稱，場景其餘程式不必知道素材來源。
 */
import type Phaser from 'phaser';
import { TILE } from './office-textures';
import { OFFICE_SHEETS, OFFICE_AVATAR_LAYERS, OFFICE_AVATAR_RECOLOR } from './office-tiles';

const FACINGS = ['up', 'down', 'left', 'right'] as const;
// 素材只有正面圖：朝上時把正面圖壓暗一點，與朝下做出區別。
const UP_BRIGHTNESS = 0.78;

function newCanvas(): { canvas: HTMLCanvasElement; ctx: CanvasRenderingContext2D } {
  const canvas = document.createElement('canvas');
  canvas.width = TILE;
  canvas.height = TILE;
  return { canvas, ctx: canvas.getContext('2d', { willReadFrequently: true })! };
}

function rotateHue(image: ImageData, degrees: number): void {
  const data = image.data;
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] === 0) continue;
    const r = data[i] / 255, g = data[i + 1] / 255, b = data[i + 2] / 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b), delta = max - min;
    const lightness = (max + min) / 2;
    if (delta === 0) continue;
    const saturation = delta / (1 - Math.abs(2 * lightness - 1));
    let hue = max === r ? ((g - b) / delta) % 6 : max === g ? (b - r) / delta + 2 : (r - g) / delta + 4;
    hue = (((hue * 60 + degrees) % 360) + 360) % 360;
    const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
    const x = chroma * (1 - Math.abs(((hue / 60) % 2) - 1));
    const m = lightness - chroma / 2;
    const [r1, g1, b1] = hue < 60 ? [chroma, x, 0] : hue < 120 ? [x, chroma, 0] : hue < 180 ? [0, chroma, x] : hue < 240 ? [0, x, chroma] : hue < 300 ? [x, 0, chroma] : [chroma, 0, x];
    data[i] = Math.round((r1 + m) * 255);
    data[i + 1] = Math.round((g1 + m) * 255);
    data[i + 2] = Math.round((b1 + m) * 255);
  }
}

/**
 * 依 OFFICE_AVATAR_LAYERS 為每個角色代號產生四個朝向的貼圖並登錄到場景。
 * 必須在 characters sheet 載入完成後、ensureTextures 之前呼叫；已存在的貼圖鍵名不會覆寫，所以程式繪製版本只會補上缺的鍵。
 * 朝向處理：down 與 right 使用正面圖，left 為水平翻轉，up 為壓暗的正面圖。
 * @param scene 已載入 characters sheet 的 Phaser 場景。
 */
export function composeKenneyAvatars(scene: Phaser.Scene): void {
  const texture = scene.textures.get(OFFICE_SHEETS.characters.key);
  const source = texture.getSourceImage() as CanvasImageSource;
  for (const [avatarKey, layers] of Object.entries(OFFICE_AVATAR_LAYERS)) {
    const front = newCanvas();
    for (const frameIndex of layers) {
      const frame = texture.get(frameIndex);
      const layer = newCanvas();
      layer.ctx.drawImage(source, frame.cutX, frame.cutY, TILE, TILE, 0, 0, TILE, TILE);
      const degrees = OFFICE_AVATAR_RECOLOR[avatarKey]?.[frameIndex];
      if (degrees) {
        const pixels = layer.ctx.getImageData(0, 0, TILE, TILE);
        rotateHue(pixels, degrees);
        layer.ctx.putImageData(pixels, 0, 0);
      }
      front.ctx.drawImage(layer.canvas, 0, 0);
    }
    for (const facing of FACINGS) {
      const out = newCanvas();
      if (facing === 'left') {
        out.ctx.translate(TILE, 0);
        out.ctx.scale(-1, 1);
      }
      out.ctx.drawImage(front.canvas, 0, 0);
      if (facing === 'up') {
        const pixels = out.ctx.getImageData(0, 0, TILE, TILE);
        for (let i = 0; i < pixels.data.length; i += 4) {
          pixels.data[i] *= UP_BRIGHTNESS;
          pixels.data[i + 1] *= UP_BRIGHTNESS;
          pixels.data[i + 2] *= UP_BRIGHTNESS;
        }
        out.ctx.putImageData(pixels, 0, 0);
      }
      scene.textures.addCanvas(`avatar-${avatarKey}-${facing}`, out.canvas);
    }
  }
}
