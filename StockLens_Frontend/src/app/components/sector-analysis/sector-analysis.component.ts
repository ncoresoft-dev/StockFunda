import { Component, OnInit, OnDestroy, HostListener, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute, RouterModule } from '@angular/router';
import { Subject, Subscription } from 'rxjs';
import { debounceTime } from 'rxjs/operators';
import { SectorAnalysisService } from '../../services/sector-analysis.service';
import { SectorAnalysisResponse, SectorSummary, SectorStock } from '../../models/sector-analysis.model';
import { computeSquarifiedTreemap, TreemapBounds, TreemapLayoutItem } from '../../utils/squarified-treemap.util';
import { HeaderComponent } from '../header/header.component';

export interface TreemapTile {
  stock: SectorStock;
  symbol: string;
  name: string;
  price: number;
  change: number;
  changePercent: number;
  marketCapCr: number;
  formattedMarketCap: string;
  formattedChange: string;
  isPositive: boolean;
  isNeutral: boolean;
  leftPct: number;
  topPct: number;
  widthPct: number;
  heightPct: number;
  bgColor: string;
  textColor: string;
  borderColor: string;
  fontSizeTier: 'sm' | 'md' | 'lg' | 'xl';
  showDetails: boolean;
}

@Component({
  selector: 'app-sector-analysis',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, HeaderComponent],
  templateUrl: './sector-analysis.component.html',
  styleUrls: ['./sector-analysis.component.css']
})
export class SectorAnalysisComponent implements OnInit, OnDestroy {
  private readonly sectorService = inject(SectorAnalysisService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  // Core State
  isLoading = signal<boolean>(true);
  errorMessage = signal<string | null>(null);
  selectedSector = signal<string>('');
  availableSectors = signal<string[]>([]);
  summary = signal<SectorSummary | null>(null);
  stocks = signal<SectorStock[]>([]);
  topGainers = signal<SectorStock[]>([]);
  topLosers = signal<SectorStock[]>([]);
  bestOverallStock = signal<SectorStock | null>(null);

  // Filters & Controls
  filterMode = signal<'all' | 'gainers' | 'losers'>('all');
  searchQuery = signal<string>('');
  selectedForCompare = signal<Set<string>>(new Set());
  hoveredStock = signal<SectorStock | null>(null);
  
  // Sector Search
  sectorSearchQuery = signal<string>('');
  isSectorDropdownOpen = signal<boolean>(false);

  // Sort state for table
  sortField = signal<keyof SectorStock>('marketCapCr');
  sortAsc = signal<boolean>(false);

  // Computed Treemap Layout
  treemapTiles = signal<TreemapTile[]>([]);

  // Subscriptions
  private routeSub?: Subscription;

  // Dynamic top sectors derived purely from backend available sectors (no static list)
  topSectors = computed(() => {
    return this.availableSectors().slice(0, 10);
  });

  // Dynamic filter for sectors in picker
  filteredAvailableSectors = computed(() => {
    const q = this.sectorSearchQuery().trim().toLowerCase();
    const all = this.availableSectors();
    if (!q) return all;
    return all.filter(s => s.toLowerCase().includes(q));
  });

  ngOnInit(): void {
    this.routeSub = this.route.queryParams.subscribe(params => {
      const sectorParam = params['sector'];
      if (sectorParam && sectorParam.trim().length > 0) {
        this.selectedSector.set(sectorParam.trim());
        this.loadSectorData(this.selectedSector());
      } else {
        // Load dynamically from backend without pre-baked static sector
        this.loadSectorData();
      }
    });
  }

  ngOnDestroy(): void {
    this.routeSub?.unsubscribe();
  }

  /**
   * Load data for given sector (or dynamically load default if omitted)
   */
  loadSectorData(sectorName?: string): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.sectorService.getSectorAnalysis(sectorName).subscribe({
      next: (res: SectorAnalysisResponse) => {
        this.summary.set(res.summary);
        this.stocks.set(res.stocks || []);
        this.topGainers.set(res.topGainers || []);
        this.topLosers.set(res.topLosers || []);
        this.bestOverallStock.set(res.bestOverallStock || null);
        
        if (res.availableSectors && res.availableSectors.length > 0) {
          this.availableSectors.set(res.availableSectors);
        }

        if (res.summary?.sectorName) {
          this.selectedSector.set(res.summary.sectorName);
        }

        this.generateTreemapLayout();
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Failed to load sector analysis:', err);
        this.errorMessage.set('Could not load sector data. Please try again.');
        this.isLoading.set(false);
      }
    });
  }

  /**
   * Switch active sector
   */
  onSelectSector(sectorName: string): void {
    if (this.selectedSector() === sectorName) return;
    this.selectedSector.set(sectorName);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { sector: sectorName },
      queryParamsHandling: 'merge'
    });
    this.loadSectorData(sectorName);
  }

  /**
   * Switch filter mode (All, Gainers, Losers)
   */
  onFilterChange(mode: 'all' | 'gainers' | 'losers'): void {
    this.filterMode.set(mode);
    this.generateTreemapLayout();
  }

  /**
   * Calculate squarified treemap bounding boxes based on current filter & weights
   */
  generateTreemapLayout(): void {
    const rawStocks = this.stocks();
    if (!rawStocks || rawStocks.length === 0) {
      this.treemapTiles.set([]);
      return;
    }

    let filtered = [...rawStocks];
    const mode = this.filterMode();
    if (mode === 'gainers') {
      filtered = filtered.filter(s => s.changePercent > 0);
    } else if (mode === 'losers') {
      filtered = filtered.filter(s => s.changePercent < 0);
    }

    if (filtered.length === 0) {
      this.treemapTiles.set([]);
      return;
    }

    // Limit to top 80 stocks for clean performance and readability
    filtered = filtered.slice(0, 80);

    // Compute relative weight for squarification (MarketCap based, minimum 100 Cr floor)
    const items = filtered.map(s => ({
      ...s,
      value: Math.max(Number(s.marketCapCr) || 0, 100)
    }));

    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 520 };
    const layout = computeSquarifiedTreemap(items, bounds);

    const tiles: TreemapTile[] = layout.map(node => {
      const stock = node.item;
      const chg = stock.changePercent || 0;
      const isPos = chg > 0;
      const isNeut = Math.abs(chg) < 0.01;
      
      const colors = this.getTileColor(chg);
      const isSmall = node.widthPct < 7 || node.heightPct < 7;
      const isMedium = node.widthPct >= 7 && node.widthPct < 15;
      const isLarge = node.widthPct >= 15 && node.widthPct < 25;
      
      let fontSizeTier: 'sm' | 'md' | 'lg' | 'xl' = 'sm';
      if (node.widthPct >= 25) fontSizeTier = 'xl';
      else if (isLarge) fontSizeTier = 'lg';
      else if (isMedium) fontSizeTier = 'md';

      return {
        stock,
        symbol: stock.symbol,
        name: stock.companyName || stock.symbol,
        price: stock.price || 0,
        change: stock.change || 0,
        changePercent: chg,
        marketCapCr: stock.marketCapCr || 0,
        formattedMarketCap: this.formatCr(stock.marketCapCr),
        formattedChange: `${isPos ? '+' : ''}${chg.toFixed(2)}%`,
        isPositive: isPos,
        isNeutral: isNeut,
        leftPct: node.leftPct,
        topPct: node.topPct,
        widthPct: node.widthPct,
        heightPct: node.heightPct,
        bgColor: colors.bg,
        textColor: colors.text,
        borderColor: colors.border,
        fontSizeTier,
        showDetails: !isSmall
      };
    });

    this.treemapTiles.set(tiles);
  }

  /**
   * Determine Trendlyne-style green/red gradient shades
   */
  private getTileColor(change: number): { bg: string; text: string; border: string } {
    if (change >= 10) {
      return { bg: 'rgba(21, 128, 61, 0.95)', text: '#ffffff', border: '#15803d' }; // Dark Emerald
    } else if (change >= 5) {
      return { bg: 'rgba(22, 163, 74, 0.88)', text: '#ffffff', border: '#16a34a' }; // Rich Green
    } else if (change >= 2) {
      return { bg: 'rgba(34, 197, 94, 0.78)', text: '#ffffff', border: '#22c55e' }; // Moderate Green
    } else if (change > 0) {
      return { bg: 'rgba(74, 222, 128, 0.65)', text: '#064e3b', border: '#4ade80' }; // Soft Sage
    } else if (Math.abs(change) < 0.01) {
      return { bg: 'rgba(71, 85, 105, 0.70)', text: '#f8fafc', border: '#64748b' }; // Slate Neutral
    } else if (change <= -10) {
      return { bg: 'rgba(153, 27, 27, 0.95)', text: '#ffffff', border: '#991b1b' }; // Deep Crimson
    } else if (change <= -5) {
      return { bg: 'rgba(185, 28, 28, 0.88)', text: '#ffffff', border: '#b91c1c' }; // Strong Red
    } else if (change <= -2) {
      return { bg: 'rgba(220, 38, 38, 0.78)', text: '#ffffff', border: '#dc2626' }; // Moderate Red
    } else {
      return { bg: 'rgba(248, 113, 113, 0.65)', text: '#7f1d1d', border: '#f87171' }; // Soft Rose
    }
  }

  /**
   * Table Sorting
   */
  sortBy(field: keyof SectorStock): void {
    if (this.sortField() === field) {
      this.sortAsc.update(prev => !prev);
    } else {
      this.sortField.set(field);
      this.sortAsc.set(false); // default desc for financial metrics
    }
  }

  /**
   * Filtered & Sorted stock list for the table
   */
  sortedStocks = computed(() => {
    let list = [...this.stocks()];
    const query = this.searchQuery().trim().toLowerCase();
    if (query) {
      list = list.filter(s =>
        s.symbol.toLowerCase().includes(query) ||
        (s.companyName && s.companyName.toLowerCase().includes(query))
      );
    }

    const field = this.sortField();
    const asc = this.sortAsc();

    return list.sort((a, b) => {
      const valA = a[field] ?? 0;
      const valB = b[field] ?? 0;
      if (valA < valB) return asc ? -1 : 1;
      if (valA > valB) return asc ? 1 : -1;
      return 0;
    });
  });

  /**
   * Toggle stock selection for comparison
   */
  toggleCompare(symbol: string): void {
    this.selectedForCompare.update(set => {
      const next = new Set(set);
      if (next.has(symbol)) {
        next.delete(symbol);
      } else {
        if (next.size >= 4) {
          alert('You can compare a maximum of 4 companies at a time.');
          return next;
        }
        next.add(symbol);
      }
      return next;
    });
  }

  isStockSelected(symbol: string): boolean {
    return this.selectedForCompare().has(symbol);
  }

  /**
   * Navigate to Compare matrix with selected stocks
   */
  launchComparison(): void {
    const selected = Array.from(this.selectedForCompare());
    if (selected.length === 0) return;

    if (selected.length === 1 && this.bestOverallStock()) {
      // Auto-pair with sector leader
      const leader = this.bestOverallStock()?.symbol;
      if (leader && leader !== selected[0]) {
        selected.unshift(leader);
      }
    }

    const queryParams: Record<string, string> = {};
    selected.forEach((sym, idx) => {
      queryParams[`stock${idx + 1}`] = sym;
    });

    this.router.navigate(['/compare'], { queryParams });
  }

  /**
   * Quick compare a stock directly with the Sector Leader
   */
  compareWithLeader(symbol: string): void {
    const leader = this.bestOverallStock()?.symbol || this.stocks()[0]?.symbol;
    if (!leader || leader === symbol) {
      this.router.navigate(['/compare'], { queryParams: { stock1: symbol } });
      return;
    }
    this.router.navigate(['/compare'], {
      queryParams: { stock1: leader, stock2: symbol }
    });
  }

  /**
   * Navigate to single stock details
   */
  navigateToStock(symbol: string): void {
    if (!symbol) return;
    this.router.navigate(['/stock', symbol.toUpperCase()]);
  }

  // Formatters
  formatCr(value?: number | null): string {
    if (value == null || isNaN(value)) return '—';
    if (value >= 100000) {
      return `₹${(value / 100000).toFixed(2)} Lakh Cr`;
    }
    return `₹${value.toLocaleString('en-IN', { maximumFractionDigits: 1 })} Cr`;
  }

  formatRatio(value?: number | null): string {
    if (value == null || isNaN(value) || value <= 0) return '—';
    return value.toFixed(2);
  }

  formatPercent(value?: number | null): string {
    if (value == null || isNaN(value)) return '—';
    return `${value.toFixed(2)}%`;
  }

  // Ratio / Breadth Bar width computation
  getAdvanceBreadthPercent(): number {
    const s = this.summary();
    if (!s || s.totalCompanies <= 0) return 50;
    const total = (s.advanceCount || 0) + (s.declineCount || 0);
    if (total <= 0) return 50;
    return Math.round((s.advanceCount / total) * 100);
  }

  getDeclineBreadthPercent(): number {
    return 100 - this.getAdvanceBreadthPercent();
  }
}

