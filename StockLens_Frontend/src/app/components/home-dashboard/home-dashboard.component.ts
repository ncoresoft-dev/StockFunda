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
import { HeaderComponent } from '../header/header.component';
import { formatCurrency, formatCrores } from '../../utils/formatters.util';
import { computeSquarifiedTreemap, TreemapBounds } from '../../utils/squarified-treemap.util';

export type LensType = 'Growth' | 'Cash Flow' | 'Ownership' | 'Risk';

export interface HeatmapTile {
  symbol: string;
  name: string;
  sector: string;
  marketCap: number;
  formattedMarketCap: string;
  formattedMarketCapCr: string;
  price: number;
  formattedPrice: string;
  changePercent: number;
  formattedChange: string;
  isPositive: boolean;
  weightPercent: number;
  logoUrl?: string;

  leftPct: number;
  topPct: number;
  widthPct: number;
  heightPct: number;

  bgColor: string;
  textColor: string;
  borderColor: string;
  badgeBg: string;
  badgeColor: string;
  sizeTier: 'hero' | 'large' | 'medium' | 'small' | 'micro';
  isNarrow?: boolean;
  isShort?: boolean;
}

export interface HeatmapStats {
  totalMarketCapFormatted: string;
  stockCount: number;
  advances: number;
  declines: number;
  topStockSymbol: string;
  topStockWeight: string;
}

@Component({
  selector: 'app-home-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, HeaderComponent],
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
          ? formatCurrency(priceVal, 2) 
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

  // Heatmap View State (Default 11 companies with Market Cap color design)
  heatmapLimit = signal<number>(11);
  hoveredTile = signal<HeatmapTile | null>(null);
  hoverTooltipPos = signal<{ x: number; y: number } | null>(null);

  // Full 50 Nifty Constituents with live merges
  niftyHeatmapStocks = signal<Nifty50StockItem[]>([]);

  // Pure dynamic Nifty 50 constituents from API, enriched with live quote/lens data
  enrichedHeatmapStocks = computed<Nifty50StockItem[]>(() => {
    const apiStocks = this.niftyHeatmapStocks();
    if (!apiStocks || apiStocks.length === 0) return [];

    const backendMap = this.backendStocksMap();
    const exploreList = this.currentLensItems();
    const tickerList = this.tickerQuotes();

    const exploreMap = new Map(exploreList.map(c => [c.symbol.toUpperCase(), c]));
    const tickerMap = new Map(tickerList.map(t => [t.symbol.toUpperCase(), t]));

    const mergedList: Nifty50StockItem[] = apiStocks.map(s => {
      const upper = s.symbol.toUpperCase();
      const backendItem = backendMap.get(upper);
      const exploreItem = exploreMap.get(upper);
      const tickerItem = tickerMap.get(upper);

      const price = s.price ?? backendItem?.price ?? exploreItem?.currentPrice ?? (tickerItem?.currentPrice ?? 0);
      const marketCap = s.marketCap ?? backendItem?.marketCap ?? 0;
      const sector = s.sector || backendItem?.sector || 'Equities';
      const companyName = s.companyName || backendItem?.companyName || s.symbol;
      const logoUrl = s.logoUrl || backendItem?.logoUrl || undefined;
      const changePct = exploreItem?.changePercent ?? (tickerItem ? tickerItem.changePercent : (s.profitGrowthYoy || 0));

      return {
        ...s,
        symbol: upper,
        companyName,
        sector,
        price,
        marketCap,
        logoUrl,
        profitGrowthYoy: changePct
      } as Nifty50StockItem;
    });

    const totalCap = mergedList.reduce((sum, s) => sum + (s.marketCap || 0), 0);
    if (totalCap > 0) {
      mergedList.forEach(s => {
        s.weightagePercentage = Math.round(((s.marketCap || 0) / totalCap) * 10000) / 100;
      });
    }

    return mergedList;
  });

  // Overall Heatmap Stats for the top 7 to 9 displayed Nifty 50 market cap leaders
  heatmapStats = computed<HeatmapStats>(() => {
    const allStocks = this.enrichedHeatmapStocks();
    const limit = this.heatmapLimit();
    const topStocks = [...allStocks]
      .filter(s => s.marketCap && s.marketCap > 0)
      .sort((a, b) => (b.marketCap || 0) - (a.marketCap || 0))
      .slice(0, limit);

    const totalCap = topStocks.reduce((sum, s) => sum + (s.marketCap || 0), 0);
    const advances = topStocks.filter(s => (s.profitGrowthYoy || 0) >= 0).length;
    const declines = topStocks.filter(s => (s.profitGrowthYoy || 0) < 0).length;

    const sectorMap = new Map<string, number>();
    topStocks.forEach(s => {
      const sec = s.sector || 'Others';
      sectorMap.set(sec, (sectorMap.get(sec) || 0) + (s.marketCap || 0));
    });

    let topSecName = 'Financial Services';
    let topSecCap = 0;
    sectorMap.forEach((cap, sec) => {
      if (cap > topSecCap) {
        topSecCap = cap;
        topSecName = sec;
      }
    });
    const topSecWeight = totalCap > 0 ? ((topSecCap / totalCap) * 100).toFixed(1) : '33.8';

    const topStock = topStocks[0];
    const topStockSym = topStock ? topStock.symbol : 'RELIANCE';
    const topStockWeight = totalCap > 0 && topStock ? (((topStock.marketCap || 0) / totalCap) * 100).toFixed(1) : '22.5';

    return {
      totalMarketCapFormatted: this.formatMarketCapLakhCr(totalCap),
      totalMarketCapCr: totalCap,
      stockCount: topStocks.length,
      advances,
      declines,
      topSectorName: topSecName,
      topSectorWeight: `${topSecWeight}%`,
      topStockSymbol: topStockSym,
      topStockWeight: `${topStockWeight}%`
    };
  });

  // Treemap View showing only 7 to 9 companies strictly from the Nifty 50 list (Area strictly proportional to Market Cap)
  heatmapUnifiedTiles = computed<HeatmapTile[]>(() => {
    const allStocks = this.enrichedHeatmapStocks();
    const limit = this.heatmapLimit();

    if (allStocks.length === 0) return [];

    // Filter strictly to the top 7 to 9 companies by market cap from the Nifty 50 list
    const topStocks = [...allStocks]
      .filter(s => s.marketCap && s.marketCap > 0)
      .sort((a, b) => (b.marketCap || 0) - (a.marketCap || 0))
      .slice(0, limit);

    const totalGroupCap = topStocks.reduce((sum, s) => sum + (s.marketCap || 0), 0);
    const maxStockCap = Math.max(...topStocks.map(s => s.marketCap || 0), 1);

    const stockItems = topStocks.map(s => ({
      ...s,
      value: s.marketCap || 1000
    }));

    const bounds: TreemapBounds = { x: 0, y: 0, width: 1000, height: 500 };
    const stockLayout = computeSquarifiedTreemap(stockItems, bounds);

    return stockLayout.map(st => {
      const stock = st.item;
      const changeVal = stock.profitGrowthYoy || 0;
      const isPos = changeVal >= 0;
      const mcap = stock.marketCap || 0;
      const sizeTier = this.computeTileSizeTier(st.width, st.height);
      const isNarrow = st.width < 170;
      const isShort = st.height < 100;
      const mcapStyle = this.getTileMcapDensityStyle(mcap, maxStockCap);
      const bg = mcapStyle.bg;
      const text = mcapStyle.text;
      const border = mcapStyle.border;
      const badgeBg = mcapStyle.badgeBg;
      const badgeColor = mcapStyle.badgeColor;

      return {
        symbol: stock.symbol,
        name: stock.companyName || stock.symbol,
        sector: stock.sector || 'Equities',
        marketCap: mcap,
        formattedMarketCap: this.formatMarketCapLakhCr(mcap),
        formattedMarketCapCr: this.formatMarketCapCr(mcap),
        price: stock.price || 0,
        formattedPrice: stock.price ? formatCurrency(stock.price, 2) : '—',
        changePercent: changeVal,
        formattedChange: `${isPos ? '+' : ''}${changeVal.toFixed(2)}%`,
        isPositive: isPos,
        weightPercent: totalGroupCap > 0 ? (mcap / totalGroupCap) * 100 : 0,
        logoUrl: stock.logoUrl,
        leftPct: st.leftPct,
        topPct: st.topPct,
        widthPct: st.widthPct,
        heightPct: st.heightPct,
        bgColor: bg,
        textColor: text,
        borderColor: border,
        badgeBg,
        badgeColor,
        sizeTier,
        isNarrow,
        isShort
      };
    });
  });

  // Heatmap Loading State
  isLoadingNiftyMap = signal<boolean>(true);

  ngOnInit(): void {
    this.updateGreeting();
    this.loadTickers();
    this.loadExploreLens(this.activeLens());
    this.setupSearch();
    this.loadNifty50Data();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
  }

  private lastFetchedSymbolsKey = '';

  fetchBackendStocks(symbols: string[]): void {
    if (!symbols || symbols.length === 0) return;
    const cleanSymbols = Array.from(new Set(symbols.map(s => s.trim().toUpperCase()))).filter(Boolean).sort();
    const key = cleanSymbols.join(',');
    if (this.lastFetchedSymbolsKey === key) return; // Prevent duplicate network calls for identical symbols
    this.lastFetchedSymbolsKey = key;

    this.indexService.getStocksBySymbols(cleanSymbols).subscribe({
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
    this.tickerService.getTickerQuotes(forceRefresh).subscribe({
      next: (quotes) => {
        this.tickerQuotes.set(quotes);
      },
      error: (err) => {
        console.error('Error in loadTickers:', err);
      }
    });
  }

  loadExploreLens(lens: LensType, forceRefresh = false): void {
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
      },
      error: (err) => {
        console.warn('Failed to load real explore lens data, using existing state:', err);
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

  private loadNifty50Data(): void {
    this.isLoadingNiftyMap.set(true);

    this.indexService.getNifty50List().subscribe({
      next: (stocks) => {
        if (stocks && stocks.length > 0) {
          this.allStocks.set(stocks);
          this.niftyHeatmapStocks.set(stocks);

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
        }
        this.isLoadingNiftyMap.set(false);
      },
      error: (err) => {
        console.warn('Could not load Nifty 50 constituents from API:', err);
        this.isLoadingNiftyMap.set(false);
      }
    });
  }

  private formatMarketCapCr(val: number): string {
    return formatCrores(val, 0);
  }

  trackBySymbol(_index: number, tile: HeatmapTile): string {
    return tile.symbol;
  }

  computeTileSizeTier(width: number, height: number): 'hero' | 'large' | 'medium' | 'small' | 'micro' {
    if ((width >= 110 && height >= 70) || (width * height >= 18000)) return 'hero';
    if (width >= 85 && height >= 52) return 'large';
    if (width >= 50 && height >= 36) return 'medium';
    if (width >= 32 && height >= 22) return 'small';
    return 'micro';
  }

  getTileMcapDensityStyle(mcap: number, maxCap?: number): { bg: string; text: string; border: string; badgeBg: string; badgeColor: string } {
    const lakhCr = (mcap || 0) / 100000;

    // Step 4: > ₹14 L Cr (matches legend .mcap-step.s4: #064e3b)
    if (lakhCr >= 11.5) {
      return { 
        bg: '#064e3b', 
        text: '#ffffff', 
        border: '#ffffff', 
        badgeBg: 'rgba(255, 255, 255, 0.22)', 
        badgeColor: '#ffffff' 
      };
    }

    // Step 3: ₹8 L Cr (matches legend .mcap-step.s3: #047857)
    if (lakhCr >= 6.5) {
      return { 
        bg: '#047857', 
        text: '#ffffff', 
        border: '#ffffff', 
        badgeBg: 'rgba(255, 255, 255, 0.20)', 
        badgeColor: '#ffffff' 
      };
    }

    // Step 2: ₹5 L Cr (matches legend .mcap-step.s2: #10b981)
    if (lakhCr >= 3.5) {
      return { 
        bg: '#10b981', 
        text: '#ffffff', 
        border: '#ffffff', 
        badgeBg: 'rgba(0, 0, 0, 0.16)', 
        badgeColor: '#ffffff' 
      };
    }

    // Step 1: < ₹3 L Cr (matches legend .mcap-step.s1: #6ee7b7)
    return { 
      bg: '#6ee7b7', 
      text: '#064e3b', 
      border: '#ffffff', 
      badgeBg: 'rgba(6, 78, 59, 0.12)', 
      badgeColor: '#064e3b' 
    };
  }

  onHeatmapTileEnter(tile: HeatmapTile, event: MouseEvent): void {
    this.hoveredTile.set(tile);
    this.positionTooltipForTile(tile, event);
  }

  onHeatmapTileLeave(): void {
    this.hoveredTile.set(null);
    this.hoverTooltipPos.set(null);
  }

  private positionTooltipForTile(tile: HeatmapTile, event: MouseEvent): void {
    const target = event.currentTarget as HTMLElement;
    const container = target?.closest('.heatmap-canvas-wrapper') as HTMLElement;
    if (!container) return;

    const containerRect = container.getBoundingClientRect();
    const containerW = containerRect.width;
    const containerH = containerRect.height;

    // Fixed tooltip dimensions
    const tooltipW = 250;
    const tooltipH = 195;
    const margin = 12;

    // If narrow viewport (e.g. mobile/small tablet), center horizontally
    if (containerW < 560) {
      const tileRect = target.getBoundingClientRect();
      const tileTop = tileRect.top - containerRect.top;
      const tileH = tileRect.height;

      const x = Math.round((containerW - tooltipW) / 2);
      let y = tileTop < containerH / 2 
        ? tileTop + tileH + 8 
        : tileTop - tooltipH - 8;
      y = Math.max(margin, Math.min(y, containerH - tooltipH - margin));
      this.hoverTooltipPos.set({ x: Math.max(margin, x), y: Math.round(y) });
      return;
    }

    const tileRect = target.getBoundingClientRect();
    const tileLeft = tileRect.left - containerRect.left;
    const tileTop = tileRect.top - containerRect.top;
    const tileW = tileRect.width;
    const tileH = tileRect.height;

    const tileCenterX = tileLeft + tileW / 2;
    const tileCenterY = tileTop + tileH / 2;

    let x: number;
    // If tile is in the right half of the canvas, place tooltip to the LEFT of the tile
    if (tileCenterX > containerW / 2) {
      x = tileLeft - tooltipW - 10;
      if (x < margin) {
        x = tileLeft + tileW + 10;
        if (x + tooltipW > containerW - margin) {
          x = containerW - tooltipW - margin;
        }
      }
    } else {
      // Tile is in the left half: place tooltip to the RIGHT of the tile
      x = tileLeft + tileW + 10;
      if (x + tooltipW > containerW - margin) {
        x = tileLeft - tooltipW - 10;
        if (x < margin) {
          x = margin;
        }
      }
    }

    // Determine vertical placement: vertically align with tile center
    let y = tileCenterY - tooltipH / 2;

    // Hard boundary clamping: ensures zero clipping on all 4 borders
    x = Math.max(margin, Math.min(x, containerW - tooltipW - margin));
    y = Math.max(margin, Math.min(y, containerH - tooltipH - margin));

    this.hoverTooltipPos.set({ x: Math.round(x), y: Math.round(y) });
  }

  onHeatmapTileClick(tile: HeatmapTile): void {
    this.goToStock(tile.symbol);
  }

  formatMarketCapLakhCr(mcapInCrores: number): string {
    if (!mcapInCrores || mcapInCrores <= 0) return '—';
    const lakhCr = mcapInCrores / 100000;
    return `₹${lakhCr.toFixed(1)} L Cr`;
  }

}
