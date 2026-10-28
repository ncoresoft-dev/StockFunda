import { Component, OnInit, OnDestroy, inject, signal, computed, effect, untracked, ElementRef, HostListener, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { of, Subject, Subscription } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, catchError, map } from 'rxjs/operators';
import { MarketTickerService } from '../../services/market-ticker.service';
import { StockNewsService } from '../../services/stock-news.service';
import { StockIndexService } from '../../services/stock-index.service';
import { WatchlistService } from '../../services/watchlist.service';
import { RecentResearchService, RecentStockItem } from '../../services/recent-research.service';
import { MarketTickerQuote, ExploreLensCompany, LensMetric } from '../../models/market-ticker.model';
import { Company } from '../../models/stock-news.model';
import { Nifty50StockItem } from '../../models/stock-index.model';
import { AdminNotificationBellComponent } from '../admin-notification-bell/admin-notification-bell.component';

export type LensType = 'Growth' | 'Cash Flow' | 'Ownership' | 'Risk';

export interface NiftyTop5Item {
  name: string;
  symbol: string;
  marketCapFormatted: string;
  marketCapRaw: number;
  barPercent: number;
  color: string;
}

export interface NiftyMapItem {
  symbol: string;
  name: string;
  sector: string;
  marketCapRaw: number;
  formattedLakhCr: string;
  bg: string;
  textColor: string;
  flexWeight: number;
}

@Component({
  selector: 'app-home-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminNotificationBellComponent],
  templateUrl: './home-dashboard.component.html',
  styleUrls: ['./home-dashboard.component.css']
})
export class HomeDashboardComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly tickerService = inject(MarketTickerService);
  private readonly newsService = inject(StockNewsService);
  private readonly indexService = inject(StockIndexService);
  readonly watchlistService = inject(WatchlistService);
  private readonly recentService = inject(RecentResearchService);

  // Live Ticker Quotes
  tickerQuotes = signal<MarketTickerQuote[]>([]);
  isLoadingTicker = signal<boolean>(true);

  // Time-based Greeting
  greeting = signal<string>('Good afternoon');

  // Search Typeahead
  searchQuery = signal<string>('');
  searchResults = signal<Company[]>([]);
  isSearching = signal<boolean>(false);
  isDropdownOpen = signal<boolean>(false);
  failedSearchLogos = signal<Record<string, boolean>>({});
  allStocks = signal<Nifty50StockItem[]>([]);
  @ViewChild('searchSection') searchSection?: ElementRef<HTMLElement>;
  private searchSubject = new Subject<string>();
  private searchSub?: Subscription;

  onSearchLogoError(symbol?: string): void {
    if (!symbol) return;
    this.failedSearchLogos.update(map => ({ ...map, [symbol.toUpperCase()]: true }));
  }

  // Active Lens Tab
  activeLens = signal<LensType>('Growth');
  lensTabs: LensType[] = ['Growth', 'Cash Flow', 'Ownership', 'Risk'];

  // Real Explore Lens Data
  currentLensItems = signal<ExploreLensCompany[]>([]);
  focusedCompany = signal<ExploreLensCompany | null>(null);
  isLoadingLens = signal<boolean>(false);

  // Continue Research Section State (100% Dynamic - driven by WatchlistService, Backend API & browser history)
  activeResearchTab = signal<'recent' | 'watchlist'>('recent');

  // Map of stock data fetched dynamically from backend (same service & source as Nifty50 page)
  readonly backendStocksMap = signal<Map<string, Nifty50StockItem>>(new Map());

  // Failed logo tracking for clean fallback
  readonly failedLogos = signal<Record<string, boolean>>({});

  onLogoError(symbol?: string): void {
    if (!symbol) return;
    this.failedLogos.update(map => ({ ...map, [symbol.toUpperCase()]: true }));
  }

  getCompanyLogo(comp?: ExploreLensCompany | null): string | null {
    if (!comp) return null;
    const sym = comp.symbol.toUpperCase();
    if (this.failedLogos()[sym]) return null;
    return comp.logoUrl || this.backendStocksMap().get(sym)?.logoUrl || null;
  }

  currentResearchItems = computed<RecentStockItem[]>(() => {
    const tab = this.activeResearchTab();
    // 1. Get real symbols from WatchlistService or browser history (RecentResearchService)
    const symbols = tab === 'watchlist'
      ? this.watchlistService.watchlistSymbols()
      : this.recentService.recentSymbols();

    if (!symbols || symbols.length === 0) return [];

    const stockMap = this.backendStocksMap();
    const lensList = this.currentLensItems();

    return symbols.map(sym => {
      const upper = sym.toUpperCase();
      const backendStock = stockMap.get(upper);
      const meta = this.watchlistService.getMeta(upper);
      const lensCompany = lensList.find(c => c.symbol.toUpperCase() === upper);

      // Dynamic metadata from backend API, exactly like Nifty 50 page
      const name = backendStock?.companyName || meta?.companyName || lensCompany?.name || upper;
      const sector = backendStock?.sector || meta?.sector || lensCompany?.sector || 'Equities';
      const priceVal = backendStock?.price ?? meta?.price ?? lensCompany?.currentPrice;
      const mcapVal = backendStock?.marketCap ?? meta?.marketCap;
      const logo = backendStock?.logoUrl || meta?.logoUrl || lensCompany?.logoUrl;

      const letter = (name.charAt(0) || upper.charAt(0)).toUpperCase();
      const styling = this.recentService.computePalette(upper);

      return {
        symbol: upper,
        name,
        sector,
        price: priceVal,
        formattedPrice: priceVal && priceVal > 0 
          ? `₹${priceVal.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}` 
          : '—',
        marketCap: mcapVal,
        formattedMarketCap: mcapVal && mcapVal > 0 
          ? this.formatMarketCapCr(mcapVal) 
          : '—',
        avatarLetter: letter,
        avatarBg: styling.bg,
        avatarColor: styling.color,
        logoUrl: logo
      };
    });
  });

  constructor() {
    // Reactively fetch real backend data whenever watchlist or recent browser history changes
    effect(() => {
      const watch = this.watchlistService.watchlistSymbols();
      const rec = this.recentService.recentSymbols();
      const all = Array.from(new Set([...watch, ...rec])).map(s => s.toUpperCase());
      if (all.length > 0) {
        untracked(() => {
          this.fetchBackendStocks(all);
        });
      }
    });
  }

  // NIFTY 50 Market Cap Map (100% Dynamic from Backend API - No static data)
  niftyMapItems = signal<NiftyMapItem[]>([]);
  isLoadingNiftyMap = signal<boolean>(true);
  niftyMapRow1 = computed(() => this.niftyMapItems().slice(0, 6));
  niftyMapRow2 = computed(() => this.niftyMapItems().slice(6, 8));

  // Top 5 NIFTY 50 Companies by Market Cap (100% Dynamic from Backend API - No static data)
  niftyTop5 = signal<NiftyTop5Item[]>([]);
  isLoadingNiftyTop5 = signal<boolean>(true);
  niftyTotalConstituents = signal<number>(50);
  niftyTotalSectors = signal<number>(17);
  niftyTopWeightSymbol = signal<string>('RELIANCE');

  ngOnInit(): void {
    this.updateGreeting();
    this.loadTickers();
    this.loadExploreLens(this.activeLens());
    this.setupSearch();
    this.loadNiftyTop5();
    this.loadNifty50Map();
    this.loadInitialBackendStocks();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
  }

  private loadInitialBackendStocks(): void {
    // 1. Populate constituents from Nifty 50 list into backendStocksMap
    this.indexService.getNifty50List().subscribe({
      next: (stocks) => {
        if (stocks && stocks.length > 0) {
          this.backendStocksMap.update(map => {
            const newMap = new Map(map);
            for (const s of stocks) {
              newMap.set(s.symbol.toUpperCase(), s);
            }
            return newMap;
          });
        }
      }
    });

    // 2. Fetch details for all symbols in watchlist and recent search history from backend
    const allSymbols = Array.from(new Set([
      ...this.watchlistService.watchlistSymbols(),
      ...this.recentService.recentSymbols()
    ])).map(s => s.toUpperCase());

    this.fetchBackendStocks(allSymbols);
  }

  fetchBackendStocks(symbols: string[]): void {
    if (!symbols || symbols.length === 0) return;
    this.indexService.getStocksBySymbols(symbols).subscribe({
      next: (stocks) => {
        if (!stocks || stocks.length === 0) return;
        this.backendStocksMap.update(currentMap => {
          const newMap = new Map(currentMap);
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
        console.warn('Could not fetch backend stocks:', err);
      }
    });
  }

  private updateGreeting(): void {
    const hour = new Date().getHours();
    if (hour < 12) {
      this.greeting.set('Good morning');
    } else if (hour < 17) {
      this.greeting.set('Good afternoon');
    } else {
      this.greeting.set('Good evening');
    }
  }

  loadTickers(forceRefresh = false): void {
    this.isLoadingTicker.set(true);
    this.tickerService.getTickerQuotes(forceRefresh).subscribe({
      next: (quotes) => {
        this.tickerQuotes.set(quotes);
        this.isLoadingTicker.set(false);
      },
      error: (err) => {
        console.error('Error in loadTickers:', err);
        this.isLoadingTicker.set(false);
      }
    });
  }

  loadExploreLens(lens: LensType, forceRefresh = false): void {
    this.isLoadingLens.set(true);
    this.tickerService.getExploreLensData(lens, forceRefresh).subscribe({
      next: (res) => {
        if (res && res.companies && res.companies.length > 0) {
          this.currentLensItems.set(res.companies);

          // Seed backendStocksMap with explore lens logos
          this.backendStocksMap.update(currentMap => {
            const newMap = new Map(currentMap);
            for (const c of res.companies) {
              const upper = c.symbol.toUpperCase();
              const existing = newMap.get(upper);
              if (c.logoUrl && (!existing || !existing.logoUrl)) {
                newMap.set(upper, {
                  ...(existing || {}),
                  symbol: upper,
                  companyName: c.name || existing?.companyName || upper,
                  sector: c.sector || existing?.sector || 'Equities',
                  logoUrl: c.logoUrl,
                  price: c.currentPrice || existing?.price
                } as any);
              }
            }
            return newMap;
          });

          // Keep previously focused company if still present, otherwise default to first (e.g. INFY)
          const currentSym = this.focusedCompany()?.symbol;
          const matched = res.companies.find(c => c.symbol === currentSym) || res.companies[0];
          this.focusedCompany.set(matched);
        }
        this.isLoadingLens.set(false);
      },
      error: (err) => {
        console.warn('Failed to load real explore lens data, using existing state:', err);
        this.isLoadingLens.set(false);
      }
    });
  }

  setLens(lens: LensType): void {
    if (this.activeLens() === lens) return;
    this.activeLens.set(lens);
    this.loadExploreLens(lens);
  }

  focusCompany(item: ExploreLensCompany): void {
    this.focusedCompany.set(item);
  }

  // Coordinates helper for radial planet nodes on the radar
  getPlanetLeft(item: ExploreLensCompany): number {
    const rad = (item.orbitAngle * Math.PI) / 180;
    // Center is 240, 240; badge diameter is 62px => half is 31px
    return Math.round(240 + item.orbitRadius * Math.cos(rad) - 31);
  }

  getPlanetTop(item: ExploreLensCompany): number {
    const rad = (item.orbitAngle * Math.PI) / 180;
    return Math.round(240 + item.orbitRadius * Math.sin(rad) - 31);
  }

  // Radar Sector beam angle for focused item
  getRadarBeamTransform(): string {
    const angle = this.focusedCompany()?.orbitAngle ?? 320;
    return `rotate(${angle - 25} 240 240)`;
  }

  // Navigation
  goToStock(symbol?: string): void {
    const sym = symbol || this.focusedCompany()?.symbol;
    if (sym) {
      this.recentService.addRecentSymbol(sym);
      this.fetchBackendStocks([sym]);
      this.router.navigate(['/stock', sym.toUpperCase()]);
    }
  }

  // Continue Research Actions
  setResearchTab(tab: 'recent' | 'watchlist'): void {
    this.activeResearchTab.set(tab);
  }

  isStarred(symbol: string): boolean {
    return this.watchlistService.isInWatchlist(symbol);
  }

  toggleStar(item: RecentStockItem, event: MouseEvent): void {
    event.stopPropagation();
    const isNowStarred = this.watchlistService.toggleWatchlist(item.symbol, {
      companyName: item.name,
      sector: item.sector,
      price: item.price,
      marketCap: item.marketCap
    });
    if (isNowStarred) {
      this.fetchBackendStocks([item.symbol]);
    }
  }

  openResearchStock(item: RecentStockItem): void {
    this.recentService.addRecentSymbol(item.symbol);
    this.router.navigate(['/stock', item.symbol]);
  }

  goToMarketMap(): void {
    this.router.navigate(['/nifty50']);
  }

  getSevenDayChangeText(comp: ExploreLensCompany): string {
    if (comp.formattedSevenDayChange) {
      return comp.formattedSevenDayChange;
    }
    if (comp.sevenDayPrices && comp.sevenDayPrices.length >= 2) {
      const first = comp.sevenDayPrices[0];
      const last = comp.sevenDayPrices[comp.sevenDayPrices.length - 1];
      if (first > 0) {
        const pct = ((last - first) / first) * 100;
        return `${pct >= 0 ? '+' : ''}${pct.toFixed(1)}%`;
      }
    }
    return '';
  }

  formatMetricValue(m: LensMetric): string {
    if (!m || !m.value) return '';
    const v = m.value.trim();
    if (v === '+0.0%' || v === '-0.0%' || v === '+0%' || v === '-0%') {
      return '0.0%';
    }
    return v;
  }

  isMetricPositive(m: LensMetric): boolean {
    if (!m || !m.value) return false;
    const v = m.value.trim();
    if (v === '0.0%' || v === '+0.0%' || v === '-0.0%' || v === '0%') {
      return false;
    }
    return m.isPositive === true;
  }

  isMetricNegative(m: LensMetric): boolean {
    if (!m || !m.value) return false;
    const v = m.value.trim();
    if (v === '0.0%' || v === '+0.0%' || v === '-0.0%' || v === '0%') {
      return false;
    }
    return m.isPositive === false || v.startsWith('-');
  }

  // Search Typeahead Setup
  private setupSearch(): void {
    this.searchSub = this.searchSubject.pipe(
      debounceTime(200),
      distinctUntilChanged(),
      switchMap(query => {
        const clean = (query || '').trim();
        if (!clean || clean.length < 2) {
          this.isSearching.set(false);
          this.searchResults.set([]);
          this.isDropdownOpen.set(false);
          return of([]);
        }
        this.isSearching.set(true);

        const localMatches: Company[] = this.allStocks()
          .filter(s =>
            s.symbol.toLowerCase().includes(clean.toLowerCase()) ||
            s.companyName.toLowerCase().includes(clean.toLowerCase())
          )
          .map(s => ({
            id: 0,
            symbol: s.symbol,
            companyName: s.companyName,
            industry: s.sector || '',
            exchange: s.exchange || 'NSE',
            logoUrl: s.logoUrl
          } as Company));

        return this.newsService.searchCompanies(clean).pipe(
          map(apiRes => {
            if (apiRes && apiRes.length > 0) {
              return apiRes.map(c => {
                const upperSym = c.symbol.toUpperCase();
                return {
                  ...c,
                  logoUrl: c.logoUrl || this.backendStocksMap().get(upperSym)?.logoUrl || this.allStocks().find(s => s.symbol.toUpperCase() === upperSym)?.logoUrl
                };
              });
            }
            return localMatches;
          }),
          catchError(() => of(localMatches))
        );
      })
    ).subscribe({
      next: (results) => {
        this.searchResults.set(results || []);
        this.isSearching.set(false);
        if (results && results.length > 0) {
          this.isDropdownOpen.set(true);
        }
      },
      error: () => {
        this.searchResults.set([]);
        this.isSearching.set(false);
        this.isDropdownOpen.set(false);
      }
    });
  }

  onSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchQuery.set(val);
    if (val.trim().length >= 2) {
      this.isDropdownOpen.set(true);
    } else {
      this.isDropdownOpen.set(false);
      this.searchResults.set([]);
    }
    this.searchSubject.next(val);
  }

  onSearchFocus(): void {
    if (this.searchResults().length > 0 && this.searchQuery().trim().length >= 2) {
      this.isDropdownOpen.set(true);
    }
  }

  closeDropdown(): void {
    this.isDropdownOpen.set(false);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    if (this.isDropdownOpen() && this.searchSection?.nativeElement && !this.searchSection.nativeElement.contains(target)) {
      this.isDropdownOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscapePress(): void {
    this.isDropdownOpen.set(false);
  }

  selectSearchResult(company: Company): void {
    this.isDropdownOpen.set(false);
    this.searchQuery.set('');
    this.searchResults.set([]);
    this.recentService.addRecentSymbol(company.symbol);
    this.fetchBackendStocks([company.symbol]);
    this.router.navigate(['/stock', company.symbol]);
  }

  onSearchSubmit(): void {
    this.isDropdownOpen.set(false);
    const q = this.searchQuery().trim();
    if (q) {
      this.recentService.addRecentSymbol(q);
      this.fetchBackendStocks([q]);
      this.router.navigate(['/stock', q.toUpperCase()]);
    }
  }

  navigateToNifty50(): void {
    this.router.navigate(['/nifty50']);
  }

  private loadNiftyTop5(): void {
    this.isLoadingNiftyTop5.set(true);
    this.indexService.getNifty50List().subscribe({
      next: (stocks) => {
        if (stocks && stocks.length > 0) {
          this.allStocks.set(stocks);
          this.backendStocksMap.update(currentMap => {
            const newMap = new Map(currentMap);
            for (const s of stocks) {
              const sym = s.symbol.toUpperCase();
              if (!newMap.has(sym)) {
                newMap.set(sym, s);
              } else if (s.logoUrl && !newMap.get(sym)?.logoUrl) {
                newMap.set(sym, { ...newMap.get(sym)!, logoUrl: s.logoUrl });
              }
            }
            return newMap;
          });

          // Dynamic constituents and sectors count from real API data
          this.niftyTotalConstituents.set(stocks.length);
          const uniqueSectors = new Set(stocks.map(s => s.sector).filter(Boolean));
          if (uniqueSectors.size > 0) {
            this.niftyTotalSectors.set(uniqueSectors.size);
          }

          const validStocks = stocks.filter(s => s.marketCap && s.marketCap > 0);
          const sorted = (validStocks.length >= 5 ? validStocks : stocks)
            .sort((a, b) => (b.marketCap || 0) - (a.marketCap || 0))
            .slice(0, 5);

          if (sorted.length > 0) {
            this.niftyTopWeightSymbol.set(sorted[0].symbol);
            const maxCap = sorted[0].marketCap || 1;
            const colors = ['#d4f77c', '#bee6fd', '#fed7aa', '#fed7aa', '#fed7aa'];

            this.niftyTop5.set(sorted.map((s, idx) => ({
              name: s.companyName || s.symbol,
              symbol: s.symbol,
              marketCapFormatted: s.marketCap && s.marketCap > 0 ? this.formatMarketCapCr(s.marketCap) : '—',
              marketCapRaw: s.marketCap || 0,
              barPercent: maxCap > 0 ? Math.round(((s.marketCap || 0) / maxCap) * 1000) / 10 : 50,
              color: colors[idx] || '#fed7aa'
            })));
          }
        }
        this.isLoadingNiftyTop5.set(false);
      },
      error: (err) => {
        console.warn('Could not load Nifty 50 constituents from API:', err);
        this.isLoadingNiftyTop5.set(false);
      }
    });
  }

  private formatMarketCapCr(val: number): string {
    const rounded = Math.round(val);
    return `₹${rounded.toLocaleString('en-IN')} Cr`;
  }

  private loadNifty50Map(): void {
    this.isLoadingNiftyMap.set(true);
    this.indexService.getNifty50List().subscribe({
      next: (stocks) => {
        if (stocks && stocks.length > 0) {
          const sorted = [...stocks]
            .filter(s => s.marketCap && s.marketCap > 0)
            .sort((a, b) => (b.marketCap || 0) - (a.marketCap || 0))
            .slice(0, 8);

          if (sorted.length > 0) {
            const mapped: NiftyMapItem[] = sorted.map(s => {
              const cap = s.marketCap || 0;
              const sectorColor = this.getSectorMapColor(s.sector);
              return {
                symbol: s.symbol,
                name: s.companyName || s.symbol,
                sector: s.sector || 'Equities',
                marketCapRaw: cap,
                formattedLakhCr: this.formatMarketCapLakhCr(cap),
                bg: sectorColor.bg,
                textColor: sectorColor.text,
                flexWeight: Math.max(Math.round(cap / 10000), 20)
              };
            });
            this.niftyMapItems.set(mapped);
          }
        }
        this.isLoadingNiftyMap.set(false);
      },
      error: (err) => {
        console.warn('Could not load Nifty 50 market cap map from API:', err);
        this.isLoadingNiftyMap.set(false);
      }
    });
  }

  formatMarketCapLakhCr(mcapInCrores: number): string {
    if (!mcapInCrores || mcapInCrores <= 0) return '—';
    const lakhCr = mcapInCrores / 100000;
    return `₹${lakhCr.toFixed(1)} L Cr`;
  }

  getSectorMapColor(sector?: string): { bg: string; text: string } {
    if (!sector) return { bg: '#e8ede0', text: '#14180d' };
    const s = sector.toUpperCase().trim();

    if (s.includes('FINANC') || s.includes('BANK')) {
      return { bg: '#fedbb3', text: '#14180d' };
    }
    if (s.includes('OIL') || s.includes('GAS') || s.includes('FUEL') || s.includes('ENERGY')) {
      return { bg: '#e2f99d', text: '#14180d' };
    }
    if (s.includes('TELECOM')) {
      return { bg: '#c9e8fd', text: '#14180d' };
    }
    if (s.includes('TECH') || s.includes('IT') || s.includes('SOFTWARE')) {
      return { bg: '#ebdcfb', text: '#14180d' };
    }
    if (s.includes('CONSTRUCT') || s.includes('CAPITAL') || s.includes('INDUSTRIAL') || s.includes('INFRA')) {
      return { bg: '#cbf3db', text: '#14180d' };
    }
    if (s.includes('AUTO')) {
      return { bg: '#fee2e2', text: '#14180d' };
    }
    if (s.includes('CONSUMER') || s.includes('FMCG') || s.includes('FOOD')) {
      return { bg: '#fef08a', text: '#14180d' };
    }
    if (s.includes('HEALTH') || s.includes('PHARMA')) {
      return { bg: '#e0e7ff', text: '#14180d' };
    }
    if (s.includes('METAL') || s.includes('MINING') || s.includes('STEEL')) {
      return { bg: '#e2e8f0', text: '#14180d' };
    }
    return { bg: '#fedbb3', text: '#14180d' };
  }
}
