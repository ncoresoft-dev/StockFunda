import { Component, OnInit, OnDestroy, inject, signal, computed, effect, untracked, ElementRef, HostListener, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { Subject, Subscription, of } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, catchError, map } from 'rxjs/operators';
import { StockIndexService } from '../../services/stock-index.service';
import { WatchlistService } from '../../services/watchlist.service';
import { StockNewsService } from '../../services/stock-news.service';
import { Nifty50StockItem } from '../../models/stock-index.model';
import { Company } from '../../models/stock-news.model';

import { AdminNotificationBellComponent } from '../admin-notification-bell/admin-notification-bell.component';
import { AdminAnnualReportModalComponent } from '../admin-annual-report-modal/admin-annual-report-modal.component';

export type SortField = 'weightage' | 'symbol' | 'name' | 'sector' | 'industry';
export type SortOrder = 'asc' | 'desc';
export type ViewTab = 'nifty50' | 'watchlist';

@Component({
  selector: 'app-nifty50-landing',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminNotificationBellComponent, AdminAnnualReportModalComponent],
  templateUrl: './nifty50-landing.component.html',
  styleUrl: './nifty50-landing.component.css'
})
export class Nifty50LandingComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly indexService = inject(StockIndexService);
  readonly watchlistService = inject(WatchlistService);
  private readonly newsService = inject(StockNewsService);

  // Active View Tab: 'nifty50' or 'watchlist'
  activeTab = signal<ViewTab>('nifty50');

  // Theme
  isLightTheme = signal<boolean>(true);

  // Header Search Typeahead
  headerSearchQuery = signal<string>('');
  headerSearchResults = signal<Company[]>([]);
  isHeaderSearching = signal<boolean>(false);
  isHeaderSearchDropdownOpen = signal<boolean>(false);
  @ViewChild('headerSearchWrapper') headerSearchWrapper?: ElementRef<HTMLElement>;
  private headerSearchSubject = new Subject<string>();
  private headerSearchSub?: Subscription;
  private queryParamsSub?: Subscription;

  // Table Data & Filtering
  allStocks = signal<Nifty50StockItem[]>([]);
  isLoading = signal<boolean>(true);
  tableSearchQuery = signal<string>('');
  selectedSector = signal<string>('All');
  sortField = signal<SortField>('weightage');
  sortOrder = signal<SortOrder>('desc');

  // Sector Categories
  sectors = computed(() => {
    const list = this.allStocks();
    const set = new Set<string>();
    list.forEach(s => {
      if (s.sector) set.add(s.sector);
    });
    return ['All', ...Array.from(set).sort()];
  });

  // Sector Breakdown with Counts (sorted by count desc)
  sectorStats = computed(() => {
    const list = this.allStocks();
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
    const list = this.allStocks();
    const tab = this.activeTab();

    if (tab === 'watchlist') {
      const symbols = this.watchlistService.watchlistSymbols();
      const foundMap = new Map(list.map(s => [s.symbol.toUpperCase(), s]));

      return symbols.map(sym => {
        const upper = sym.toUpperCase();
        const base = foundMap.get(upper);
        const custom = this.customStocksMap().get(upper);
        const meta = this.watchlistService.getMeta(upper);

        return {
          symbol: upper,
          companyName: custom?.companyName || base?.companyName || meta?.companyName || `${upper} Ltd`,
          sector: custom?.sector || base?.sector || meta?.sector || 'Equities',
          industry: base?.industry || 'NSE Listed',
          logoUrl: custom?.logoUrl || base?.logoUrl || meta?.logoUrl,
          websiteUrl: custom?.websiteUrl || base?.websiteUrl,
          price: custom?.price ?? base?.price ?? meta?.price,
          marketCap: custom?.marketCap ?? base?.marketCap ?? meta?.marketCap,
          weightagePercentage: base?.weightagePercentage,
          isin: base?.isin,
          exchange: 'NSE'
        } as Nifty50StockItem;
      });
    }

    return list;
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

    // Filter by sector (only when in NIFTY 50 tab or if sector filter is active)
    if (sector !== 'All' && this.activeTab() === 'nifty50') {
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
    const list = this.allStocks();
    const total = list.length;
    const sectorsCount = this.sectors().length > 1 ? this.sectors().length - 1 : 0;

    let topWeighted: Nifty50StockItem | null = null;
    if (list.length > 0) {
      const sortedByWeight = [...list].sort((a, b) => (b.weightagePercentage || 0) - (a.weightagePercentage || 0));
      topWeighted = sortedByWeight[0];
    }

    return {
      totalConstituents: total,
      sectorsCount,
      topWeighted,
      exchange: 'NSE',
      category: 'Broad Market Benchmark'
    };
  });

  ngOnInit(): void {
    this.applyThemeToDom();
    this.setupHeaderSearch();

    // Check query params immediately before triggering initial loads
    const initialView = this.route.snapshot.queryParams['tab'] || this.route.snapshot.queryParams['view'];
    if (initialView === 'watchlist') {
      this.setActiveTab('watchlist');
    }

    // Only forceRefresh if user landed directly on the NIFTY 50 tab
    this.loadNifty50Stocks(this.activeTab() === 'nifty50');

    // Listen for subsequent query param changes
    this.queryParamsSub = this.route.queryParams.subscribe(params => {
      const view = params['tab'] || params['view'];
      if (view === 'watchlist' && this.activeTab() !== 'watchlist') {
        this.setActiveTab('watchlist');
      } else if (view === 'nifty50' && this.activeTab() !== 'nifty50') {
        this.setActiveTab('nifty50');
      }
    });
  }

  ngOnDestroy(): void {
    this.headerSearchSub?.unsubscribe();
    this.queryParamsSub?.unsubscribe();
    this.customStocksSub?.unsubscribe();
  }

  loadNifty50Stocks(forceRefresh: boolean): void {
    if (this.allStocks().length === 0) {
      this.isLoading.set(true);
    }
    this.indexService.getNifty50List(forceRefresh).subscribe({
      next: (stocks) => {
        this.allStocks.set(stocks);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.warn('Failed to load NIFTY 50 constituent data:', err);
        if (this.allStocks().length === 0) {
          this.allStocks.set([]);
        }
        this.isLoading.set(false);
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
    if (tab === 'watchlist') {
      this.sortField.set('symbol');
      this.sortOrder.set('asc');
      this.loadCustomWatchlistStocks();
    } else {
      this.sortField.set('weightage');
      this.sortOrder.set('desc');
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
    const stock = this.allStocks().find(s => s.symbol.toUpperCase() === upper) || this.customStocksMap().get(upper);
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

  // Typeahead Header Search
  onHeaderSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.headerSearchQuery.set(val);
    if (val.trim().length >= 2) {
      this.isHeaderSearchDropdownOpen.set(true);
    } else {
      this.isHeaderSearchDropdownOpen.set(false);
      this.headerSearchResults.set([]);
    }
    this.headerSearchSubject.next(val);
  }

  onHeaderSearchFocus(): void {
    if (this.headerSearchResults().length > 0 && this.headerSearchQuery().trim().length >= 2) {
      this.isHeaderSearchDropdownOpen.set(true);
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    if (this.isHeaderSearchDropdownOpen() && this.headerSearchWrapper?.nativeElement && !this.headerSearchWrapper.nativeElement.contains(target)) {
      this.isHeaderSearchDropdownOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscapePress(): void {
    this.isHeaderSearchDropdownOpen.set(false);
  }

  onHeaderSearchSubmit(): void {
    this.isHeaderSearchDropdownOpen.set(false);
    const q = this.headerSearchQuery().trim();
    if (q) {
      this.navigateToStock(q);
      this.headerSearchQuery.set('');
      this.headerSearchResults.set([]);
    }
  }

  selectHeaderSearchResult(company: Company): void {
    this.isHeaderSearchDropdownOpen.set(false);
    if (company && company.symbol) {
      this.navigateToStock(company.symbol);
      this.headerSearchQuery.set('');
      this.headerSearchResults.set([]);
    }
  }

  private setupHeaderSearch(): void {
    this.headerSearchSub = this.headerSearchSubject.pipe(
      debounceTime(200),
      distinctUntilChanged(),
      switchMap(query => {
        const clean = query.trim();
        if (!clean || clean.length < 2) {
          this.headerSearchResults.set([]);
          this.isHeaderSearching.set(false);
          this.isHeaderSearchDropdownOpen.set(false);
          return of([]);
        }
        this.isHeaderSearching.set(true);

        const localMatches: Company[] = this.allStocks()
          .filter(s =>
            s.symbol.toLowerCase().includes(clean.toLowerCase()) ||
            s.companyName.toLowerCase().includes(clean.toLowerCase())
          )
          .map(s => ({
            id: 0,
            symbol: s.symbol,
            companyName: s.companyName,
            industry: s.industry || '',
            exchange: s.exchange || 'NSE'
          } as Company));

        return this.newsService.searchCompanies(clean).pipe(
          map(apiRes => (apiRes && apiRes.length > 0 ? apiRes : localMatches)),
          catchError(() => of(localMatches))
        );
      })
    ).subscribe(results => {
      this.headerSearchResults.set(results);
      this.isHeaderSearching.set(false);
      if (results && results.length > 0) {
        this.isHeaderSearchDropdownOpen.set(true);
      }
    });
  }
}
