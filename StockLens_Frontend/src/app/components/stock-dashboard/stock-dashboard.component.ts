import { Component, OnInit, OnDestroy, inject, signal, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription, timer } from 'rxjs';
import { formatNumber, formatPercent, formatCurrency, formatCrores } from '../../utils/formatters.util';
import { StockNewsService } from '../../services/stock-news.service';
import { StockCompanyService } from '../../services/stock-company.service';
import { StockShareholdingService } from '../../services/stock-shareholding.service';
import { StockCashflowService } from '../../services/stock-cashflow.service';
import { StockBalanceSheetService, BalanceSheetResponseDto } from '../../services/stock-balancesheet.service';
import { StockQuarterlyResultsService } from '../../services/stock-quarterly-results.service';
import { StockEvaluationService } from '../../services/stock-evaluation.service';
import { StockPeerService } from '../../services/stock-peer.service';
import { StockIndexService } from '../../services/stock-index.service';
import { WatchlistService } from '../../services/watchlist.service';
import { StockAnnualReportService } from '../../services/stock-annual-report.service';
import { Stock, Company, StockNewsResponse, StockNewsItem, LoadingState } from '../../models/stock-news.model';
import { CompanyOverview } from '../../models/company-overview.model';
import { StockShareholdingResponse } from '../../models/stock-shareholding.model';
import { StockCashflowResponse } from '../../models/stock-cashflow.model';
import { StockQuarterlyResultsResponse } from '../../models/stock-quarterly-results.model';
import { StockHealthScoreResponse } from '../../models/stock-evaluation.model';
import { TimeAgoPipe } from '../../pipes/time-ago.pipe';
import { StockCandlestickChartComponent } from '../stock-candlestick-chart/stock-candlestick-chart.component';
import { StockTechnicalAnalysis } from '../stock-technical-analysis/stock-technical-analysis';
import { StockVolumeDeliveryCardComponent } from '../stock-volume-delivery-card/stock-volume-delivery-card.component';
import { StockCompanyAboutCardComponent } from '../stock-company-about-card/stock-company-about-card.component';
import { StockDealsCardComponent } from '../stock-deals-card/stock-deals-card.component';
import { StockPeerComparisonCardComponent } from '../stock-peer-comparison-card/stock-peer-comparison-card.component';
import { StockQuartersCardComponent } from '../stock-quarters-card/stock-quarters-card.component';
import { StockShareholdingCardComponent } from '../stock-shareholding-card/stock-shareholding-card.component';
import { StockCashflowCardComponent } from '../stock-cashflow-card/stock-cashflow-card.component';
import { StockAssetGrowthCardComponent } from '../stock-asset-growth-card/stock-asset-growth-card.component';

import { AdminAnnualReportModalComponent } from '../admin-annual-report-modal/admin-annual-report-modal.component';
import { HeaderComponent } from '../header/header.component';

export type NewsFilterTab = 'all' | 'filings' | 'announcements';
export type DetailModalType = null | 'ownership' | 'quarters' | 'profitability' | 'cashflow' | 'balancesheet' | 'valuation';
export type OverviewTabType = 'overview' | 'quarters' | 'ownership' | 'news' | 'chart' | 'volume' | 'cashflow' | 'balancesheet' | 'deals' | 'peers' | 'reports';

@Component({
  selector: 'app-stock-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    TimeAgoPipe,
    StockCandlestickChartComponent,
    StockTechnicalAnalysis,
    StockVolumeDeliveryCardComponent,
    StockDealsCardComponent,
    StockPeerComparisonCardComponent,
    StockCompanyAboutCardComponent,
    StockQuartersCardComponent,
    StockShareholdingCardComponent,
    StockCashflowCardComponent,
    StockAssetGrowthCardComponent,
    AdminAnnualReportModalComponent,
    HeaderComponent
  ],
  templateUrl: './stock-dashboard.component.html',
  styleUrl: './stock-dashboard.component.css'
})
export class StockDashboardComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly watchlistService = inject(WatchlistService);
  private readonly newsService = inject(StockNewsService);
  private readonly companyService = inject(StockCompanyService);
  private readonly peerService = inject(StockPeerService);
  private readonly shareholdingService = inject(StockShareholdingService);
  private readonly cashflowService = inject(StockCashflowService);
  private readonly financialsService = inject(StockBalanceSheetService);
  private readonly quartersService = inject(StockQuarterlyResultsService);
  private readonly evaluationService = inject(StockEvaluationService);
  private readonly indexService = inject(StockIndexService);
  readonly annualReportService = inject(StockAnnualReportService);
  private readonly cd = inject(ChangeDetectorRef);

  private routeSub?: Subscription;

  // Quick select stocks
  readonly quickStocks = [
    { symbol: 'RELIANCE', name: 'Reliance Industries', exchange: 'NSE' },
    { symbol: 'TATAMOTORS', name: 'Tata Motors Ltd', exchange: 'NSE' },
    { symbol: 'TCS', name: 'Tata Consultancy Services', exchange: 'NSE' },
    { symbol: 'INFY', name: 'Infosys Limited', exchange: 'NSE' },
    { symbol: 'HDFCBANK', name: 'HDFC Bank Ltd', exchange: 'NSE' },
    { symbol: 'ICICIBANK', name: 'ICICI Bank Ltd', exchange: 'NSE' }
  ];


  // Core Active State
  availableStocks = signal<Stock[]>([]);
  selectedSymbol = signal<string>('RELIANCE');
  selectedExchange = signal<string>('NSE');
  chartPeriod = signal<string>('1yr');
  candlestickPeriod = signal<string>('1yr');
  selectedChartType = signal<'candlestick' | 'area'>('candlestick');
  newsTab = signal<NewsFilterTab>('all');
  newsCategoryTab = signal<'all' | 'market' | 'filings'>('all');
  activeDetailModal = signal<DetailModalType>(null);
  isLoadingStock = signal<boolean>(false);
  deliveryAnalysisData = signal<any>(null);
  isAboutExpanded = signal<boolean>(false);
  hasActiveBreakouts = signal<boolean>(false);
  isLightTheme = signal<boolean>(true);
  selectedFinancialTab = signal<'cashflow' | 'balancesheet'>('cashflow');

  // Filter dropdown signals
  selectedOwnershipPeriod = signal<string>('Jun 2026');
  selectedFinancialsInterval = signal<'Quarterly' | 'Annual'>('Quarterly');
  selectedLatestQuarterPeriod = signal<string>('Jun 2026');
  lqTimeframe = signal<'1D' | '1W' | '1M' | '3M' | '1Y' | '5Y'>('1Y');

  // Market indices
  indices = signal([
    { name: 'NSE', value: '25,401.15', change: '+0.72%', isPositive: true },
    { name: 'BSE', value: '83,706.12', change: '+0.68%', isPositive: true }
  ]);

  private pricePollingSubscription?: Subscription;

  // Shareholding State
  shareholdingResponse = signal<StockShareholdingResponse | null>(null);
  shareholdingLoadingState = signal<LoadingState>('idle');
  shareholdingErrorMessage = signal<string>('');
  isShareholdingRefreshing = signal<boolean>(false);

  // Quarters State
  quartersResponse = signal<StockQuarterlyResultsResponse | null>(null);
  quartersLoadingState = signal<LoadingState>('idle');
  quartersErrorMessage = signal<string>('');
  isQuartersRefreshing = signal<boolean>(false);

  // Cashflow State
  cashflowResponse = signal<StockCashflowResponse | null>(null);
  cashflowLoadingState = signal<LoadingState>('idle');
  cashflowErrorMessage = signal<string>('');
  isCashflowRefreshing = signal<boolean>(false);

  // Assets State
  assetsResponse = signal<BalanceSheetResponseDto | null>(null);
  assetsLoadingState = signal<LoadingState>('idle');
  assetsErrorMessage = signal<string>('');
  isAssetsRefreshing = signal<boolean>(false);

  // News State
  newsResponse = signal<StockNewsResponse | null>(null);
  newsLoadingState = signal<LoadingState>('idle');
  newsErrorMessage = signal<string>('');
  isNewsRefreshing = signal<boolean>(false);

  // Stock Evaluation State (Good / Neutral / Bad Signal Engine)
  evaluationResponse = signal<StockHealthScoreResponse | null>(null);
  isEvaluationRefreshing = signal<boolean>(false);
  isEvaluationModalOpen = signal<boolean>(false);
  isAboutModalOpen = signal<boolean>(false);

  // Peers State
  companyPeers = signal<import('../../models/peer.model').Peer[]>([]);
  isPeersLoading = signal<boolean>(false);
  hasPeersError = signal<boolean>(false);

  // Company Overview & Logo State
  companyOverview = signal<CompanyOverview | null>(null);
  isLogoFailed = signal<boolean>(false);

  // Screener-style Overview Navigation Tabs State & ScrollSpy Engine
  activeOverviewTab = signal<OverviewTabType>('overview');
  private isManualScrolling = false;
  private scrollTimeout?: any;

  setActiveOverviewTab(tab: OverviewTabType): void {
    this.scrollToSection(tab);
  }

  scrollToSection(sectionId: OverviewTabType): void {
    this.activeOverviewTab.set(sectionId);
    this.isManualScrolling = true;
    if (this.scrollTimeout) clearTimeout(this.scrollTimeout);

    const el = document.getElementById('section-' + sectionId);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }

    this.scrollTimeout = setTimeout(() => {
      this.isManualScrolling = false;
    }, 800);
  }

  onCockpitScroll(event: Event): void {
    if (this.isManualScrolling) return;

    const container = event.target as HTMLElement;
    if (!container) return;

    const sections: OverviewTabType[] = [
      'overview',
      'news',
      'quarters',
      'ownership',
      'chart',
      'volume',
      'cashflow',
      'balancesheet',
      'deals',
      'peers',
      'reports'
    ];

    const containerRect = container.getBoundingClientRect();
    let detectedSection: OverviewTabType = 'overview';

    // 1. Check if user is scrolled near the bottom of the scroll container
    const isAtBottom = (container.scrollHeight - container.scrollTop - container.clientHeight) < 80;
    
    // 2. Check if Annual Report section is prominently visible on screen
    const reportsEl = document.getElementById('section-reports');
    const isReportsVisible = reportsEl ? (reportsEl.getBoundingClientRect().top - containerRect.top <= containerRect.height * 0.6) : false;

    if (isAtBottom && isReportsVisible) {
      detectedSection = 'reports';
    } else {
      for (const id of sections) {
        const el = document.getElementById('section-' + id);
        if (el) {
          const rect = el.getBoundingClientRect();
          const offsetFromContainerTop = rect.top - containerRect.top;
          if (offsetFromContainerTop <= 160) {
            detectedSection = id;
          }
        }
      }
    }

    if (this.activeOverviewTab() !== detectedSection) {
      this.activeOverviewTab.set(detectedSection);
    }
  }

  // Global Refresh State
  isSyncingAll = signal<boolean>(false);
  technicalRefreshTrigger = signal<number>(0);

  ngOnInit(): void {
    this.applyThemeToDom();
    this.loadAvailableStocks();

    const routeSym = this.route.snapshot.paramMap.get('symbol');
    if (routeSym) {
      this.selectedSymbol.set(routeSym.toUpperCase());
    }

    this.isLoadingStock.set(true);
    this.cashflowLoadingState.set('loading');
    this.shareholdingLoadingState.set('loading');
    this.quartersLoadingState.set('loading');
    this.assetsLoadingState.set('loading');
    this.newsLoadingState.set('loading');

    this.fetchAllData(false);
    this.startLivePricePolling();

    // Listen for route param changes (e.g. /stock/INFY)
    this.routeSub = this.route.paramMap.subscribe(params => {
      const sym = params.get('symbol');
      if (sym && sym.toUpperCase() !== this.selectedSymbol()) {
        this.selectStock(sym.toUpperCase(), this.selectedExchange());
      }
    });
  }

  navigateToHome(): void {
    this.router.navigate(['/']);
  }

  navigateToWatchlist(): void {
    this.router.navigate(['/nifty50'], { queryParams: { tab: 'watchlist' } });
  }

  navigateToAnnualReports(): void {
    this.router.navigate(['/annual-reports']);
  }

  /**
   * Checks if current selected stock is in user's personal watchlist
   */
  isCurrentStockStarred(): boolean {
    const sym = this.selectedSymbol();
    return !!sym && this.watchlistService.isInWatchlist(sym);
  }

  /**
   * Toggles the current selected stock in/out of the user's personal watchlist
   */
  toggleCurrentStockStar(): void {
    const sym = this.selectedSymbol();
    if (sym) {
      const price = this.cashflowResponse()?.ratios?.currentPrice;
      const marketCap = this.cashflowResponse()?.ratios?.marketCap;
      this.watchlistService.toggleWatchlist(sym, {
        symbol: sym,
        companyName: this.getCompanyName(),
        logoUrl: this.getCompanyLogoUrl() || undefined,
        price: price != null ? Number(price) : undefined,
        marketCap: marketCap != null ? Number(marketCap) : undefined,
        sector: this.getSector()
      });
    }
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

  ngOnDestroy(): void {
    this.routeSub?.unsubscribe();
    this.stopLivePricePolling();
    this.indexService.clearCache();
  }

  /**
   * Checks whether Indian Stock Market is open (09:15 AM to 03:30 PM IST, Mon-Fri)
   */
  isIndianMarketOpen(): boolean {
    const now = new Date();
    const utcTime = now.getTime() + (now.getTimezoneOffset() * 60000);
    const istDate = new Date(utcTime + (3600000 * 5.5));

    const day = istDate.getDay();
    if (day === 0 || day === 6) return false;

    const totalMinutes = istDate.getHours() * 60 + istDate.getMinutes();
    return totalMinutes >= 555 && totalMinutes <= 930;
  }

  startLivePricePolling(): void {
    this.stopLivePricePolling();
    this.pricePollingSubscription = timer(300000, 300000).subscribe(() => {
      if (this.isIndianMarketOpen()) {
        const symbol = this.selectedSymbol();
        const exchange = this.selectedExchange();

        this.cashflowService.getCashflowBySymbol(symbol, exchange, false).subscribe({
          next: (data) => {
            if (data && data.ratios && this.selectedSymbol() === symbol) {
              this.cashflowResponse.set(data);
              if (data.ratios.currentPrice) {
                this.indexService.updateStockInCache(symbol, data.ratios.currentPrice, data.ratios.marketCap);
                if (this.watchlistService.isInWatchlist(symbol)) {
                  this.watchlistService.setMeta(symbol, {
                    price: Number(data.ratios.currentPrice),
                    marketCap: data.ratios.marketCap ? Number(data.ratios.marketCap) : undefined
                  });
                }
              }
            }
          },
          error: (err) => console.debug('[Polling skipped]:', err)
        });
      }
    });
  }

  stopLivePricePolling(): void {
    if (this.pricePollingSubscription) {
      this.pricePollingSubscription.unsubscribe();
      this.pricePollingSubscription = undefined;
    }
  }



  loadAvailableStocks(): void {
    this.newsService.getStocks().subscribe({
      next: (stocks) => this.availableStocks.set(stocks),
      error: (err) => console.warn('Could not load stock list:', err)
    });
  }

  selectStock(symbol: string, exchange: string = 'NSE'): void {
    const cleanSymbol = symbol.toUpperCase();
    const cleanExchange = exchange.toUpperCase();

    // Prevent clearing data if the user searches for the exact same stock they are already viewing
    if (this.selectedSymbol() === cleanSymbol && this.selectedExchange() === cleanExchange) {
      return;
    }

    this.isLoadingStock.set(true);
    this.selectedSymbol.set(cleanSymbol);
    this.selectedExchange.set(cleanExchange);

    if (this.route.snapshot.paramMap.get('symbol') !== cleanSymbol) {
      this.router.navigate(['/stock', cleanSymbol]);
    }

    // Clear previous responses to show skeletons immediately
    this.cashflowResponse.set(null);
    this.quartersResponse.set(null);
    this.assetsResponse.set(null);
    this.shareholdingResponse.set(null);
    this.evaluationResponse.set(null);
    this.newsResponse.set(null);
    this.deliveryAnalysisData.set(null);
    this.companyOverview.set(null);
    this.isLogoFailed.set(false);
    this.hasActiveBreakouts.set(false);
    this.headerArFeedback.set('');
    this.annualReportService.activeReport.set(null);

    this.shareholdingLoadingState.set('loading');
    this.quartersLoadingState.set('loading');
    this.cashflowLoadingState.set('loading');
    this.assetsLoadingState.set('loading');
    this.newsLoadingState.set('loading');

    this.fetchAllData(false);
    this.startLivePricePolling();

  }

  toggleExchange(exchange: string): void {
    if (this.selectedExchange() !== exchange) {
      this.selectStock(this.selectedSymbol(), exchange);
    }
  }

  setChartPeriod(period: string): void {
    this.chartPeriod.set(period);
  }

  setCandlestickPeriod(period: string): void {
    this.candlestickPeriod.set(period);
  }

  setNewsTab(tab: NewsFilterTab): void {
    this.newsTab.set(tab);
  }

  openDetailModal(modalType: DetailModalType): void {
    this.activeDetailModal.set(modalType);
  }

  onPriceDataLoaded(analysisData: any): void {
    this.deliveryAnalysisData.set(analysisData);
  }

  closeDetailModal(): void {
    this.activeDetailModal.set(null);
  }



  onHeaderStockSelected(company: Company | Stock): void {
    if (company && company.symbol) {
      this.selectStock(company.symbol, this.selectedExchange());
    }
  }

  onHeaderSearchSubmit(query: string): void {
    if (query && query.trim()) {
      this.selectStock(query.trim(), this.selectedExchange());
    }
  }

  fetchAllData(isRefresh: boolean = false): void {
    if (isRefresh) {
      this.technicalRefreshTrigger.update(v => v + 1);
    }
    this.fetchCompanyOverview(isRefresh);
    this.fetchCompanyPeers(isRefresh);
    this.fetchCashflow(isRefresh);
    this.fetchBalanceSheet(isRefresh);
    this.fetchShareholding(isRefresh);
    this.fetchQuarterlyResults(isRefresh);
    this.fetchNews(isRefresh);
    this.fetchEvaluation(isRefresh);
    this.fetchAnnualReport(isRefresh);
  }

  headerArFeedback = signal<string>('');
  isHeaderArRequesting = signal<boolean>(false);

  fetchAnnualReport(isRefresh: boolean = false): void {
    const symbol = this.selectedSymbol();
    if (!symbol) return;
    this.annualReportService.getAnnualReports(symbol, isRefresh).subscribe();
  }

  requestAnnualReportFromHeader(): void {
    const symbol = this.selectedSymbol();
    if (!symbol || this.isHeaderArRequesting()) return;

    this.isHeaderArRequesting.set(true);
    this.headerArFeedback.set('');

    this.annualReportService.requestAnnualReport(symbol).subscribe({
      next: () => {
        this.isHeaderArRequesting.set(false);
        this.headerArFeedback.set('Requested!');
      },
      error: () => {
        this.isHeaderArRequesting.set(false);
        this.headerArFeedback.set('Failed');
        setTimeout(() => this.headerArFeedback.set(''), 3000);
      }
    });
  }

  fetchCompanyOverview(isRefresh: boolean = false): void {
    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();
    if (!symbol) return;
    this.isLogoFailed.set(false);

    this.companyService.getCompanyOverview(symbol, exchange, isRefresh).subscribe({
      next: (data) => {
        if (data && this.selectedSymbol() === symbol) {
          this.companyOverview.set(data);
        }
      },
      error: (err) => console.debug('Could not load company overview for logo/meta:', err)
    });
  }

  fetchCompanyPeers(isRefresh: boolean = false): void {
    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();
    if (!symbol) return;
    
    this.isPeersLoading.set(true);
    this.hasPeersError.set(false);

    this.peerService.getCompanyPeers(symbol, exchange, isRefresh).subscribe({
      next: (peers) => {
        if (this.selectedSymbol() === symbol) {
          this.companyPeers.set(peers || []);
        }
        this.isPeersLoading.set(false);
      },
      error: (err) => {
        console.debug('Could not load company peers:', err);
        this.hasPeersError.set(true);
        this.isPeersLoading.set(false);
      }
    });
  }

  fetchEvaluation(isRefresh: boolean): void {
    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();
    if (!symbol) return;

    if (isRefresh) {
      this.isEvaluationRefreshing.set(true);
    }

    this.evaluationService.getEvaluation(symbol, exchange, isRefresh).subscribe({
      next: (data) => {
        if (this.selectedSymbol() !== symbol) return;
        if (data) {
          this.evaluationResponse.set(data);
        } else {
        
        }
        this.isEvaluationRefreshing.set(false);
      },
      error: () => {
        if (this.selectedSymbol() !== symbol) return;
       
        this.isEvaluationRefreshing.set(false);
      }
    });
  }

  

  getEvaluation(): StockHealthScoreResponse {
    const existing = this.evaluationResponse();
    if (existing && existing.symbol === this.selectedSymbol().toUpperCase()) {
      return existing;
    }
    // Return placeholder while backend evaluation loads - do not compute score in UI
    return {
      symbol: this.selectedSymbol(),
      exchange: this.selectedExchange(),
      companyName: this.getCompanyName(),
      totalScore: 0,
      signal: 'Evaluating...',
      signalClass: 'badge-analyzing',
      pros: [],
      cons: [],
      redFlags: [],
      summaryText: 'Fetching sector-adaptive algorithmic evaluation from backend...',
      categoryScores: []
    } as any;
  }

  openEvaluationModal(): void {
    this.isEvaluationModalOpen.set(true);
  }

  closeEvaluationModal(): void {
    this.isEvaluationModalOpen.set(false);
  }

  openAboutModal(): void {
    this.isAboutModalOpen.set(true);
  }

  closeAboutModal(): void {
    this.isAboutModalOpen.set(false);
  }

  syncAllData(): void {
    this.isSyncingAll.set(true);
    this.fetchAllData(true);
    setTimeout(() => {
      this.isSyncingAll.set(false);
    }, 1500);
  }

  fetchQuarterlyResults(isRefresh: boolean): void {
    if (isRefresh) {
      this.isQuartersRefreshing.set(true);
    } else {
      this.quartersLoadingState.set('loading');
    }
    this.quartersErrorMessage.set('');

    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();

    this.quartersService.getQuarterlyResultsBySymbol(symbol, exchange, isRefresh).subscribe({
      next: (data) => {
        if (this.selectedSymbol() !== symbol) return;
        this.quartersResponse.set(data);
        this.isQuartersRefreshing.set(false);
        this.quartersLoadingState.set(data && data.history && data.history.length > 0 ? 'success' : 'empty');
      },
      error: (err) => {
        if (this.selectedSymbol() !== symbol) return;
        this.isQuartersRefreshing.set(false);
        this.quartersLoadingState.set('error');
        this.quartersErrorMessage.set(err.error?.detail || err.error?.message || 'Failed to retrieve quarterly results.');
      }
    });
  }

  fetchBalanceSheet(isRefresh: boolean): void {
    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();
    if (!symbol) return;

    if (isRefresh) {
      this.isAssetsRefreshing.set(true);
    } else {
      this.assetsLoadingState.set('loading');
    }
    this.assetsErrorMessage.set('');

    this.financialsService.getBalanceSheet(symbol, exchange, isRefresh).subscribe({
      next: (response) => {
        if (this.selectedSymbol() !== symbol) return;
        this.assetsResponse.set(response);
        this.assetsLoadingState.set('success');
        this.isAssetsRefreshing.set(false);
      },
      error: (err) => {
        if (this.selectedSymbol() !== symbol) return;
        this.assetsErrorMessage.set(err.error?.message || 'Could not fetch balance sheet.');
        this.assetsLoadingState.set('error');
        this.isAssetsRefreshing.set(false);
      }
    });
  }

  fetchShareholding(isRefresh: boolean): void {
    if (isRefresh) {
      this.isShareholdingRefreshing.set(true);
    } else {
      this.shareholdingLoadingState.set('loading');
    }
    this.shareholdingErrorMessage.set('');

    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();

    this.shareholdingService.getShareholdingBySymbol(symbol, exchange, isRefresh).subscribe({
      next: (data) => {
        if (this.selectedSymbol() !== symbol) return;
        this.shareholdingResponse.set(data);
        this.isShareholdingRefreshing.set(false);
        this.shareholdingLoadingState.set(data.currentPeriod ? 'success' : 'empty');
      },
      error: (err) => {
        if (this.selectedSymbol() !== symbol) return;
        this.isShareholdingRefreshing.set(false);
        this.shareholdingLoadingState.set('error');
        this.shareholdingErrorMessage.set(err.error?.detail || err.error?.message || 'Failed to retrieve shareholding data.');
      }
    });
  }

  fetchCashflow(isRefresh: boolean): void {
    if (isRefresh) {
      this.isCashflowRefreshing.set(true);
    } else {
      this.cashflowLoadingState.set('loading');
    }
    this.cashflowErrorMessage.set('');

    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();

    this.cashflowService.getCashflowBySymbol(symbol, exchange, isRefresh).subscribe({
      next: (data) => {
        if (this.selectedSymbol() !== symbol) return;
        console.log(`[Cashflow API Response for ${symbol}]`, data);
        this.cashflowResponse.set(data);
        if (data?.ratios?.currentPrice) {
          this.indexService.updateStockInCache(symbol, data.ratios.currentPrice, data.ratios.marketCap);
          if (this.watchlistService.isInWatchlist(symbol)) {
            this.watchlistService.setMeta(symbol, {
              price: Number(data.ratios.currentPrice),
              marketCap: data.ratios.marketCap ? Number(data.ratios.marketCap) : undefined
            });
          }
        }
        this.isCashflowRefreshing.set(false);
        this.isLoadingStock.set(false);
        this.cashflowLoadingState.set(data && data.summary ? 'success' : 'empty');
      },
      error: (err) => {
        if (this.selectedSymbol() !== symbol) return;
        this.isCashflowRefreshing.set(false);
        this.isLoadingStock.set(false);
        this.cashflowLoadingState.set('error');
        this.cashflowErrorMessage.set(err.error?.detail || err.error?.message || 'Failed to retrieve financial metrics.');
      }
    });
  }

  fetchNews(isRefresh: boolean): void {
    if (isRefresh) {
      this.isNewsRefreshing.set(true);
    } else {
      this.newsLoadingState.set('loading');
    }
    this.newsErrorMessage.set('');

    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();

    // Fetch top 5 freshest news items sorted strictly by time
    this.newsService.getNewsBySymbol(symbol, exchange, 5, 1, isRefresh).subscribe({
      next: (data) => {
        if (this.selectedSymbol() !== symbol) return;
        this.newsResponse.set(data);
        this.isNewsRefreshing.set(false);
        this.newsLoadingState.set(data.news && data.news.length > 0 ? 'success' : 'empty');
      },
      error: (err) => {
        if (this.selectedSymbol() !== symbol) return;
        this.isNewsRefreshing.set(false);
        this.newsLoadingState.set('error');
        this.newsErrorMessage.set(err.error?.detail || err.error?.message || 'Failed to retrieve market news.');
      }
    });
  }

  // Header & Info Helpers
  getCompanyName(): string {
    return this.companyOverview()?.companyName ||
      this.quartersResponse()?.companyName ||
      this.cashflowResponse()?.companyName ||
      this.shareholdingResponse()?.companyName ||
      this.newsResponse()?.companyName ||
      this.selectedSymbol();
  }

  getCompanyLogoUrl(): string | null {
    return this.companyOverview()?.logoUrl || null;
  }

  onLogoError(event: Event): void {
    this.isLogoFailed.set(true);
  }

  getSector(): string {
    const compName = this.getCompanyName();
    const overview = this.companyOverview();
    if (overview?.sector && overview.sector.trim()) {
      return overview.sector.trim();
    }
    if (overview?.industry && overview.industry.trim()) {
      return overview.industry.trim();
    }

    const ratiosSector = this.cashflowResponse()?.ratios?.sectorPeSector;
    if (ratiosSector && ratiosSector.trim() && ratiosSector.trim().toLowerCase() !== compName.toLowerCase()) {
      return ratiosSector.trim();
    }

    const stockInfo = this.availableStocks().find(s => s.symbol.toUpperCase() === this.selectedSymbol().toUpperCase());
    if (stockInfo?.industry && stockInfo.industry.trim() && stockInfo.industry.trim().toLowerCase() !== compName.toLowerCase()) {
      return stockInfo.industry.trim();
    }

    return 'Equities & Derivatives';
  }

  getFilteredNews(): StockNewsItem[] {
    return this.newsResponse()?.news || [];
  }

  // Visual Quality Bars
  getRoeProgress(val?: number | null): number {
    if (!val || isNaN(val) || val <= 0) return 0;
    return Math.min(100, Math.round((val / 30) * 100));
  }

  getRoceProgress(val?: number | null): number {
    if (!val || isNaN(val) || val <= 0) return 0;
    return Math.min(100, Math.round((val / 35) * 100));
  }

  getArrow(val?: number | null): string {
    if (val === null || val === undefined || val === 0) return '•';
    return val > 0 ? '▲' : '▼';
  }

  getChangeClass(val?: number | null): string {
    if (val === null || val === undefined || val === 0) return 'neutral';
    return val > 0 ? 'positive' : 'negative';
  }

  getInvertedChangeClass(val?: number | null): string {
    if (val === null || val === undefined || val === 0) return 'neutral';
    return val > 0 ? 'negative' : 'positive';
  }

  // Formatters (delegated to formatters.util.ts)
  formatCurrency(val?: number | null): string {
    return formatCurrency(val, 2, '—', 0);
  }

  formatMarketCap(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    if (val >= 10000000) {
      return formatCrores(val / 10000000, 0, '—', 0, ' Cr.');
    }
    return formatCrores(val, 0, '—', 0, ' Cr.');
  }

  getValueColorClass(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '';
    return val < 0 ? 'text-red' : 'text-green';
  }

  formatCrores(val?: number | null): string {
    return formatCrores(val, 0, '—', 0, ' Cr.');
  }

  formatRatio(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    return val.toFixed(1);
  }

  formatPercent(val?: number | null): string {
    return formatPercent(val, 1, true, '—');
  }

  formatPp(val?: number | null): string {
    return formatPercent(val, 2, true, '—');
  }

  formatEps(val?: number | null): string {
    return formatCurrency(val, 2, '—', 2);
  }

  formatNumber(val?: number | null, decimals: number = 0): string {
    return formatNumber(val, decimals);
  }

  formatAsOfDate(dateStr?: string | null): string {
    if (!dateStr) return '—';
    const date = new Date(dateStr);
    if (isNaN(date.getTime())) return dateStr;
    return date.toLocaleDateString('en-IN', { month: 'short', year: 'numeric' });
  }

  getLatestTotalAssets(): number | null {
    const bs = this.assetsResponse();
    if (!bs || !bs.lineItems || bs.lineItems.length === 0) return null;
    const item = bs.lineItems.find(i => (i.name && i.name.toLowerCase().includes('total assets')) || i.isTotal);
    if (!item || !item.values || item.values.length === 0) return null;
    return item.values[item.values.length - 1];
  }

  getLatestBorrowings(): number | null {
    const bs = this.assetsResponse();
    if (bs && bs.lineItems && bs.lineItems.length > 0) {
      const item = bs.lineItems.find(i => i.name && i.name.toLowerCase() === 'borrowings');
      if (item && item.values && item.values.length > 0) {
        const nonNullVals = item.values.filter((v): v is number => v !== null && v !== undefined && !isNaN(v));
        if (nonNullVals.length > 0) return nonNullVals[nonNullVals.length - 1];
      }
    }
    return null;
  }

  getDebtToEquity(): { ratio: number | null, label: string, statusClass: string } {
    const totalBorrowings = this.getLatestBorrowings();
    const totalEquity = this.cashflowResponse()?.summary?.totalEquity ||
      this.cashflowResponse()?.ratios?.totalEquity ||
      this.cashflowResponse()?.ratios?.equityCapital;

    if (totalBorrowings !== null && totalEquity && totalEquity > 0) {
      const deRatio = +(totalBorrowings / totalEquity).toFixed(2);
      let label = 'Low Debt';
      let statusClass = 'text-green';
      if (deRatio > 1.5) {
        label = 'High Debt';
        statusClass = 'text-red';
      } else if (deRatio > 0.8) {
        label = 'Moderate';
        statusClass = 'text-amber';
      }
      return { ratio: deRatio, label, statusClass };
    }
    return { ratio: null, label: 'Low / Debt Free', statusClass: 'text-green' };
  }

  getTotalShares(): number | null {
    const fromRatios = this.cashflowResponse()?.ratios?.totalShares;
    if (fromRatios && fromRatios > 0) return fromRatios;

    const eqCap = this.cashflowResponse()?.ratios?.equityCapital;
    const fv = this.cashflowResponse()?.ratios?.faceValue;
    if (eqCap && fv && fv > 0) {
      return eqCap / fv;
    }

    const mcap = this.cashflowResponse()?.ratios?.marketCap;
    const price = this.cashflowResponse()?.ratios?.currentPrice;
    if (mcap && price && price > 0) {
      return mcap / price;
    }
    return null;
  }

  formatShares(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val) || val <= 0) return '—';
    if (val >= 10000000) {
      return (val / 10000000).toLocaleString('en-IN', { maximumFractionDigits: 1, minimumFractionDigits: 1 }) + ' Cr';
    }
    if (val >= 100000) {
      return (val / 100000).toLocaleString('en-IN', { maximumFractionDigits: 1, minimumFractionDigits: 1 }) + ' L';
    }
    if (val < 10000) {
      return val.toLocaleString('en-IN', { maximumFractionDigits: 1, minimumFractionDigits: 1 }) + ' Cr';
    }
    return val.toLocaleString('en-IN');
  }

  getInterestCoverageRatio(): { icr: number | null, label: string, statusClass: string } {
    const op = this.cashflowResponse()?.summary?.operatingProfit || this.quartersResponse()?.summary?.operatingProfit;
    const interest = this.cashflowResponse()?.summary?.interest || this.quartersResponse()?.summary?.interest;
    if (op && interest && interest > 0) {
      const val = +(op / interest).toFixed(1);
      if (val >= 4) return { icr: val, label: 'High Cov.', statusClass: 'text-green' };
      if (val >= 2) return { icr: val, label: 'Adequate', statusClass: 'text-cyan' };
      if (val >= 1.2) return { icr: val, label: 'Moderate', statusClass: 'text-amber' };
      return { icr: val, label: 'Tight', statusClass: 'text-red' };
    }
    return { icr: null, label: 'Safe', statusClass: 'text-green' };
  }

  getTotalInstitutional(): string {
    const sh = this.shareholdingResponse()?.currentPeriod;
    if (!sh || (sh.fii === null && sh.dii === null)) return '—';
    const total = (sh.fii || 0) + (sh.dii || 0);
    return `${total.toFixed(2)}%`;
  }

  setChartType(type: 'candlestick' | 'area'): void {
    this.selectedChartType.set(type);
  }

  setFinancialTab(tab: 'cashflow' | 'balancesheet'): void {
    this.selectedFinancialTab.set(tab);
  }

  onBreakoutStateChange(hasBreakout: boolean): void {
    this.hasActiveBreakouts.set(hasBreakout);
  }

  openExternalUrl(url?: string): void {
    if (url && (url.startsWith('http://') || url.startsWith('https://'))) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  }

  openWebsite(url?: string): void {
    const targetUrl = url || this.getWebsiteUrl();
    if (targetUrl) {
      this.openExternalUrl(targetUrl);
    }
  }

  toggleAboutExpand(): void {
    this.isAboutExpanded.update(v => !v);
  }

  setNewsCategoryTab(tab: 'all' | 'market' | 'filings'): void {
    this.newsCategoryTab.set(tab);
  }

  // --- TOP STOCK HERO HELPERS ---

  getCompanyCapCategory(): string {
    const mcap = this.cashflowResponse()?.ratios?.marketCap;
    if (!mcap || isNaN(mcap) || mcap <= 0) return 'Large Cap';
    // If mcap >= 10,000,000, value is in raw INR; otherwise it is in Crores
    const mcapInCr = mcap >= 10000000 ? (mcap / 10000000) : mcap;
    if (mcapInCr >= 20000) return 'Large Cap';
    if (mcapInCr >= 5000) return 'Mid Cap';
    return 'Small Cap';
  }

  getCompanySectorTag(): string {
    const s = this.getSectorName();
    if (!s || s === 'Equities & Derivatives') return 'Equity';
    return s.length > 18 ? s.split('&')[0].trim() : s;
  }

  getCompanyIndustryTag(): string {
    const ind = this.getIndustryName();
    if (!ind || ind === 'Equities & Derivatives') return 'General';
    return ind.length > 20 ? ind.split('&')[0].trim() : ind;
  }

  getCurrentPriceFormatted(): string {
    const price = this.cashflowResponse()?.ratios?.currentPrice;
    if (price !== null && price !== undefined && !isNaN(price) && price > 0) {
      return this.formatCurrency(price);
    }
    return '—';
  }

  getPriceChangeDisplay(): { text: string, isPositive: boolean } {
    const current = this.cashflowResponse()?.ratios?.currentPrice;
    if (current !== null && current !== undefined && !isNaN(current) && current > 0) {
      return { text: 'Live Price', isPositive: true };
    }
    return { text: '—', isPositive: true };
  }

  getTimestampDisplay(): string {
    const asOf = this.cashflowResponse()?.ratios?.asOfDate;
    if (asOf) {
      const d = new Date(asOf);
      if (!isNaN(d.getTime())) {
        const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
        return `As on ${d.getDate()} ${monthNames[d.getMonth()]}, ${d.getFullYear()}`;
      }
    }
    return '—';
  }

  getMarketCapFormatted(): string {
    const mcap = this.cashflowResponse()?.ratios?.marketCap;
    if (mcap !== null && mcap !== undefined && !isNaN(mcap)) {
      return this.formatMarketCap(mcap);
    }
    return '—';
  }

  getPeRatioFormatted(): string {
    const pe = this.cashflowResponse()?.ratios?.peRatio;
    if (pe !== null && pe !== undefined && !isNaN(pe)) {
      return this.formatRatio(pe);
    }
    return '—';
  }

  getSectorPeFormatted(): string {
    const spe = this.cashflowResponse()?.ratios?.sectorPe;
    if (spe !== null && spe !== undefined && !isNaN(spe)) {
      return this.formatRatio(spe);
    }
    return '—';
  }

  get52WHighFormatted(): string {
    const h = this.cashflowResponse()?.ratios?.week52High;
    if (h !== null && h !== undefined && !isNaN(h)) {
      return this.formatCurrency(h);
    }
    return '—';
  }

  get52WLowFormatted(): string {
    const l = this.cashflowResponse()?.ratios?.week52Low;
    if (l !== null && l !== undefined && !isNaN(l)) {
      return this.formatCurrency(l);
    }
    return '—';
  }

  getEvaluationVerdict(): { text: string, score: number | string, class: string, progressWidth: number, isLoading: boolean } {
    const evalData = this.evaluationResponse();
    if (this.isEvaluationRefreshing() || !evalData || evalData.symbol !== this.selectedSymbol().toUpperCase()) {
      return {
        text: 'Evaluating...',
        score: '—',
        class: 'badge-analyzing',
        progressWidth: 0,
        isLoading: true
      };
    }

    const score = evalData.totalScore ?? 0;
    const text = evalData.signal || 'Neutral';
    return {
      text,
      score,
      class: evalData.signalClass || 'badge-good',
      progressWidth: Math.min(100, Math.max(0, score)),
      isLoading: false
    };
  }

  // --- ABOUT COMPANY HELPERS ---

  getAboutText(): string {
    const overview = this.companyOverview();
    if (overview?.about && overview.about.trim().length > 30) {
      return overview.about;
    }
    return `${this.getCompanyName()} is a listed enterprise on the exchange with strong operational fundamentals and dynamic financial disclosures.`;
  }

  getTruncatedAboutText(): string {
    const full = this.getAboutText();
    const limit = 220;
    if (full.length <= limit) {
      return full;
    }
    const truncated = full.substring(0, limit);
    const lastSpace = truncated.lastIndexOf(' ');
    return (lastSpace > 0 ? truncated.substring(0, lastSpace) : truncated).trim();
  }

  shouldShowReadMore(): boolean {
    return this.getAboutText().length > 180;
  }

  getSectorName(): string {
    const overview = this.companyOverview();
    if (overview?.sector && overview.sector.trim()) return overview.sector.trim();
    const ratiosSector = this.cashflowResponse()?.ratios?.sectorPeSector;
    if (ratiosSector && ratiosSector.trim()) return ratiosSector.trim();
    const stockInfo = this.availableStocks().find(s => s.symbol.toUpperCase() === this.selectedSymbol().toUpperCase());
    if (stockInfo?.industry && stockInfo.industry.trim()) return stockInfo.industry.trim();
    return this.getSector();
  }

  getIndustryName(): string {
    const overview = this.companyOverview();
    if (overview?.industry && overview.industry.trim()) return overview.industry.trim();
    const stockInfo = this.availableStocks().find(s => s.symbol.toUpperCase() === this.selectedSymbol().toUpperCase());
    if (stockInfo?.industry && stockInfo.industry.trim()) return stockInfo.industry.trim();
    const ratiosSector = this.cashflowResponse()?.ratios?.sectorPeSector;
    if (ratiosSector && ratiosSector.trim()) return ratiosSector.trim();
    return this.getSector();
  }

  getFoundedYear(): string {
    const overview = this.companyOverview();
    return overview?.foundedYear || '—';
  }

  getEmployeesCount(): string {
    const overview = this.companyOverview();
    if (overview?.employeesCount) {
      return this.formatNumber(overview.employeesCount, 0) + '+';
    }
    return '—';
  }

  getHeadquarters(): string {
    const overview = this.companyOverview();
    return overview?.headquarters || overview?.city || '—';
  }

  getWebsiteUrl(): string {
    const overview = this.companyOverview();
    return overview?.websiteUrl || 'https://www.nseindia.com';
  }



  // --- NEWS FEED HELPERS ---

  getDisplayNewsList(): StockNewsItem[] {
    const activeFilter = this.newsCategoryTab();
    const fetched = this.newsResponse()?.news || [];

    if (!fetched || fetched.length === 0) {
      return [];
    }

    if (activeFilter === 'market') {
      return fetched.filter(n =>
        (n.category || '').toLowerCase().includes('market') ||
        (n.category || '').toLowerCase().includes('news') ||
        !n.category
      );
    }

    if (activeFilter === 'filings') {
      return fetched.filter(n =>
        (n.category || '').toLowerCase().includes('filing') ||
        (n.category || '').toLowerCase().includes('announcement') ||
        (n.category || '').toLowerCase().includes('disclosure')
      );
    }

    return fetched;
  }

  // --- LATEST QUARTER HELPERS ---

  setLqTimeframe(tf: '1D' | '1W' | '1M' | '3M' | '1Y' | '5Y'): void {
    this.lqTimeframe.set(tf);
  }



  getBookValueFormatted(): string {
    const bv = this.cashflowResponse()?.ratios?.bookValue;
    if (bv !== null && bv !== undefined && !isNaN(bv)) {
      return '₹' + this.formatNumber(bv, 2);
    }
    return '—';
  }

  getFaceValueFormatted(): string {
    const fv = this.cashflowResponse()?.ratios?.faceValue;
    if (fv !== null && fv !== undefined && !isNaN(fv)) {
      return '₹' + this.formatNumber(fv, 2);
    }
    return '—';
  }

  getRoeFormatted(): string {
    const roe = this.cashflowResponse()?.ratios?.roe;
    if (roe !== null && roe !== undefined && !isNaN(roe)) {
      return this.formatNumber(roe, 1) + '%';
    }
    return '—';
  }

  getRoeYoY(): number | null | undefined {
    return this.cashflowResponse()?.ratios?.roeYoY;
  }

  getRoeTooltip(): string {
    const r = this.cashflowResponse()?.ratios;
    if (!r || r.roeYoY === null || r.roeYoY === undefined) return '';
    let tip = `YoY Growth: ${r.roeYoY >= 0 ? '+' : ''}${r.roeYoY.toFixed(1)}%`;
    if (r.roeChange !== null && r.roeChange !== undefined) tip += ` (${r.roeChange >= 0 ? '+' : ''}${r.roeChange.toFixed(2)}% pts)`;
    if (r.roePrevYear !== null && r.roePrevYear !== undefined) tip += ` vs Prev Year: ${r.roePrevYear.toFixed(1)}%`;
    return tip;
  }

  getRoceFormatted(): string {
    const roce = this.cashflowResponse()?.ratios?.roce;
    if (roce !== null && roce !== undefined && !isNaN(roce)) {
      return this.formatNumber(roce, 1) + '%';
    }
    return '—';
  }

  getRoceYoY(): number | null | undefined {
    return this.cashflowResponse()?.ratios?.roceYoY;
  }

  getRoceTooltip(): string {
    const r = this.cashflowResponse()?.ratios;
    if (!r || r.roceYoY === null || r.roceYoY === undefined) return '';
    let tip = `YoY Growth: ${r.roceYoY >= 0 ? '+' : ''}${r.roceYoY.toFixed(1)}%`;
    if (r.roceChange !== null && r.roceChange !== undefined) tip += ` (${r.roceChange >= 0 ? '+' : ''}${r.roceChange.toFixed(2)}% pts)`;
    if (r.rocePrevYear !== null && r.rocePrevYear !== undefined) tip += ` vs Prev Year: ${r.rocePrevYear.toFixed(1)}%`;
    return tip;
  }

  getSharesCountFormatted(): string {
    const shares = this.getTotalShares();
    if (shares !== null && shares !== undefined && !isNaN(shares) && shares > 0) {
      const inCr = shares >= 10_000_000 ? shares / 10_000_000 : shares;
      return `${this.formatNumber(inCr, 2)} Cr.`;
    }
    return '—';
  }

  getPbRatioFormatted(): string {
    const pb = this.cashflowResponse()?.ratios?.pbRatio;
    if (pb !== null && pb !== undefined && !isNaN(pb)) {
      return this.formatNumber(pb, 2);
    }
    return '—';
  }

  getDividendYieldFormatted(): string {
    const dy = this.cashflowResponse()?.ratios?.dividendYield;
    if (dy !== null && dy !== undefined && !isNaN(dy)) {
      return this.formatNumber(dy, 2) + '%';
    }
    return '—';
  }

  getLqMarginFormatted(): string {
    const opm = this.quartersResponse()?.summary?.opmPercentage;
    if (opm !== null && opm !== undefined && !isNaN(opm)) {
      return `${this.formatNumber(opm, 2)}%`;
    }
    return '—';
  }
}

