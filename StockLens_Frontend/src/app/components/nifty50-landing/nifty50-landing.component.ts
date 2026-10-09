import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
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

export type SortField = 'weightage' | 'symbol' | 'name' | 'sector' | 'industry';
export type SortOrder = 'asc' | 'desc';
export type ViewTab = 'nifty50' | 'watchlist';

@Component({
  selector: 'app-nifty50-landing',
  standalone: true,
  imports: [CommonModule, FormsModule],
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

  // Theme & Exchange
  isLightTheme = signal<boolean>(true);
  selectedExchange = signal<'NSE' | 'BSE'>('NSE');
  isSyncing = signal<boolean>(false);

  // Header Search Typeahead
  headerSearchQuery = signal<string>('');
  headerSearchResults = signal<Company[]>([]);
  isHeaderSearching = signal<boolean>(false);
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

  // Base list depending on active tab
  currentBaseStocks = computed(() => {
    const list = this.allStocks();
    const tab = this.activeTab();

    if (tab === 'watchlist') {
      const symbols = this.watchlistService.watchlistSymbols();
      const symbolSet = new Set(symbols.map(s => s.toUpperCase()));
      const found = list.filter(s => symbolSet.has(s.symbol.toUpperCase()));
      const foundSet = new Set(found.map(f => f.symbol.toUpperCase()));

      // Include any user-added custom stocks that are not in default NIFTY 50
      const custom = symbols
        .filter(s => !foundSet.has(s.toUpperCase()))
        .map(sym => ({
          symbol: sym.toUpperCase(),
          companyName: `${sym.toUpperCase()} Ltd`,
          sector: 'Equities',
          industry: 'NSE Listed',
          exchange: 'NSE'
        } as Nifty50StockItem));

      return [...found, ...custom];
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
    this.loadNifty50Stocks(false);

    // Read query params (e.g. ?tab=watchlist or ?view=watchlist)
    this.queryParamsSub = this.route.queryParams.subscribe(params => {
      if (params['tab'] === 'watchlist' || params['view'] === 'watchlist') {
        this.activeTab.set('watchlist');
      } else if (params['tab'] === 'nifty50' || params['view'] === 'nifty50') {
        this.activeTab.set('nifty50');
      }
    });
  }

  ngOnDestroy(): void {
    this.headerSearchSub?.unsubscribe();
    this.queryParamsSub?.unsubscribe();
  }

  loadNifty50Stocks(forceRefresh: boolean): void {
    this.isLoading.set(true);
    this.indexService.getNifty50List(forceRefresh).subscribe({
      next: (stocks) => {
        this.allStocks.set(stocks);
        this.isLoading.set(false);
        this.isSyncing.set(false);
      },
      error: (err) => {
        console.warn('Failed to load NIFTY 50 constituent data:', err);
        this.allStocks.set([]);
        this.isLoading.set(false);
        this.isSyncing.set(false);
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
    } else {
      this.sortField.set('weightage');
      this.sortOrder.set('desc');
    }
  }

  // Header Navigation & Actions
  navigateToHome(): void {
    this.setActiveTab('nifty50');
  }

  navigateToStock(symbol: string): void {
    if (!symbol) return;
    this.router.navigate(['/stock', symbol.toUpperCase()]);
  }

  toggleExchange(exchange: 'NSE' | 'BSE'): void {
    this.selectedExchange.set(exchange);
  }

  syncData(): void {
    this.isSyncing.set(true);
    this.loadNifty50Stocks(true);
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
    this.watchlistService.toggleWatchlist(symbol);
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
    this.headerSearchSubject.next(val);
  }

  onHeaderSearchSubmit(): void {
    const q = this.headerSearchQuery().trim();
    if (q) {
      this.navigateToStock(q);
      this.headerSearchQuery.set('');
      this.headerSearchResults.set([]);
    }
  }

  selectHeaderSearchResult(company: Company): void {
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
    });
  }
}
