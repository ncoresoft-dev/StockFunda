import { Component, OnInit, OnDestroy, inject, signal, computed, effect, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { StockIndexService } from '../../services/stock-index.service';
import { WatchlistService } from '../../services/watchlist.service';
import {
  Nifty50StockItem,
  IndexAttributionResponse,
  IndexContributor,
  IndexChartSlice,
  IndexTugOfWar
} from '../../models/stock-index.model';

import { AdminAnnualReportModalComponent } from '../admin-annual-report-modal/admin-annual-report-modal.component';
import { HeaderComponent } from '../header/header.component';

export type SortField = 'weightage' | 'symbol' | 'name' | 'sector' | 'industry';
export type SortOrder = 'asc' | 'desc';
export type ViewTab = 'nifty50' | 'sensex' | 'banknifty' | 'watchlist';
export type ViewMode = 'circle' | 'waterfall';
export type ContributorFilter = 'all' | 'lifting' | 'dragging';

export interface SvgDonutSlice {
  slice: IndexChartSlice;
  path: string;
  fill: string;
  stroke: string;
  labelX: number;
  labelY: number;
  displaySymbol: string;
  displayPoints: string;
  symbolFontSize: number;
  pointsFontSize: number;
  showSymbol: boolean;
  showPoints: boolean;
  clipPathId: string;
  startAngle: number;
  endAngle: number;
}

export interface DonutTooltipData {
  x: number;
  y: number;
  isOther: boolean;
  symbol: string;
  companyName: string;
  sector?: string;
  points: number;
  formattedPoints: string;
  isLifter: boolean;
  changePercent?: number;
  formattedChangePercent?: string;
  weightage?: number;
  price?: number;
  otherCount?: number;
  percentageOfCategory?: number;
}

@Component({
  selector: 'app-nifty50-landing',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminAnnualReportModalComponent, HeaderComponent],
  templateUrl: './nifty50-landing.component.html',
  styleUrl: './nifty50-landing.component.css'
})
export class Nifty50LandingComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly indexService = inject(StockIndexService);
  readonly watchlistService = inject(WatchlistService);

  // Active View Tab: 'nifty50', 'sensex', 'banknifty', or 'watchlist'
  activeTab = signal<ViewTab>('nifty50');

  // Display Name of Currently Active Tab
  currentTabTitle = computed(() => {
    const tab = this.activeTab();
    if (tab === 'sensex') return 'BSE SENSEX';
    if (tab === 'banknifty') return 'BANK NIFTY';
    if (tab === 'watchlist') return 'My Watchlist';
    return 'NIFTY 50';
  });

  // Visualizer Mode for Left Card: Circle (Donut), Waterfall, Heatmap
  viewMode = signal<ViewMode>('circle');

  // Attribution Data & Loading State
  attributionData = signal<IndexAttributionResponse | null>(null);
  isAttributionLoading = signal<boolean>(false);
  hoveredSliceId = signal<string | null>(null);
  activeTooltip = signal<DonutTooltipData | null>(null);

  // Contributors Filter & Search in Right Panel
  contributorFilter = signal<ContributorFilter>('all');
  contributorSearch = signal<string>('');

  // Theme
  isLightTheme = signal<boolean>(true);

  private queryParamsSub?: Subscription;
  private attributionSub?: Subscription;

  // Table Data & Filtering
  allStocks = signal<Nifty50StockItem[]>([]);
  sensexStocks = signal<Nifty50StockItem[]>([]);
  bankNiftyStocks = signal<Nifty50StockItem[]>([]);
  isLoading = signal<boolean>(true);
  tableSearchQuery = signal<string>('');
  selectedSector = signal<string>('All');
  sortField = signal<SortField>('weightage');
  sortOrder = signal<SortOrder>('desc');

  // Watchlist View State & Controls
  watchlistViewMode = signal<'cards'>('cards');
  watchlistFilterQuery = signal<string>('');
  watchlistSortField = signal<'sno' | 'name' | 'sector' | 'price' | 'changePercent' | 'marketCap'>('sno');
  watchlistSortOrder = signal<'asc' | 'desc'>('desc');
  isManageWatchlistModalOpen = signal<boolean>(false);
  manageWatchlistSearchQuery = signal<string>('');

  // Filtered & Sorted Watchlist items for display
  watchlistDisplayStocks = computed(() => {
    let list = this.currentBaseStocks();
    if (this.activeTab() !== 'watchlist') return list;

    const q = this.watchlistFilterQuery().trim().toLowerCase();
    if (q) {
      list = list.filter(s =>
        s.symbol.toLowerCase().includes(q) ||
        s.companyName.toLowerCase().includes(q) ||
        (s.sector && s.sector.toLowerCase().includes(q))
      );
    }

    const field = this.watchlistSortField();
    const order = this.watchlistSortOrder();
    const multiplier = order === 'asc' ? 1 : -1;

    if (field === 'sno') {
      return list;
    }

    return [...list].sort((a, b) => {
      if (field === 'name') {
        const aVal = a.companyName || a.symbol;
        const bVal = b.companyName || b.symbol;
        return aVal.localeCompare(bVal) * multiplier;
      }
      if (field === 'sector') {
        const aVal = a.sector || '';
        const bVal = b.sector || '';
        return aVal.localeCompare(bVal) * multiplier;
      }
      let aNum = 0;
      let bNum = 0;
      if (field === 'price') {
        aNum = a.price ?? 0;
        bNum = b.price ?? 0;
      } else if (field === 'changePercent') {
        aNum = a.changePercent ?? 0;
        bNum = b.changePercent ?? 0;
      } else if (field === 'marketCap') {
        aNum = a.marketCap ?? 0;
        bNum = b.marketCap ?? 0;
      }
      return (aNum - bNum) * multiplier;
    });
  });

  // Dynamic Watchlist Summary Statistics (Top Pulse Card)
  watchlistStats = computed(() => {
    const list = this.currentBaseStocks();
    const total = list.length;
    if (total === 0) {
      return {
        total: 0,
        upCount: 0,
        downCount: 0,
        neutralCount: 0,
        upPct: 50,
        downPct: 50,
        avgChange: 0,
        bestStock: null as Nifty50StockItem | null,
        weakestStock: null as Nifty50StockItem | null,
        narrative: 'Your watchlist is currently empty.'
      };
    }

    let up = 0;
    let down = 0;
    let neutral = 0;
    let sumChange = 0;
    let countWithChange = 0;
    let best: Nifty50StockItem | null = null;
    let weakest: Nifty50StockItem | null = null;

    for (const s of list) {
      const chg = s.changePercent ?? 0;
      if (s.changePercent != null) {
        sumChange += chg;
        countWithChange++;
      }
      if (chg > 0) up++;
      else if (chg < 0) down++;
      else neutral++;

      if (best === null || chg > (best.changePercent ?? -999)) best = s;
      if (weakest === null || chg < (weakest.changePercent ?? 999)) weakest = s;
    }

    const avg = countWithChange > 0 ? sumChange / countWithChange : 0;
    const upPct = total > 0 ? Math.round((up / total) * 100) : 50;
    const downPct = 100 - upPct;

    let leadText = '';
    if (up > down) {
      leadText = `Gainers lead ${up} to ${down}.`;
    } else if (down > up) {
      leadText = `Losers lead ${down} to ${up}.`;
    } else {
      leadText = `Equally balanced (${up} gainers, ${down} losers).`;
    }

    let bestText = '';
    if (best) {
      const b: Nifty50StockItem = best;
      const bChg = b.changePercent ?? 0;
      bestText = `Best today: ${b.companyName || b.symbol} ${bChg >= 0 ? '+' : ''}${bChg.toFixed(2)}%.`;
    }

    let weakestText = '';
    if (weakest && weakest !== best) {
      const w: Nifty50StockItem = weakest;
      const wChg = w.changePercent ?? 0;
      weakestText = `Weakest: ${w.companyName || w.symbol} ${wChg >= 0 ? '+' : ''}${wChg.toFixed(2)}%.`;
    }

    const narrative = `${leadText} ${bestText} ${weakestText}`.trim();

    return {
      total,
      upCount: up,
      downCount: down,
      neutralCount: neutral,
      upPct,
      downPct,
      avgChange: avg,
      bestStock: best,
      weakestStock: weakest,
      narrative
    };
  });

  // Dynamic search suggestions for Manage Watchlist modal
  manageWatchlistSuggestions = computed(() => {
    const q = this.manageWatchlistSearchQuery().trim().toLowerCase();
    const all = [
      ...this.allStocks(),
      ...this.sensexStocks(),
      ...this.bankNiftyStocks(),
      ...Array.from(this.customStocksMap().values())
    ];
    const unique = new Map<string, Nifty50StockItem>();
    all.forEach(s => {
      const sym = s.symbol.toUpperCase();
      if (!unique.has(sym)) unique.set(sym, s);
    });

    const list = Array.from(unique.values());
    if (!q) return list.slice(0, 15);
    return list.filter(s =>
      s.symbol.toLowerCase().includes(q) ||
      s.companyName.toLowerCase().includes(q) ||
      (s.sector && s.sector.toLowerCase().includes(q))
    ).slice(0, 25);
  });

  // Sector Categories (dynamically computes from the currently selected index or watchlist)
  sectors = computed(() => {
    const list = this.currentBaseStocks();
    const set = new Set<string>();
    list.forEach(s => {
      if (s.sector) set.add(s.sector);
    });
    return ['All', ...Array.from(set).sort()];
  });

  // Sector Breakdown with Counts (sorted by count desc)
  sectorStats = computed(() => {
    const list = this.currentBaseStocks();
    const map = new Map<string, number>();
    list.forEach(s => {
      if (s.sector) {
        map.set(s.sector, (map.get(s.sector) || 0) + 1);
      }
    });
    return Array.from(map.entries())
      .sort((a, b) => b[1] - a[1])
      .map(([name, count]) => ({ name, count }));
  });

  // Top 6 Major Sectors for quick 1-click chip access without scrollbars
  topSectors = computed(() => this.sectorStats().slice(0, 6));

  isTopSector(sector: string): boolean {
    return this.topSectors().some(s => s.name === sector);
  }

  // Cache of non-Nifty stocks enriched with logo, live price, and market cap
  readonly customStocksMap = signal<Map<string, Nifty50StockItem>>(new Map());

  private lastFetchedCustomSymbolsKey: string = '';
  private isFetchingCustomStocks: boolean = false;
  private customStocksSub?: Subscription;

  constructor() {
    effect(() => {
      const tab = this.activeTab();
      const symbols = this.watchlistService.watchlistSymbols();
      if (tab === 'watchlist' && symbols.length > 0) {
        untracked(() => {
          this.loadCustomWatchlistStocks();
        });
      }
    });
  }

  // Base list depending on active tab
  currentBaseStocks = computed(() => {
    const tab = this.activeTab();

    if (tab === 'banknifty') {
      const niftyMap = new Map(this.allStocks().map(s => [s.symbol.toUpperCase(), s]));
      return this.bankNiftyStocks().map(b => {
        const nStock = niftyMap.get(b.symbol.toUpperCase());
        if (nStock && (b.price == null || b.marketCap == null)) {
          return {
            ...b,
            price: b.price ?? nStock.price,
            marketCap: b.marketCap ?? nStock.marketCap,
            logoUrl: b.logoUrl || nStock.logoUrl,
            sector: b.sector || nStock.sector
          };
        }
        return b;
      });
    }

    if (tab === 'sensex') {
      const niftyMap = new Map(this.allStocks().map(s => [s.symbol.toUpperCase(), s]));
      return this.sensexStocks().map(b => {
        const nStock = niftyMap.get(b.symbol.toUpperCase());
        if (nStock && (b.price == null || b.marketCap == null)) {
          return {
            ...b,
            price: b.price ?? nStock.price,
            marketCap: b.marketCap ?? nStock.marketCap,
            logoUrl: b.logoUrl || nStock.logoUrl,
            sector: b.sector || nStock.sector
          };
        }
        return b;
      });
    }

    if (tab === 'watchlist') {
      const symbols = this.watchlistService.watchlistSymbols();
      const allKnown = [...this.allStocks(), ...this.sensexStocks(), ...this.bankNiftyStocks()];
      const foundMap = new Map(allKnown.map(s => [s.symbol.toUpperCase(), s]));

      return symbols.map(sym => {
        const upper = sym.toUpperCase();
        const base = foundMap.get(upper);
        const custom = this.customStocksMap().get(upper);
        const meta = this.watchlistService.getMeta(upper);

        const chg = custom?.change ?? meta?.change;
        const chgPct = custom?.changePercent ?? meta?.changePercent;
        const isPos = custom?.isPositive ?? meta?.isPositive ?? (chgPct != null ? chgPct >= 0 : (chg != null ? chg >= 0 : true));

        return {
          symbol: upper,
          companyName: custom?.companyName || base?.companyName || meta?.companyName || `${upper} Ltd`,
          sector: custom?.sector || base?.sector || meta?.sector || 'Equities',
          industry: base?.industry || 'Listed',
          logoUrl: custom?.logoUrl || base?.logoUrl || meta?.logoUrl,
          websiteUrl: custom?.websiteUrl || base?.websiteUrl,
          price: custom?.price ?? base?.price ?? meta?.price,
          marketCap: custom?.marketCap ?? base?.marketCap ?? meta?.marketCap,
          change: chg,
          changePercent: chgPct,
          isPositive: isPos,
          trendPath: custom?.trendPath ?? meta?.trendPath,
          sevenDayPrices: custom?.sevenDayPrices,
          weightagePercentage: base?.weightagePercentage,
          isin: base?.isin,
          exchange: base?.exchange || 'NSE'
        } as Nifty50StockItem;
      });
    }

    return this.allStocks();
  });

  // Filtered & Sorted Stocks for Display
  filteredStocks = computed(() => {
    let list = this.currentBaseStocks();
    const query = this.tableSearchQuery().trim().toLowerCase();
    const sector = this.selectedSector();

    // Filter by search query
    if (query) {
      list = list.filter(s =>
        s.symbol.toLowerCase().includes(query) ||
        s.companyName.toLowerCase().includes(query) ||
        (s.sector && s.sector.toLowerCase().includes(query)) ||
        (s.industry && s.industry.toLowerCase().includes(query)) ||
        (s.isin && s.isin.toLowerCase().includes(query))
      );
    }

    // Filter by sector (active for NIFTY 50, SENSEX, and BANK NIFTY tabs)
    if (sector !== 'All' && (this.activeTab() === 'nifty50' || this.activeTab() === 'sensex' || this.activeTab() === 'banknifty')) {
      list = list.filter(s => s.sector === sector);
    }

    // Sort
    const field = this.sortField();
    const order = this.sortOrder();
    const multiplier = order === 'asc' ? 1 : -1;

    return [...list].sort((a, b) => {
      if (field === 'symbol') {
        return a.symbol.localeCompare(b.symbol) * multiplier;
      }
      if (field === 'name') {
        return a.companyName.localeCompare(b.companyName) * multiplier;
      }
      if (field === 'sector') {
        return (a.sector || '').localeCompare(b.sector || '') * multiplier;
      }
      if (field === 'industry') {
        return (a.industry || '').localeCompare(b.industry || '') * multiplier;
      }
      if (field === 'weightage') {
        return ((a.weightagePercentage || 0) - (b.weightagePercentage || 0)) * multiplier;
      }
      return 0;
    });
  });

  // Market Summary Stats
  marketStats = computed(() => {
    const list = this.currentBaseStocks();
    const total = list.length;
    const sectorsCount = this.sectors().length > 1 ? this.sectors().length - 1 : 0;

    let topWeighted: Nifty50StockItem | null = null;
    if (list.length > 0) {
      const sortedByWeight = [...list].sort((a, b) => (b.weightagePercentage || 0) - (a.weightagePercentage || 0));
      topWeighted = sortedByWeight[0];
    }

    const isBankNifty = this.activeTab() === 'banknifty';
    const isSensex = this.activeTab() === 'sensex';
    return {
      totalConstituents: total,
      sectorsCount,
      topWeighted,
      exchange: isSensex ? 'BSE' : 'NSE',
      category: isBankNifty ? 'NSE Banking Benchmark' : (isSensex ? 'BSE Benchmark Index' : 'Broad Market Benchmark')
    };
  });

  // Contributors List Filtered for Display in Right Card
  filteredContributors = computed<IndexContributor[]>(() => {
    const data = this.attributionData();
    if (!data || !data.contributors) return [];

    let list = data.contributors;
    const filter = this.contributorFilter();
    const query = this.contributorSearch().trim().toLowerCase();

    if (filter === 'lifting') {
      list = list.filter(c => c.isLifter);
    } else if (filter === 'dragging') {
      list = list.filter(c => !c.isLifter);
    }

    if (query) {
      list = list.filter(c =>
        c.symbol.toLowerCase().includes(query) ||
        c.companyName.toLowerCase().includes(query) ||
        (c.sector && c.sector.toLowerCase().includes(query))
      );
    }

    return list;
  });

  // SVG Donut Slices Math for Left Circle Card
  donutSlices = computed<SvgDonutSlice[]>(() => {
    const data = this.attributionData();
    if (!data || !data.circleSlices || data.circleSlices.length === 0) return [];

    const slices = data.circleSlices;
    const lifters = slices.filter(s => s.isLifter);
    const draggers = slices.filter(s => !s.isLifter);

    const totalLiftAbs = lifters.reduce((sum, s) => sum + Math.abs(s.points), 0) || 1;
    const totalDragAbs = draggers.reduce((sum, s) => sum + Math.abs(s.points), 0) || 1;
    const totalAbs = totalLiftAbs + totalDragAbs;

    // Green starts at 0 deg (12 o'clock) and sweeps clockwise
    // Red continues clockwise from where green ends back to 360 deg
    let greenSpan = (totalLiftAbs / totalAbs) * 360;
    greenSpan = Math.max(50, Math.min(310, greenSpan));
    const redSpan = 360 - greenSpan;

    const cx = 240;
    const cy = 240;
    const R = 182; // Outer radius (expanded from 175 for optimal ring thickness)
    const r = 114; // Inner radius
    const W_radial = R - r; // 68px
    const r_mid = (R + r) / 2; // 148px
    const gap = 1.4; // Separation gap in degrees

    const polarToCart = (angleDeg: number, radius: number) => {
      const rad = ((angleDeg - 90) * Math.PI) / 180;
      return {
        x: Math.round((cx + radius * Math.cos(rad)) * 100) / 100,
        y: Math.round((cy + radius * Math.sin(rad)) * 100) / 100
      };
    };

    const createArcPath = (startDeg: number, endDeg: number) => {
      const adjustedStart = startDeg + gap / 2;
      const adjustedEnd = endDeg - gap / 2;
      const sweep = Math.max(0.1, adjustedEnd - adjustedStart);

      const p1 = polarToCart(adjustedStart, R);
      const p2 = polarToCart(adjustedEnd, R);
      const p3 = polarToCart(adjustedEnd, r);
      const p4 = polarToCart(adjustedStart, r);

      const largeArc = sweep > 180 ? 1 : 0;
      return `M ${p1.x} ${p1.y} A ${R} ${R} 0 ${largeArc} 1 ${p2.x} ${p2.y} L ${p3.x} ${p3.y} A ${r} ${r} 0 ${largeArc} 0 ${p4.x} ${p4.y} Z`;
    };

    const result: SvgDonutSlice[] = [];

    // 1. Lifters (Green Slices)
    let currentAngle = 0;
    lifters.forEach((slice, idx) => {
      const share = Math.abs(slice.points) / totalLiftAbs;
      const sweep = share * greenSpan;
      const startDeg = currentAngle;
      const endDeg = currentAngle + sweep;
      currentAngle = endDeg;

      const midDeg = (startDeg + endDeg) / 2;
      const midPos = polarToCart(midDeg, r_mid);

      const fill = slice.isOther ? '#134e40' : (idx === 0 ? '#00e599' : '#00c582');
      const stroke = slice.isOther ? '#1a6b57' : '#009e66';

      const layout = this.calculateSliceLabelLayout(
        slice.label,
        slice.points,
        slice.isOther,
        sweep,
        midDeg,
        W_radial,
        r_mid
      );
      const clipPathId = 'slice-clip-' + slice.id.replace(/[^a-zA-Z0-9_-]/g, '_');

      result.push({
        slice,
        path: createArcPath(startDeg, endDeg),
        fill,
        stroke,
        labelX: midPos.x,
        labelY: midPos.y,
        displaySymbol: layout.displaySymbol,
        displayPoints: layout.displayPoints,
        symbolFontSize: layout.symbolFontSize,
        pointsFontSize: layout.pointsFontSize,
        showSymbol: layout.showSymbol,
        showPoints: layout.showPoints,
        clipPathId,
        startAngle: startDeg,
        endAngle: endDeg
      });
    });

    // 2. Draggers (Red Slices)
    draggers.forEach((slice, idx) => {
      const share = Math.abs(slice.points) / totalDragAbs;
      const sweep = share * redSpan;
      const startDeg = currentAngle;
      const endDeg = currentAngle + sweep;
      currentAngle = endDeg;

      const midDeg = (startDeg + endDeg) / 2;
      const midPos = polarToCart(midDeg, r_mid);

      const fill = slice.isOther ? '#4c1d24' : (idx === 0 ? '#ff4d5a' : '#eb3b48');
      const stroke = slice.isOther ? '#6b2933' : '#c92a37';

      const layout = this.calculateSliceLabelLayout(
        slice.label,
        slice.points,
        slice.isOther,
        sweep,
        midDeg,
        W_radial,
        r_mid
      );
      const clipPathId = 'slice-clip-' + slice.id.replace(/[^a-zA-Z0-9_-]/g, '_');

      result.push({
        slice,
        path: createArcPath(startDeg, endDeg),
        fill,
        stroke,
        labelX: midPos.x,
        labelY: midPos.y,
        displaySymbol: layout.displaySymbol,
        displayPoints: layout.displayPoints,
        symbolFontSize: layout.symbolFontSize,
        pointsFontSize: layout.pointsFontSize,
        showSymbol: layout.showSymbol,
        showPoints: layout.showPoints,
        clipPathId,
        startAngle: startDeg,
        endAngle: endDeg
      });
    });

    return result;
  });

  // Calculate dynamic text truncation and font sizing for donut slice labels
  private calculateSliceLabelLayout(
    label: string,
    points: number,
    isOther: boolean,
    sweep: number,
    midDeg: number,
    W_radial: number,
    r_mid: number
  ) {
    const formattedPoints = `${points >= 0 ? '+' : ''}${points.toFixed(Math.abs(points) >= 100 ? 1 : 2)}`;

    // For very thin slices (sweep < 8.5 deg), omit text inside slice completely
    if (sweep < 8.5) {
      return {
        displaySymbol: '',
        displayPoints: '',
        symbolFontSize: 9,
        pointsFontSize: 9,
        showSymbol: false,
        showPoints: false
      };
    }

    // Normalized midDeg in [0, 360)
    const normMid = ((midDeg % 360) + 360) % 360;
    const rad = (normMid * Math.PI) / 180;
    const sinAngle = Math.abs(Math.sin(rad));
    const cosAngle = Math.abs(Math.cos(rad));

    // Circumferential arc length available at mid radius
    const arcLength = r_mid * ((sweep * Math.PI) / 180);

    // Calculate maximum safe horizontal box width that fits inside the ring:
    // Near 90 deg (right) and 270 deg (left), the ring width is horizontal, so horizontal limit = W_radial
    // Near 0 deg (top) and 180 deg (bottom), the arc is horizontal, so horizontal limit = arcLength
    const maxHorizontalSpace = Math.max(
      22,
      Math.min(
        (W_radial * 0.82) / (sinAngle * 0.70 + 0.45),
        (arcLength * 0.88) / (cosAngle * 0.70 + 0.45)
      )
    );

    // If slice is too tight for both symbol and points (sweep < 13.5 deg), only show points
    if (sweep < 13.5 && !isOther) {
      return {
        displaySymbol: '',
        displayPoints: formattedPoints,
        symbolFontSize: 8.5,
        pointsFontSize: 8.5,
        showSymbol: false,
        showPoints: true
      };
    }

    // Determine font size & truncation for Symbol
    let symbolFontSize = 10;
    let displaySymbol = label;
    let showSymbol = true;

    // Check if label fits at 10px (~6.2px per char)
    const len10 = label.length * 6.2;
    if (len10 > maxHorizontalSpace) {
      // Try 9px (~5.5px per char)
      const len9 = label.length * 5.5;
      if (len9 <= maxHorizontalSpace) {
        symbolFontSize = 9;
      } else {
        // Try 8.5px (~4.8px per char)
        const len85 = label.length * 4.8;
        if (len85 <= maxHorizontalSpace) {
          symbolFontSize = 8.5;
        } else {
          // Truncate symbol with trailing dot so it NEVER bleeds outside the circle
          symbolFontSize = 8.5;
          const maxChars = Math.max(3, Math.floor(maxHorizontalSpace / 5.0));
          if (label.length > maxChars) {
            displaySymbol = label.substring(0, Math.max(3, maxChars - 1)) + '.';
          }
        }
      }
    }

    // Determine font size for Points
    let pointsFontSize = 9.2;
    if (formattedPoints.length * 5.4 > maxHorizontalSpace) {
      pointsFontSize = 8.2;
    }

    return {
      displaySymbol,
      displayPoints: formattedPoints,
      symbolFontSize,
      pointsFontSize,
      showSymbol,
      showPoints: true
    };
  }

  // Waterfall Chart Items
  waterfallItems = computed(() => {
    const data = this.attributionData();
    if (!data || !data.contributors) return [];

    const topLifters = data.topLifters.slice(0, 8);
    const topDraggers = data.topDraggers.slice(0, 8);
    let runningPoints = 0;

    const items: Array<{
      symbol: string;
      points: number;
      startVal: number;
      endVal: number;
      isPositive: boolean;
      barPct: number;
    }> = [];

    [...topLifters, ...topDraggers].forEach(c => {
      const start = runningPoints;
      runningPoints += c.pointContribution;
      items.push({
        symbol: c.symbol,
        points: c.pointContribution,
        startVal: start,
        endVal: runningPoints,
        isPositive: c.pointContribution >= 0,
        barPct: c.pointBarPct
      });
    });

    return items;
  });

  ngOnInit(): void {
    this.applyThemeToDom();

    // Check query params immediately before triggering initial loads
    const initialView = this.route.snapshot.queryParams['tab'] || this.route.snapshot.queryParams['view'];
    if (initialView === 'watchlist') {
      this.setActiveTab('watchlist');
    } else if (initialView === 'sensex') {
      this.setActiveTab('sensex');
    } else if (initialView === 'banknifty') {
      this.setActiveTab('banknifty');
    }

    // Load initial data based on active tab
    this.loadAttributionData(this.activeTab());
    if (this.activeTab() === 'banknifty') {
      this.loadBankNiftyStocks(true);
      this.loadNifty50Stocks(false);
      this.loadSensexStocks(false);
    } else if (this.activeTab() === 'sensex') {
      this.loadSensexStocks(true);
      this.loadNifty50Stocks(false);
      this.loadBankNiftyStocks(false);
    } else {
      this.loadNifty50Stocks(this.activeTab() === 'nifty50');
      // Prefetch Sensex & Bank Nifty stocks dynamically in background so tab switching is instantaneous
      this.loadSensexStocks(false);
      this.loadBankNiftyStocks(false);
    }

    // Listen for subsequent query param changes
    this.queryParamsSub = this.route.queryParams.subscribe(params => {
      const view = params['tab'] || params['view'];
      if (view === 'watchlist' && this.activeTab() !== 'watchlist') {
        this.setActiveTab('watchlist');
      } else if (view === 'banknifty' && this.activeTab() !== 'banknifty') {
        this.setActiveTab('banknifty');
      } else if (view === 'sensex' && this.activeTab() !== 'sensex') {
        this.setActiveTab('sensex');
      } else if (view === 'nifty50' && this.activeTab() !== 'nifty50') {
        this.setActiveTab('nifty50');
      }
    });
  }

  ngOnDestroy(): void {
    this.queryParamsSub?.unsubscribe();
    this.customStocksSub?.unsubscribe();
    this.attributionSub?.unsubscribe();
  }

  loadNifty50Stocks(forceRefresh: boolean): void {
    if (this.allStocks().length === 0 && this.activeTab() === 'nifty50') {
      this.isLoading.set(true);
    }
    this.indexService.getNifty50List(forceRefresh).subscribe({
      next: (stocks) => {
        this.allStocks.set(stocks);
        if (this.activeTab() === 'nifty50') {
          this.isLoading.set(false);
        }
      },
      error: (err) => {
        console.warn('Failed to load NIFTY 50 constituent data:', err);
        if (this.allStocks().length === 0) {
          this.allStocks.set([]);
        }
        if (this.activeTab() === 'nifty50') {
          this.isLoading.set(false);
        }
      }
    });
  }

  loadSensexStocks(forceRefresh: boolean = false): void {
    if (this.sensexStocks().length === 0 && this.activeTab() === 'sensex') {
      this.isLoading.set(true);
    }
    this.indexService.getSensexList(forceRefresh).subscribe({
      next: (stocks) => {
        this.sensexStocks.set(stocks);
        if (this.activeTab() === 'sensex') {
          this.isLoading.set(false);
        }
      },
      error: (err) => {
        console.warn('Failed to load BSE SENSEX constituent data:', err);
        if (this.sensexStocks().length === 0) {
          this.sensexStocks.set([]);
        }
        if (this.activeTab() === 'sensex') {
          this.isLoading.set(false);
        }
      }
    });
  }

  loadBankNiftyStocks(forceRefresh: boolean = false): void {
    if (this.bankNiftyStocks().length === 0 && this.activeTab() === 'banknifty') {
      this.isLoading.set(true);
    }
    this.indexService.getBankNiftyList(forceRefresh).subscribe({
      next: (stocks) => {
        this.bankNiftyStocks.set(stocks);
        if (this.activeTab() === 'banknifty') {
          this.isLoading.set(false);
        }
      },
      error: (err) => {
        console.warn('Failed to load BANK NIFTY constituent data:', err);
        if (this.bankNiftyStocks().length === 0) {
          this.bankNiftyStocks.set([]);
        }
        if (this.activeTab() === 'banknifty') {
          this.isLoading.set(false);
        }
      }
    });
  }

  loadCustomWatchlistStocks(forceRefresh: boolean = false): void {
    const symbols = this.watchlistService.watchlistSymbols();
    if (symbols.length === 0) return;

    // 1. Populate customStocksMap from WatchlistService metadata first (immediate cache hit for instant display)
    const currentMap = new Map(this.customStocksMap());
    let mapChanged = false;
    for (const sym of symbols) {
      const upper = sym.toUpperCase();
      if (!currentMap.has(upper)) {
        const meta = this.watchlistService.getMeta(upper);
        if (meta && meta.companyName) {
          currentMap.set(upper, {
            symbol: upper,
            companyName: meta.companyName,
            logoUrl: meta.logoUrl,
            price: meta.price,
            marketCap: meta.marketCap,
            sector: meta.sector || 'Equities',
            industry: 'NSE Listed',
            exchange: 'NSE'
          } as Nifty50StockItem);
          mapChanged = true;
        }
      }
    }
    if (mapChanged) {
      this.customStocksMap.set(currentMap);
    }

    // 2. Fetch fresh prices and market caps for ALL watchlist symbols from backend
    const symbolsKey = symbols.map(s => s.toUpperCase()).sort().join(',');
    const hasMissingData = symbols.some(s => {
      const existing = this.customStocksMap().get(s.toUpperCase());
      return !existing || existing.marketCap == null;
    });
    if (!forceRefresh && !hasMissingData && (this.isFetchingCustomStocks || this.lastFetchedCustomSymbolsKey === symbolsKey)) {
      return;
    }

    this.isFetchingCustomStocks = true;
    this.customStocksSub?.unsubscribe();

    this.customStocksSub = this.indexService.getStocksBySymbols(symbols).subscribe({
      next: (stocks) => {
        this.isFetchingCustomStocks = false;
        this.lastFetchedCustomSymbolsKey = symbolsKey;
        if (!stocks || stocks.length === 0) return;
        this.customStocksMap.update(map => {
          const newMap = new Map(map);
          for (const s of stocks) {
            newMap.set(s.symbol.toUpperCase(), s);
            this.watchlistService.setMeta(s.symbol, {
              symbol: s.symbol,
              companyName: s.companyName,
              logoUrl: s.logoUrl,
              price: s.price,
              marketCap: s.marketCap,
              sector: s.sector,
              change: s.change,
              changePercent: s.changePercent,
              isPositive: s.isPositive,
              trendPath: s.trendPath
            });
          }
          return newMap;
        });
      },
      error: (err) => {
        this.isFetchingCustomStocks = false;
        console.warn('Could not fetch custom watchlist stocks:', err);
      }
    });
  }

  // View Tab Switching
  setActiveTab(tab: ViewTab): void {
    this.activeTooltip.set(null);
    this.hoveredSliceId.set(null);
    this.activeTab.set(tab);
    this.tableSearchQuery.set('');
    this.selectedSector.set('All');
    if (tab === 'watchlist') {
      this.sortField.set('symbol');
      this.sortOrder.set('asc');
      this.loadCustomWatchlistStocks();
    } else if (tab === 'banknifty') {
      this.sortField.set('weightage');
      this.sortOrder.set('desc');
      if (this.bankNiftyStocks().length === 0) {
        this.loadBankNiftyStocks(false);
      }
    } else if (tab === 'sensex') {
      this.sortField.set('weightage');
      this.sortOrder.set('desc');
      if (this.sensexStocks().length === 0) {
        this.loadSensexStocks(false);
      }
    } else {
      this.sortField.set('weightage');
      this.sortOrder.set('desc');
      if (this.allStocks().length === 0) {
        this.loadNifty50Stocks(false);
      }
    }

    if (tab !== 'watchlist') {
      this.loadAttributionData(tab);
    }
  }

  // Load Attribution Analytics (Tug of war, circle slices, contributors)
  loadAttributionData(tab: ViewTab = this.activeTab(), forceRefresh = false): void {
    if (tab === 'watchlist') return;

    let indexKey = 'nifty-50';
    if (tab === 'sensex') indexKey = 'sensex';
    if (tab === 'banknifty') indexKey = 'bank-nifty';

    this.attributionSub?.unsubscribe();
    this.isAttributionLoading.set(true);
    this.attributionSub = this.indexService.getIndexAttribution(indexKey, forceRefresh).subscribe({
      next: (data) => {
        this.attributionData.set(data);
        this.isAttributionLoading.set(false);
      },
      error: (err) => {
        console.warn('Failed to load attribution data:', err);
        this.isAttributionLoading.set(false);
      }
    });
  }

  setViewMode(mode: ViewMode): void {
    this.activeTooltip.set(null);
    this.hoveredSliceId.set(null);
    this.viewMode.set(mode);
  }

  setContributorFilter(filter: ContributorFilter): void {
    this.contributorFilter.set(filter);
  }

  setSliceHover(id: string | null): void {
    this.hoveredSliceId.set(id);
  }

  onSliceMouseEnter(event: MouseEvent, slice: IndexChartSlice): void {
    this.hoveredSliceId.set(slice.id);
    this.updateTooltipPosition(event, slice);
  }

  onSliceMouseMove(event: MouseEvent, slice: IndexChartSlice): void {
    this.updateTooltipPosition(event, slice);
  }

  onSliceMouseLeave(): void {
    this.hoveredSliceId.set(null);
    this.activeTooltip.set(null);
  }

  private updateTooltipPosition(event: MouseEvent, slice: IndexChartSlice): void {
    const target = event.currentTarget as SVGElement | null;
    const wrapper = target?.closest('.donut-chart-wrapper') as HTMLElement | null;

    let x = 0;
    let y = 0;
    if (wrapper) {
      const rect = wrapper.getBoundingClientRect();
      const tipWidth = 248;
      const tipHeight = 188;

      let posX = event.clientX - rect.left + 16;
      let posY = event.clientY - rect.top - 20;

      // Flip left if near right edge
      if (posX + tipWidth > rect.width) {
        posX = event.clientX - rect.left - tipWidth - 16;
      }
      if (posX < 8) {
        posX = 8;
      }

      // Flip up if near bottom edge
      if (posY + tipHeight > rect.height) {
        posY = rect.height - tipHeight - 10;
      }
      if (posY < 8) {
        posY = 8;
      }

      x = Math.round(posX);
      y = Math.round(posY);
    }

    if (slice.isOther) {
      const otherCount = slice.otherCount || (slice.isLifter ? 19 : 13);
      this.activeTooltip.set({
        x,
        y,
        isOther: true,
        symbol: slice.label,
        companyName: `${otherCount} Index Constituents`,
        sector: slice.isLifter ? 'Combined Positive Movers' : 'Combined Negative Movers',
        points: slice.points,
        formattedPoints: `${slice.points >= 0 ? '+' : ''}${slice.points.toFixed(2)} pts`,
        isLifter: slice.isLifter,
        otherCount,
        percentageOfCategory: slice.percentageOfCategory
      });
    } else {
      const contributor = this.attributionData()?.contributors.find(
        c => c.symbol.toUpperCase() === slice.id.toUpperCase()
      );
      this.activeTooltip.set({
        x,
        y,
        isOther: false,
        symbol: slice.label,
        companyName: contributor?.companyName || slice.label,
        sector: contributor?.sector || 'Index Constituent',
        points: slice.points,
        formattedPoints: contributor?.formattedPoints || `${slice.points >= 0 ? '+' : ''}${slice.points.toFixed(2)} pts`,
        isLifter: slice.isLifter,
        changePercent: contributor?.changePercent,
        formattedChangePercent: contributor?.formattedChangePercent || (contributor?.changePercent !== undefined ? `${contributor.changePercent >= 0 ? '+' : ''}${contributor.changePercent.toFixed(2)}%` : undefined),
        weightage: contributor?.weightage,
        price: contributor?.price,
        percentageOfCategory: slice.percentageOfCategory
      });
    }
  }

  // Header Navigation & Actions
  navigateToHome(): void {
    this.router.navigate(['/']);
  }

  navigateToStock(symbol: string): void {
    if (!symbol) return;
    this.router.navigate(['/stock', symbol.toUpperCase()]);
  }

  navigateToAnnualReports(): void {
    this.router.navigate(['/annual-reports']);
  }


  toggleTheme(): void {
    this.isLightTheme.update(v => !v);
    this.applyThemeToDom();
  }

  private applyThemeToDom(): void {
    if (typeof document !== 'undefined') {
      const theme = this.isLightTheme() ? 'light' : 'dark';
      document.documentElement.setAttribute('data-theme', theme);
      if (this.isLightTheme()) {
        document.body.classList.add('light-theme');
        document.body.classList.remove('dark-theme');
      } else {
        document.body.classList.remove('light-theme');
        document.body.classList.add('dark-theme');
      }
    }
  }

  // Watchlist Star Toggle (stops propagation so row click isn't triggered)
  toggleStar(event: Event, symbol: string): void {
    event.stopPropagation();
    const upper = symbol.toUpperCase();
    const stock = this.currentBaseStocks().find(s => s.symbol.toUpperCase() === upper) ||
                  this.allStocks().find(s => s.symbol.toUpperCase() === upper) ||
                  this.sensexStocks().find(s => s.symbol.toUpperCase() === upper) ||
                  this.bankNiftyStocks().find(s => s.symbol.toUpperCase() === upper) ||
                  this.customStocksMap().get(upper);
    this.watchlistService.toggleWatchlist(symbol, stock ? {
      symbol: stock.symbol,
      companyName: stock.companyName,
      logoUrl: stock.logoUrl,
      price: stock.price,
      marketCap: stock.marketCap,
      sector: stock.sector,
      change: stock.change,
      changePercent: stock.changePercent,
      isPositive: stock.isPositive,
      trendPath: stock.trendPath
    } : undefined);
  }

  isStarred(symbol: string): boolean {
    return this.watchlistService.isInWatchlist(symbol);
  }

  // Table Sorting
  setSorting(field: SortField): void {
    if (this.sortField() === field) {
      this.sortOrder.set(this.sortOrder() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortField.set(field);
      this.sortOrder.set(field === 'symbol' || field === 'name' ? 'asc' : 'desc');
    }
  }

  setSector(sector: string): void {
    this.selectedSector.set(sector);
  }

  // Watchlist Sorting & UI Helpers
  toggleWatchlistSort(field: 'sno' | 'name' | 'sector' | 'price' | 'changePercent' | 'marketCap'): void {
    if (this.watchlistSortField() === field) {
      this.watchlistSortOrder.set(this.watchlistSortOrder() === 'asc' ? 'desc' : 'asc');
    } else {
      this.watchlistSortField.set(field);
      this.watchlistSortOrder.set(field === 'name' || field === 'sector' ? 'asc' : 'desc');
    }
  }

  getAvatarInfo(name?: string, symbol?: string): { letter: string; bg: string; color: string } {
    const raw = (name || symbol || 'S').trim();
    const letter = (raw.charAt(0) || 'S').toUpperCase();
    const palettes = [
      { bg: '#ede9fe', color: '#6d28d9' }, // Purple (Vishal Mega Mart)
      { bg: '#e2e8f0', color: '#334155' }, // Slate (Ola Electric)
      { bg: '#dcfce7', color: '#15803d' }, // Green (Nuvoco Vistas)
      { bg: '#dbeafe', color: '#1d4ed8' }, // Blue (Tata Capital)
      { bg: '#f3e8ff', color: '#7e22ce' }, // Purple (Juniper Hotels)
      { bg: '#cffafe', color: '#0e7490' }, // Cyan (RailTel)
      { bg: '#ffe4e6', color: '#be123c' }, // Pink (Senco Gold)
      { bg: '#e0e7ff', color: '#4338ca' }, // Indigo (Uflex)
      { bg: '#ffedd5', color: '#c2410c' }  // Amber/Orange (Dhampur)
    ];
    const code = raw.split('').reduce((acc, c) => acc + c.charCodeAt(0), 0);
    const palette = palettes[code % palettes.length];
    return { letter, bg: palette.bg, color: palette.color };
  }

  getWatchlistTrendPath(stock: Nifty50StockItem): string {
    if (stock.trendPath) return stock.trendPath;
    const isPos = stock.changePercent != null ? stock.changePercent >= 0 : (stock.isPositive ?? true);
    const seed = Math.abs((stock.symbol || 'S').split('').reduce((acc, c) => acc + c.charCodeAt(0), 0)) % 10;
    if (isPos) {
      return `M 4 ${18 + (seed % 3)} C 22 ${16 - (seed % 3)}, 42 ${12 + (seed % 2)}, 62 10 C 72 8, 80 5, 86 4`;
    } else {
      return `M 4 ${6 + (seed % 3)} C 22 ${10 + (seed % 3)}, 42 ${15 - (seed % 2)}, 62 18 C 72 20, 80 22, 86 24`;
    }
  }

  openManageWatchlist(): void {
    this.manageWatchlistSearchQuery.set('');
    this.isManageWatchlistModalOpen.set(true);
  }

  closeManageWatchlist(): void {
    this.isManageWatchlistModalOpen.set(false);
  }
}
