import { Component, OnInit, OnDestroy, inject, signal, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription, of, timer } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, catchError } from 'rxjs/operators';
import { StockNewsService } from '../../services/stock-news.service';
import { StockCompanyService } from '../../services/stock-company.service';
import { StockShareholdingService } from '../../services/stock-shareholding.service';
import { StockCashflowService } from '../../services/stock-cashflow.service';
import { StockBalanceSheetService, BalanceSheetResponseDto } from '../../services/stock-balancesheet.service';
import { StockQuarterlyResultsService } from '../../services/stock-quarterly-results.service';
import { StockEvaluationService } from '../../services/stock-evaluation.service';
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

export type NewsFilterTab = 'all' | 'filings' | 'announcements';
export type DetailModalType = null | 'ownership' | 'quarters' | 'profitability' | 'cashflow' | 'balancesheet' | 'valuation';

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
    StockDealsCardComponent
  ],
  templateUrl: './stock-dashboard.component.html',
  styleUrl: './stock-dashboard.component.css'
})
export class StockDashboardComponent implements OnInit, OnDestroy {
  private readonly newsService = inject(StockNewsService);
  private readonly companyService = inject(StockCompanyService);
  private readonly shareholdingService = inject(StockShareholdingService);
  private readonly cashflowService = inject(StockCashflowService);
  private readonly financialsService = inject(StockBalanceSheetService);
  private readonly quartersService = inject(StockQuarterlyResultsService);
  private readonly evaluationService = inject(StockEvaluationService);
  private readonly cd = inject(ChangeDetectorRef);

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
  searchQuery = signal<string>('');
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

  // Company Overview & Logo State
  companyOverview = signal<CompanyOverview | null>(null);
  isLogoFailed = signal<boolean>(false);

  // Global Refresh State
  isSyncingAll = signal<boolean>(false);
  technicalRefreshTrigger = signal<number>(0);

  // Typeahead search
  searchResults = signal<Company[]>([]);
  isSearching = signal<boolean>(false);
  private searchSubject = new Subject<string>();
  private searchSubscription?: Subscription;

  ngOnInit(): void {
    this.loadAvailableStocks();
    this.setupSearch();
    this.fetchAllData(false);
    this.startLivePricePolling();
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
    this.stopLivePricePolling();
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

  setupSearch(): void {
    this.searchSubscription = this.searchSubject.pipe(
      debounceTime(250),
      distinctUntilChanged(),
      switchMap((query) => {
        if (!query.trim() || query.trim().length < 2) {
          this.searchResults.set([]);
          this.isSearching.set(false);
          return of([]);
        }
        this.isSearching.set(true);
        return this.newsService.searchCompanies(query).pipe(
          catchError(() => of([]))
        );
      })
    ).subscribe((results) => {
      this.searchResults.set(results);
      this.isSearching.set(false);
    });
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
      this.searchQuery.set('');
      this.searchResults.set([]);
      return;
    }

    this.isLoadingStock.set(true);
    this.selectedSymbol.set(cleanSymbol);
    this.selectedExchange.set(cleanExchange);
    this.searchQuery.set('');
    this.searchResults.set([]);

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

    this.shareholdingLoadingState.set('loading');
    this.quartersLoadingState.set('loading');
    this.cashflowLoadingState.set('loading');
    this.assetsLoadingState.set('loading');
    this.newsLoadingState.set('loading');

    this.fetchAllData(false);
    this.startLivePricePolling();

    setTimeout(() => {
      this.isLoadingStock.set(false);
    }, 600);
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

  onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchQuery.set(value);
    this.searchSubject.next(value);
  }

  onSearchSubmit(): void {
    const query = this.searchQuery().trim();
    if (query) {
      this.selectStock(query, this.selectedExchange());
    }
  }

  selectSearchResult(company: Company): void {
    this.searchQuery.set('');
    this.searchResults.set([]);
    this.selectStock(company.symbol, this.selectedExchange());
  }

  fetchAllData(isRefresh: boolean = false): void {
    if (isRefresh) {
      this.technicalRefreshTrigger.update(v => v + 1);
    }
    this.fetchCompanyOverview(isRefresh);
    this.fetchCashflow(isRefresh);
    this.fetchBalanceSheet(isRefresh);
    this.fetchShareholding(isRefresh);
    this.fetchQuarterlyResults(isRefresh);
    this.fetchNews(isRefresh);
    this.fetchEvaluation(isRefresh);
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

  fetchEvaluation(isRefresh: boolean): void {
    const symbol = this.selectedSymbol();
    const exchange = this.selectedExchange();
    if (!symbol) return;

    if (isRefresh) {
      this.isEvaluationRefreshing.set(true);
    }

    this.evaluationService.getEvaluation(symbol, exchange, isRefresh).subscribe({
      next: (data) => {
        if (data && this.selectedSymbol() === symbol) {
          this.evaluationResponse.set(data);
        } else {
          this.recomputeEvaluation();
        }
        this.isEvaluationRefreshing.set(false);
      },
      error: () => {
        this.recomputeEvaluation();
        this.isEvaluationRefreshing.set(false);
      }
    });
  }

  recomputeEvaluation(): void {
    const evalData = this.evaluationService.computeClientEvaluation(
      this.selectedSymbol(),
      this.selectedExchange(),
      this.cashflowResponse(),
      this.quartersResponse(),
      this.shareholdingResponse(),
      this.assetsResponse()
    );
    this.evaluationResponse.set(evalData);
  }

  getEvaluation(): StockHealthScoreResponse {
    const existing = this.evaluationResponse();
    if (existing && existing.symbol === this.selectedSymbol().toUpperCase()) {
      return existing;
    }
    return this.evaluationService.computeClientEvaluation(
      this.selectedSymbol(),
      this.selectedExchange(),
      this.cashflowResponse(),
      this.quartersResponse(),
      this.shareholdingResponse(),
      this.assetsResponse()
    );
  }

  openEvaluationModal(): void {
    this.isEvaluationModalOpen.set(true);
  }

  closeEvaluationModal(): void {
    this.isEvaluationModalOpen.set(false);
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
        this.quartersResponse.set(data);
        this.isQuartersRefreshing.set(false);
        this.quartersLoadingState.set(data && data.history && data.history.length > 0 ? 'success' : 'empty');
      },
      error: (err) => {
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
        this.assetsResponse.set(response);
        this.assetsLoadingState.set('success');
        this.isAssetsRefreshing.set(false);
      },
      error: (err) => {
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
        this.shareholdingResponse.set(data);
        this.isShareholdingRefreshing.set(false);
        this.shareholdingLoadingState.set(data.currentPeriod ? 'success' : 'empty');
      },
      error: (err) => {
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
        console.log(`[Cashflow API Response for ${symbol}]`, data);
        this.cashflowResponse.set(data);
        this.isCashflowRefreshing.set(false);
        this.cashflowLoadingState.set(data && data.summary ? 'success' : 'empty');
      },
      error: (err) => {
        this.isCashflowRefreshing.set(false);
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

    this.newsService.getNewsBySymbol(symbol, exchange, 5, 1, isRefresh).subscribe({
      next: (data) => {
        this.newsResponse.set(data);
        this.isNewsRefreshing.set(false);
        this.newsLoadingState.set(data.news && data.news.length > 0 ? 'success' : 'empty');
      },
      error: (err) => {
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

  // Formatters
  formatCurrency(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    return '₹' + val.toLocaleString('en-IN', { maximumFractionDigits: 2, minimumFractionDigits: 0 });
  }

  formatMarketCap(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    if (val >= 10000000) {
      return '₹' + (val / 10000000).toLocaleString('en-IN', { maximumFractionDigits: 0 }) + ' Cr.';
    }
    return '₹' + val.toLocaleString('en-IN', { maximumFractionDigits: 0 }) + ' Cr.';
  }

  getValueColorClass(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '';
    return val < 0 ? 'text-red' : 'text-green';
  }

  formatCrores(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    const isNeg = val < 0;
    const absVal = Math.abs(val);
    return (isNeg ? '-₹' : '₹') + Math.round(absVal).toLocaleString('en-IN') + ' Cr.';
  }

  formatRatio(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    return val.toFixed(1);
  }

  formatPercent(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    return (val >= 0 ? '+' : '') + val.toFixed(1) + '%';
  }

  formatPp(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    const sign = val > 0 ? '+' : '';
    return `${sign}${val.toFixed(2)}%`;
  }

  formatEps(val?: number | null): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    return '₹' + val.toFixed(2);
  }

  formatNumber(val?: number | null, decimals: number = 0): string {
    if (val === null || val === undefined || isNaN(val)) return '—';
    return val.toLocaleString('en-IN', {
      maximumFractionDigits: decimals,
      minimumFractionDigits: decimals
    });
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

  getLatestFixedAssets(): number | null {
    const bs = this.assetsResponse();
    if (!bs || !bs.lineItems || bs.lineItems.length === 0) return null;
    const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('fixed assets') || i.name.toLowerCase().includes('property')));
    if (!item || !item.values || item.values.length === 0) return this.getLatestTotalAssets();
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

  getLatestLongTermBorrowings(): number | null {
    const bs = this.assetsResponse();
    if (bs && bs.lineItems && bs.lineItems.length > 0) {
      const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('long term') || i.name.toLowerCase().includes('long-term')));
      if (item && item.values && item.values.length > 0) {
        const nonNullVals = item.values.filter((v): v is number => v !== null && v !== undefined && !isNaN(v));
        if (nonNullVals.length > 0) return nonNullVals[nonNullVals.length - 1];
      }
    }
    return null;
  }

  getLongTermBorrowingsYoY(): number | null {
    const bs = this.assetsResponse();
    if (!bs || !bs.lineItems || bs.lineItems.length === 0) return null;
    const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('long term') || i.name.toLowerCase().includes('long-term')));
    if (!item || !item.values || item.values.length < 2) return null;

    const periods = bs.periods || [];
    const paired: { period: string, val: number }[] = [];
    for (let i = 0; i < item.values.length; i++) {
      const v = item.values[i];
      const p = periods[i] || `P${i}`;
      if (v !== null && v !== undefined && !isNaN(v)) {
        const existingIdx = paired.findIndex(x => x.period === p);
        if (existingIdx >= 0) {
          paired[existingIdx].val = v;
        } else {
          paired.push({ period: p, val: v });
        }
      }
    }

    if (paired.length >= 2) {
      const latest = paired[paired.length - 1].val;
      const prev = paired[paired.length - 2].val;
      if (prev !== 0) {
        return +(((latest - prev) / Math.abs(prev)) * 100).toFixed(1);
      } else if (latest > 0) {
        return 100;
      } else {
        return 0;
      }
    }

    return null;
  }

  getLatestShortTermBorrowings(): number | null {
    const bs = this.assetsResponse();
    if (bs && bs.lineItems && bs.lineItems.length > 0) {
      const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('short term') || i.name.toLowerCase().includes('short-term')));
      if (item && item.values && item.values.length > 0) {
        const nonNullVals = item.values.filter((v): v is number => v !== null && v !== undefined && !isNaN(v));
        if (nonNullVals.length > 0) return nonNullVals[nonNullVals.length - 1];
      }
    }
    return null;
  }

  getShortTermBorrowingsYoY(): number | null {
    const bs = this.assetsResponse();
    if (!bs || !bs.lineItems || bs.lineItems.length === 0) return null;
    const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('short term') || i.name.toLowerCase().includes('short-term')));
    if (!item || !item.values || item.values.length < 2) return null;

    const periods = bs.periods || [];
    const paired: { period: string, val: number }[] = [];
    for (let i = 0; i < item.values.length; i++) {
      const v = item.values[i];
      const p = periods[i] || `P${i}`;
      if (v !== null && v !== undefined && !isNaN(v)) {
        const existingIdx = paired.findIndex(x => x.period === p);
        if (existingIdx >= 0) {
          paired[existingIdx].val = v;
        } else {
          paired.push({ period: p, val: v });
        }
      }
    }

    if (paired.length >= 2) {
      const latest = paired[paired.length - 1].val;
      const prev = paired[paired.length - 2].val;
      if (prev !== 0) {
        return +(((latest - prev) / Math.abs(prev)) * 100).toFixed(1);
      } else if (latest > 0) {
        return 100;
      } else {
        return 0;
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

  getPegRatio(): number | null {
    const directPeg = this.cashflowResponse()?.ratios?.pegRatio ??
      this.cashflowResponse()?.summary?.ratios?.pegRatio;

    if (directPeg !== null && directPeg !== undefined && !isNaN(directPeg) && directPeg > 0) {
      return directPeg;
    }
    return null;
  }

  getCfoToOpRatio(): number | null {
    const fromSummary = this.cashflowResponse()?.summary?.cfoToOperatingProfitRatio;
    if (fromSummary !== null && fromSummary !== undefined && !isNaN(fromSummary)) {
      return fromSummary;
    }
    const cfo = this.cashflowResponse()?.summary?.operatingCashFlow;
    const op = this.cashflowResponse()?.summary?.operatingProfit;
    if (cfo !== null && cfo !== undefined && op !== null && op !== undefined && op !== 0) {
      return +(cfo / op).toFixed(2);
    }
    return null;
  }

  getTotalInstitutional(): string {
    const sh = this.shareholdingResponse()?.currentPeriod;
    if (!sh || (sh.fii === null && sh.dii === null)) return '—';
    const total = (sh.fii || 0) + (sh.dii || 0);
    return `${total.toFixed(2)}%`;
  }

  formatCfoToOp(value?: number | null): string {
    const val = value !== undefined ? value : this.getCfoToOpRatio();
    if (val === null || val === undefined || isNaN(val)) return '—';
    const pct = val > 5 ? val : val * 100;
    return `${Math.round(pct)}%`;
  }

  setChartType(type: 'candlestick' | 'area'): void {
    this.selectedChartType.set(type);
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

  getEvaluationVerdict(): { text: string, score: number, class: string, progressWidth: number } {
    const evalData = this.getEvaluation();
    const score = evalData.totalScore || 70;
    const text = evalData.signal || 'Good for Buy';
    return {
      text,
      score,
      class: evalData.signalClass || 'badge-success',
      progressWidth: Math.min(100, Math.max(0, score))
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
    if (!this.shouldShowReadMore() || this.isAboutExpanded()) {
      return full;
    }
    const limit = 1500;
    const truncated = full.substring(0, limit);
    const lastSpace = truncated.lastIndexOf(' ');
    return (lastSpace > 0 ? truncated.substring(0, lastSpace) : truncated).trim();
  }

  shouldShowReadMore(): boolean {
    return this.getAboutText().length > 1550;
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

  // --- OWNERSHIP BREAKDOWN HELPERS ---

  getOwnershipBreakdown(): {
    promoter: string;
    fii: string;
    dii: string;
    public: string;
    promoterPp: string;
    fiiPp: string;
    diiPp: string;
    publicPp: string;
    promoterIsPos: boolean;
    fiiIsPos: boolean;
    diiIsPos: boolean;
    publicIsPos: boolean;
    totalInstitutional: string;
    history: { period: string, promoter: string, fii: string, dii: string, public: string }[];
  } {
    const sh = this.shareholdingResponse();
    const curr = sh?.currentPeriod;

    const formatPct = (val?: number | null) => {
      if (val === null || val === undefined || isNaN(val)) return '—';
      return `${val.toFixed(2)}%`;
    };

    const promoter = formatPct(curr?.promoter);
    const fii = formatPct(curr?.fii);
    const dii = formatPct(curr?.dii);
    const pub = formatPct(curr?.public);

    // Deltas
    const formatDelta = (val?: number | null) => {
      if (val === null || val === undefined || isNaN(val)) {
        return { text: '—', isPos: true };
      }
      const isPos = val >= 0;
      const arrow = isPos ? '▲' : '▼';
      const sign = isPos ? '+' : '';
      return {
        text: `${arrow} ${sign}${Math.abs(val).toFixed(2)}%`,
        isPos
      };
    };

    const promDelta = formatDelta(sh?.change?.promoter);
    const fiiDelta = formatDelta(sh?.change?.fii);
    const diiDelta = formatDelta(sh?.change?.dii);
    const pubDelta = formatDelta(sh?.change?.public);

    const fiiNum = curr?.fii ?? 0;
    const diiNum = curr?.dii ?? 0;
    const totalInst = (curr?.fii !== null && curr?.fii !== undefined && curr?.dii !== null && curr?.dii !== undefined)
      ? (fiiNum + diiNum).toFixed(2) + '%'
      : '—';

    // History table rows from actual API response
    let historyRows: { period: string, promoter: string, fii: string, dii: string, public: string }[] = [];
    if (sh?.history && sh.history.length > 0) {
      historyRows = sh.history.slice(0, 4).map(h => ({
        period: h.period || '—',
        promoter: h.promoter !== null && h.promoter !== undefined ? `${h.promoter}%` : '—',
        fii: h.fii !== null && h.fii !== undefined ? `${h.fii}%` : '—',
        dii: h.dii !== null && h.dii !== undefined ? `${h.dii}%` : '—',
        public: h.public !== null && h.public !== undefined ? `${h.public}%` : '—'
      }));
    }

    return {
      promoter,
      fii,
      dii,
      public: pub,
      promoterPp: promDelta.text,
      fiiPp: fiiDelta.text,
      diiPp: diiDelta.text,
      publicPp: pubDelta.text,
      promoterIsPos: promDelta.isPos,
      fiiIsPos: fiiDelta.isPos,
      diiIsPos: diiDelta.isPos,
      publicIsPos: pubDelta.isPos,
      totalInstitutional: totalInst,
      history: historyRows
    };
  }

  // --- FINANCIALS CARD HELPERS ---

  getQuarterlyFinancialsData(): {
    sales: string;
    salesQoQ: { text: string, isPositive: boolean };
    salesYoY: { text: string, isPositive: boolean };
    netProfit: string;
    netProfitQoQ: { text: string, isPositive: boolean };
    netProfitYoY: { text: string, isPositive: boolean };
    operatingProfit: string;
    opMargin: string;
    eps: string;
    epsQoQ: { text: string, isPositive: boolean };
    interest: string;
    interestQoQ: { text: string, isPositive: boolean };
    tax: string;
    taxQoQ: { text: string, isPositive: boolean };
    depreciation: string;
    depreciationQoQ: { text: string, isPositive: boolean };
    depreciationType: string;
  } {
    const q = this.quartersResponse();
    const sum = q?.summary;

    const formatGrowth = (val?: number | null, prefix: string = 'QoQ') => {
      if (val === null || val === undefined || isNaN(val)) {
        return { text: `${prefix}: —`, isPositive: true };
      }
      const isPositive = val >= 0;
      const sign = isPositive ? '+' : '';
      return {
        text: `${prefix}: ${sign}${val.toFixed(1)}%`,
        isPositive
      };
    };

    const sales = sum?.sales !== null && sum?.sales !== undefined ? this.formatCrores(sum.sales) : '—';
    const salesQoQ = formatGrowth(q?.qoQGrowth?.salesGrowthPercent, 'QoQ');
    const salesYoY = formatGrowth(q?.yoYGrowth?.salesGrowthPercent, 'YoY');

    const netProfit = sum?.netProfit !== null && sum?.netProfit !== undefined ? this.formatCrores(sum.netProfit) : '—';
    const netProfitQoQ = formatGrowth(q?.qoQGrowth?.netProfitGrowthPercent, 'QoQ');
    const netProfitYoY = formatGrowth(q?.yoYGrowth?.netProfitGrowthPercent, 'YoY');

    const operatingProfit = sum?.operatingProfit !== null && sum?.operatingProfit !== undefined ? this.formatCrores(sum.operatingProfit) : '—';
    const opMargin = sum?.opmPercentage !== null && sum?.opmPercentage !== undefined ? `Margin: ${sum.opmPercentage.toFixed(1)}%` : 'Margin: —';

    const eps = sum?.eps !== null && sum?.eps !== undefined ? `₹${sum.eps.toFixed(2)}` : '—';
    const epsQoQ = formatGrowth(q?.qoQGrowth?.epsGrowthPercent, 'QoQ');

    const interest = sum?.interest !== null && sum?.interest !== undefined ? this.formatCrores(sum.interest) : '—';
    const interestQoQ = formatGrowth(q?.qoQGrowth?.interestGrowthPercent, 'QoQ');

    const tax = sum?.tax !== null && sum?.tax !== undefined ? this.formatCrores(sum.tax) : '—';
    const taxQoQ = formatGrowth(q?.qoQGrowth?.taxGrowthPercent, 'QoQ');

    const depreciation = sum?.depreciation !== null && sum?.depreciation !== undefined ? this.formatCrores(sum.depreciation) : '—';
    const depreciationQoQ = formatGrowth(q?.qoQGrowth?.depreciationGrowthPercent, 'QoQ');
    const depreciationType = depreciation !== '—' ? 'Non-Cash' : '—';

    return {
      sales,
      salesQoQ,
      salesYoY,
      netProfit,
      netProfitQoQ,
      netProfitYoY,
      operatingProfit,
      opMargin,
      eps,
      epsQoQ,
      interest,
      interestQoQ,
      tax,
      taxQoQ,
      depreciation,
      depreciationQoQ,
      depreciationType
    };
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

  getRoceFormatted(): string {
    const roce = this.cashflowResponse()?.ratios?.roce;
    if (roce !== null && roce !== undefined && !isNaN(roce)) {
      return this.formatNumber(roce, 1) + '%';
    }
    return '—';
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

