import { describe, it, expect } from 'vitest';
import { computeSquarifiedTreemap, TreemapBounds } from './squarified-treemap.util';

describe('computeSquarifiedTreemap', () => {
  it('should return empty array for empty items or zero bounds', () => {
    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 500 };
    expect(computeSquarifiedTreemap([], bounds)).toEqual([]);
    expect(computeSquarifiedTreemap([{ value: 10 }], { x: 0, y: 0, width: 0, height: 500 })).toEqual([]);
  });

  it('should layout single item filling full container', () => {
    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 500 };
    const res = computeSquarifiedTreemap([{ value: 100 }], bounds);
    expect(res.length).toBe(1);
    expect(res[0].widthPct).toBeCloseTo(100, 1);
    expect(res[0].heightPct).toBeCloseTo(100, 1);
  });

  it('should layout multiple items without gaps or negative dimensions', () => {
    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 500 };
    const items = [
      { id: 'A', value: 500 },
      { id: 'B', value: 300 },
      { id: 'C', value: 200 }
    ];
    const res = computeSquarifiedTreemap(items, bounds);
    expect(res.length).toBe(3);

    for (const r of res) {
      expect(r.width).toBeGreaterThan(0);
      expect(r.height).toBeGreaterThan(0);
      expect(r.leftPct).toBeGreaterThanOrEqual(0);
      expect(r.topPct).toBeGreaterThanOrEqual(0);
      expect(r.leftPct + r.widthPct).toBeLessThanOrEqual(100.01);
      expect(r.topPct + r.heightPct).toBeLessThanOrEqual(100.01);
    }
  });

  it('should correctly partition 50 constituent items', () => {
    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 540 };
    const items = Array.from({ length: 50 }, (_, i) => ({
      symbol: `SYM_${i}`,
      value: Math.max(50000, Math.round(2000000 / (1 + i * 0.12)))
    }));

    const res = computeSquarifiedTreemap(items, bounds);
    expect(res.length).toBe(50);

    // Sum of areas should match container area
    const totalItemArea = res.reduce((sum, r) => sum + r.width * r.height, 0);
    const containerArea = bounds.width * bounds.height;
    expect(totalItemArea).toBeCloseTo(containerArea, -1);
  });

  it('should layout 8 top companies cleanly with optimal aspect ratios', () => {
    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 460 };
    const items = [
      { symbol: 'RELIANCE', value: 1980000 },
      { symbol: 'TCS', value: 1460000 },
      { symbol: 'HDFCBANK', value: 1320000 },
      { symbol: 'BHARTIARTL', value: 980000 },
      { symbol: 'ICICIBANK', value: 910000 },
      { symbol: 'INFY', value: 780000 },
      { symbol: 'SBIN', value: 740000 },
      { symbol: 'ITC', value: 610000 }
    ];

    const res = computeSquarifiedTreemap(items, bounds);
    expect(res.length).toBe(8);

    for (const r of res) {
      expect(r.width).toBeGreaterThan(100);
      expect(r.height).toBeGreaterThan(70);
      const aspect = Math.max(r.width / r.height, r.height / r.width);
      // squarified aspect ratio should be reasonable (typically under 3.5)
      expect(aspect).toBeLessThan(4.0);
    }
  });

  it('should layout 7 to 9 companies cleanly without overlaps', () => {
    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 460 };
    const allItems = [
      { symbol: 'RELIANCE', value: 1980000 },
      { symbol: 'TCS', value: 1460000 },
      { symbol: 'HDFCBANK', value: 1320000 },
      { symbol: 'BHARTIARTL', value: 980000 },
      { symbol: 'ICICIBANK', value: 910000 },
      { symbol: 'INFY', value: 780000 },
      { symbol: 'SBIN', value: 740000 },
      { symbol: 'ITC', value: 610000 },
      { symbol: 'LT', value: 510000 }
    ];

    for (const count of [7, 8, 9]) {
      const subset = allItems.slice(0, count);
      const res = computeSquarifiedTreemap(subset, bounds);
      expect(res.length).toBe(count);
      const totalArea = res.reduce((sum, r) => sum + r.width * r.height, 0);
      expect(totalArea).toBeCloseTo(bounds.width * bounds.height, -1);
      for (const r of res) {
        expect(r.width).toBeGreaterThan(80);
        expect(r.height).toBeGreaterThan(60);
      }
    }
  });

  it('should layout 11 and 12 companies with generous dimensions and no overlaps', () => {
    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 480 };
    const all12 = [
      { symbol: 'RELIANCE', value: 1980000 },
      { symbol: 'TCS', value: 1460000 },
      { symbol: 'HDFCBANK', value: 1320000 },
      { symbol: 'BHARTIARTL', value: 980000 },
      { symbol: 'ICICIBANK', value: 910000 },
      { symbol: 'INFY', value: 780000 },
      { symbol: 'SBIN', value: 740000 },
      { symbol: 'ITC', value: 610000 },
      { symbol: 'LT', value: 510000 },
      { symbol: 'HINDUNILVR', value: 490000 },
      { symbol: 'KOTAKBANK', value: 420000 },
      { symbol: 'AXISBANK', value: 380000 }
    ];

    for (const count of [11, 12]) {
      const subset = all12.slice(0, count);
      const res = computeSquarifiedTreemap(subset, bounds);
      expect(res.length).toBe(count);
      const totalArea = res.reduce((sum, r) => sum + r.width * r.height, 0);
      expect(totalArea).toBeCloseTo(bounds.width * bounds.height, -1);
      for (const r of res) {
        expect(r.width).toBeGreaterThan(70);
        expect(r.height).toBeGreaterThan(50);
      }
    }
  });
});

