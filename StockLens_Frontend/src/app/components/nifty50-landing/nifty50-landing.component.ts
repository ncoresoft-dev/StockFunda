import { Component, OnInit, OnDestroy, inject, signal, computed, effect, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { StockIndexService } from '../../services/stock-index.service';
import { WatchlistService } from '../../services/watchlist.service';
import { Nifty50StockItem } from '../../models/stock-index.model';

import { AdminAnnualReportModalComponent } from '../admin-annual-report-modal/admin-annual-report-modal.component';
import { HeaderComponent } from '../header/header.component';

export type SortField = 'weightage' | 'symbol' | 'name' | 'sector' | 'industry';
export type SortOrder = 'asc' | 'desc';
export type ViewTab = 'nifty50' | 'sensex' | 'banknifty' | 'watchlist';

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

  // Theme
  isLightTheme = signal<boolean>(true);

  private queryParamsSub?: Subscription;

  // Table Data & Filtering
  allStocks = signal<Nifty50StockItem[]>([]);
  sensexStocks = signal<Nifty50StockItem[]>([]);
  bankNiftyStocks = signal<Nifty50StockItem[]>([]);
  isLoading = signal<boolean>(true);
  tableSearchQuery = signal<string>('');
  selectedSector = signal<string>('All');
  sortField = signal<SortField>('weightage');
  sortOrder = signal<SortOrder>('desc');

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

        return {
          symbol: upper,
          companyName: custom?.companyName || base?.companyName || meta?.companyName || `${upper} Ltd`,
          sector: custom?.sector || base?.sector || meta?.sector || 'Equities',
          industry: base?.industry || 'Listed',
          logoUrl: custom?.logoUrl || base?.logoUrl || meta?.logoUrl,
          websiteUrl: custom?.websiteUrl || base?.websiteUrl,
          price: custom?.price ?? base?.price ?? meta?.price,
          marketCap: custom?.marketCap ?? base?.marketCap ?? meta?.marketCap,
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
    if (!forceRefresh && (this.isFetchingCustomStocks || this.lastFetchedCustomSymbolsKey === symbolsKey)) {
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
              sector: s.sector
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
      sector: stock.sector
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


}
