/**
 * @file office-grid.ts
 * 地圖格子運算：可否通行、尋路、朝向，以及「訊息會傳給誰」的提示。
 * chatRecipients 與後端 OfficeRules.ChatRecipients 規則相同，只用於提示；實際收訊對象由後端決定。
 */
import type { Facing, OfficeMap, OfficeZone } from '@/shared/services/office-service';

/** 地圖上的格子座標（左上角為 0,0）。 */
export interface Point {
  x: number;
  y: number;
}

/** 一般聊天的切比雪夫距離上限，與後端 OfficeRules 一致。 */
export const CHAT_RANGE = 2;

/**
 * 判斷格子是否可走。
 * @param map 辦公室地圖。
 * @param x 格子 X。
 * @param y 格子 Y。
 * @returns 在地圖內且不是牆或家具時為 true。
 */
export function isWalkable(map: OfficeMap, x: number, y: number): boolean {
  return x >= 0 && y >= 0 && x < map.width && y < map.height && map.grid[y][x] === '.';
}

/**
 * 取得格子所在的區塊。
 * @param map 辦公室地圖。
 * @param x 格子 X。
 * @param y 格子 Y。
 * @returns 所屬區塊；不在任何區塊內時為 undefined。
 */
export function zoneAt(map: OfficeMap, x: number, y: number): OfficeZone | undefined {
  return map.zones.find((zone) => x >= zone.x && x < zone.x + zone.w && y >= zone.y && y < zone.y + zone.h);
}

/**
 * 取得上、右、下、左四個相鄰格子（不檢查是否可走）。
 * @param point 中心格子。
 * @returns 依上右下左排列的四個格子。
 */
export function neighbors(point: Point): Point[] {
  return [
    { x: point.x, y: point.y - 1 },
    { x: point.x + 1, y: point.y },
    { x: point.x, y: point.y + 1 },
    { x: point.x - 1, y: point.y },
  ];
}

/**
 * 四方向 BFS 尋路；目標不可走（家具）時走到它旁邊。
 * @param map 辦公室地圖。
 * @param from 起點。
 * @param to 目標。
 * @returns 不含起點的每一步；已在目標或走不到時為空陣列。
 */
export function findPath(map: OfficeMap, from: Point, to: Point): Point[] {
  const targetWalkable = isWalkable(map, to.x, to.y);
  const reached = (point: Point) => (targetWalkable ? point.x === to.x && point.y === to.y : Math.abs(point.x - to.x) + Math.abs(point.y - to.y) === 1);
  if (reached(from)) return [];
  const key = (point: Point) => point.y * map.width + point.x;
  const previous = new Map<number, Point | null>([[key(from), null]]);
  const queue: Point[] = [from];
  while (queue.length > 0) {
    const current = queue.shift()!;
    for (const next of neighbors(current)) {
      if (!isWalkable(map, next.x, next.y) || previous.has(key(next))) continue;
      previous.set(key(next), current);
      if (reached(next)) {
        const steps: Point[] = [];
        for (let point: Point | null = next; point && key(point) !== key(from); point = previous.get(key(point)) ?? null) steps.unshift(point);
        return steps;
      }
      queue.push(next);
    }
  }
  return [];
}

/**
 * 依移動方向計算角色朝向。
 * @param from 起點。
 * @param to 終點。
 * @returns 水平位移優先，其餘依垂直方向；原地時為 down。
 */
export function facingTo(from: Point, to: Point): Facing {
  if (to.x > from.x) return 'right';
  if (to.x < from.x) return 'left';
  return to.y < from.y ? 'up' : 'down';
}

function roomAt(map: OfficeMap, point: Point): OfficeZone | undefined {
  const zone = zoneAt(map, point.x, point.y);
  return zone && (zone.type === 'meeting' || zone.type === 'pantry') ? zone : undefined;
}

/**
 * 推算送出的聊天訊息會被誰收到（僅供提示）。
 * @param map 辦公室地圖。
 * @param sender 發話者的 membershipId。
 * @param positions 在場者的位置。
 * @returns 收訊者的 membershipId；在會議室／茶水間內只傳給同區塊，否則傳給不在這兩類區塊且距離不超過 CHAT_RANGE 的人。
 */
export function chatRecipients(map: OfficeMap, sender: string, positions: Map<string, Point>): string[] {
  const origin = positions.get(sender);
  if (!origin) return [];
  const room = roomAt(map, origin);
  return [...positions.entries()]
    .filter(([id, point]) => {
      if (id === sender) return false;
      if (room) return point.x >= room.x && point.x < room.x + room.w && point.y >= room.y && point.y < room.y + room.h;
      return !roomAt(map, point) && Math.max(Math.abs(point.x - origin.x), Math.abs(point.y - origin.y)) <= CHAT_RANGE;
    })
    .map(([id]) => id);
}

/**
 * 找出區塊內最靠近中心的可走格子（狀態自動走位用）。
 * @param map 辦公室地圖。
 * @param zone 目標區塊。
 * @returns 最靠近中心的可走格子；區塊內沒有可走格子時回傳區塊左上角。
 */
export function zoneCenter(map: OfficeMap, zone: OfficeZone): Point {
  const center = { x: zone.x + zone.w / 2, y: zone.y + zone.h / 2 };
  let best: Point = { x: zone.x, y: zone.y };
  let bestDistance = Number.POSITIVE_INFINITY;
  for (let y = zone.y; y < zone.y + zone.h; y++) {
    for (let x = zone.x; x < zone.x + zone.w; x++) {
      const distance = Math.abs(x + 0.5 - center.x) + Math.abs(y + 0.5 - center.y);
      if (isWalkable(map, x, y) && distance < bestDistance) {
        best = { x, y };
        bestDistance = distance;
      }
    }
  }
  return best;
}
