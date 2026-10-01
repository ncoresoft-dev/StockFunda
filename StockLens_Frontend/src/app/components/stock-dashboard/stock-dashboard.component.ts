import { Component, OnInit, OnDestroy, inject, signal, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription, of, timer } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, catchError } from 'rxjs/operators';
import { StockNewsService } from '../../services/stock-news.service';
import { StockShareholdingService } from '../../services/stock-shareholding.service';
import { StockCashflowService } from '../../services/stock-cashflow.service';
import { StockBalanceSheetService, BalanceSheetResponseDto } from '../../services/stock-balancesheet.service';
import { StockQuarterlyResultsService } from '../../services/stock-quarterly-results.service';
import { StockEvaluationService } from '../../services/stock-evaluation.service';
import { Stock, Company, StockNewsResponse, StockNewsItem, LoadingState } from '../../models/stock-news.model';
import { StockShareholdingResponse } from '../../models/stock-shareholding.model';
import { StockCashflowResponse } from '../../models/stock-cashflow.model';
import { StockQuarterlyResultsResponse } from '../../models/stock-quarterly-results.model';
import { StockHealthScoreResponse } from '../../models/stock-evaluation.model';
import { TimeAgoPipe } from '../../pipes/time-ago.pipe';
import { StockCandlestickChartComponent } from '../stock-candlestick-chart/stock-candlestick-chart.component';
import { StockTechnicalAnalysis } from '../stock-technical-analysis/stock-technical-analysis';
import { StockVolumeDeliveryCardComponent } from '../stock-volume-delivery-card/stock-volume-delivery-card.component';

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
    StockVolumeDeliveryCardComponent
  ],
  templateUrl: './stock-dashboard.component.html',
  styleUrl: './stock-dashboard.component.css'
})
export class StockDashboardComponent implements OnInit, OnDestroy {
  private readonly newsService = inject(StockNewsService);
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
  activeDetailModal = signal<DetailModalType>(null);
  isLoadingStock = signal<boolean>(false);
  deliveryAnalysisData = signal<any>(null);

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
    this.fetchCashflow(isRefresh);
    this.fetchBalanceSheet(isRefresh);
    this.fetchShareholding(isRefresh);
    this.fetchQuarterlyResults(isRefresh);
    this.fetchNews(isRefresh);
    this.fetchEvaluation(isRefresh);
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
    return this.quartersResponse()?.companyName ||
      this.cashflowResponse()?.companyName ||
      this.shareholdingResponse()?.companyName ||
      this.newsResponse()?.companyName ||
      this.selectedSymbol();
  }

  getSector(): string {
    const compName = this.getCompanyName();
    const ratiosSector = this.cashflowResponse()?.ratios?.sectorPeSector;
    if (ratiosSector && ratiosSector.trim() && ratiosSector.trim().toLowerCase() !== compName.toLowerCase()) {
      return ratiosSector.trim();
    }

    const stockInfo = this.availableStocks().find(s => s.symbol.toUpperCase() === this.selectedSymbol().toUpperCase());
    if (stockInfo?.industry && stockInfo.industry.trim() && stockInfo.industry.trim().toLowerCase() !== compName.toLowerCase()) {
      return stockInfo.industry.trim();
    }

    const staticMap: { [key: string]: string } = {
      'RELIANCE': 'Refineries & Petrochemicals',
      'TATAMOTORS': 'Automobiles',
      'TCS': 'IT Services & Consulting',
      'INFY': 'IT Services & Consulting',
      'HDFCBANK': 'Private Sector Banks',
      'ICICIBANK': 'Private Sector Banks',
      'SBIN': 'Public Sector Banks',
      'BHARTIARTL': 'Telecommunication Services',
      'ITC': 'Diversified FMCG & Tobacco',
      'LT': 'Engineering & Construction',
      'HINDUNILVR': 'FMCG - Household Products'
    };

    const sym = this.selectedSymbol().toUpperCase();
    if (staticMap[sym]) {
      return staticMap[sym];
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
    return `${sign}${val.toFixed(2)} pp`;
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
        const val = item.values[item.values.length - 1];
        if (val !== null && val !== undefined) return val;
      }
    }
    return null;
  }

  getLatestLongTermBorrowings(): number | null {
    const bs = this.assetsResponse();
    if (bs && bs.lineItems && bs.lineItems.length > 0) {
      const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('long term') || i.name.toLowerCase().includes('long-term')));
      if (item && item.values && item.values.length > 0) {
        const val = item.values[item.values.length - 1];
        if (val !== null && val !== undefined) return val;
      }
    }
    return null;
  }

  getLatestShortTermBorrowings(): number | null {
    const bs = this.assetsResponse();
    if (bs && bs.lineItems && bs.lineItems.length > 0) {
      const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('short term') || i.name.toLowerCase().includes('short-term')));
      if (item && item.values && item.values.length > 0) {
        const val = item.values[item.values.length - 1];
        if (val !== null && val !== undefined) return val;
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

  openExternalUrl(url?: string): void {
    if (url && (url.startsWith('http://') || url.startsWith('https://'))) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  }
}
