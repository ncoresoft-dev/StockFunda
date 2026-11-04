/**
 * Squarified Treemap Layout Utility (Bruls, Huizing, van Wijk algorithm)
 * Generates responsive, non-overlapping rectangles with optimized golden aspect ratios.
 */

export interface TreemapBounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface TreemapLayoutItem<T> {
  item: T;
  x: number;
  y: number;
  width: number;
  height: number;
  leftPct: number;
  topPct: number;
  widthPct: number;
  heightPct: number;
}

/**
 * Computes squarified treemap bounding boxes for a list of items with numeric values.
 */
export function computeSquarifiedTreemap<T extends { value: number }>(
  rawItems: T[],
  bounds: TreemapBounds
): TreemapLayoutItem<T>[] {
  const validItems = (rawItems || []).filter(it => it && typeof it.value === 'number' && it.value > 0);
  if (validItems.length === 0 || bounds.width <= 0 || bounds.height <= 0) {
    return [];
  }

  const totalValue = validItems.reduce((acc, curr) => acc + curr.value, 0);
  if (totalValue <= 0) return [];

  const totalArea = bounds.width * bounds.height;
  const items = validItems.map(it => ({
    ...it,
    rawItem: it,
    area: (it.value / totalValue) * totalArea
  })).sort((a, b) => b.area - a.area);

  const rects: TreemapLayoutItem<T>[] = [];
  const currentRect: TreemapBounds = { ...bounds };
  let row: typeof items = [];

  function worst(rowItems: typeof items, length: number): number {
    if (rowItems.length === 0 || length <= 0) return Infinity;
    const s = rowItems.reduce((sum, it) => sum + it.area, 0);
    if (s <= 0) return Infinity;
    const s2 = s * s;
    const l2 = length * length;
    let maxAspect = 0;
    for (const it of rowItems) {
      const a = it.area;
      if (a <= 0) continue;
      const aspect = Math.max((l2 * a) / s2, s2 / (l2 * a));
      if (aspect > maxAspect) maxAspect = aspect;
    }
    return maxAspect;
  }

  function layoutRow(rowItems: typeof items, length: number, isHorizontal: boolean): void {
    const s = rowItems.reduce((sum, it) => sum + it.area, 0);
    if (s <= 0 || length <= 0) return;
    const thickness = s / length;
    let offset = 0;

    for (const it of rowItems) {
      const itemLen = thickness > 0 ? it.area / thickness : 0;
      const x = isHorizontal ? currentRect.x : currentRect.x + offset;
      const y = isHorizontal ? currentRect.y + offset : currentRect.y;
      const w = isHorizontal ? thickness : itemLen;
      const h = isHorizontal ? itemLen : thickness;

      rects.push({
        item: it.rawItem,
        x,
        y,
        width: w,
        height: h,
        leftPct: bounds.width > 0 ? (x / bounds.width) * 100 : 0,
        topPct: bounds.height > 0 ? (y / bounds.height) * 100 : 0,
        widthPct: bounds.width > 0 ? (w / bounds.width) * 100 : 0,
        heightPct: bounds.height > 0 ? (h / bounds.height) * 100 : 0
      });

      offset += itemLen;
    }

    if (isHorizontal) {
      currentRect.x += thickness;
      currentRect.width = Math.max(0, currentRect.width - thickness);
    } else {
      currentRect.y += thickness;
      currentRect.height = Math.max(0, currentRect.height - thickness);
    }
  }

  for (let i = 0; i < items.length; i++) {
    const it = items[i];
    const isHorizontal = currentRect.width > currentRect.height;
    const length = isHorizontal ? currentRect.height : currentRect.width;

    if (length <= 0) break;

    const rowWithNext = [...row, it];
    if (row.length === 0) {
      row.push(it);
    } else {
      const wCurrent = worst(row, length);
      const wNext = worst(rowWithNext, length);
      if (wNext <= wCurrent) {
        row.push(it);
      } else {
        layoutRow(row, length, isHorizontal);
        row = [it];
      }
    }
  }

  if (row.length > 0) {
    const isHorizontal = currentRect.width > currentRect.height;
    const length = isHorizontal ? currentRect.height : currentRect.width;
    if (length > 0) {
      layoutRow(row, length, isHorizontal);
    }
  }

  return rects;
}

