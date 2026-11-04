import { Component, OnInit, inject, signal, computed, ElementRef, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HighchartsChartComponent } from 'highcharts-angular';
import * as Highcharts from 'highcharts/highstock';
import HighchartsMore from 'highcharts/highcharts-more';

if (typeof HighchartsMore === 'function') {
  (HighchartsMore as any)(Highcharts);
} else if (HighchartsMore && typeof (HighchartsMore as any).default === 'function') {
  (HighchartsMore as any).default(Highcharts);
}
import { HeaderComponent } from '../header/header.component';
import { StockComparisonService } from '../../services/stock-comparison.service';
import { StockNewsService } from '../../services/stock-news.service';
import { Company } from '../../models/stock-news.model';
import { StockComparisonResponse } from '../../models/stock-comparison.model';
import { formatNumber, formatPercent } from '../../utils/formatters.util';

@Component({
  selector: 'app-stock-comparison',
  standalone: true,
  imports: [CommonModule, FormsModule, HighchartsChartComponent, HeaderComponent],
  templateUrl: './stock-comparison.component.html',
  styleUrls: ['./stock-comparison.component.css']
})
export class StockComparisonComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly comparisonService = inject(StockComparisonService);
  private readonly newsService = inject(StockNewsService);
  private readonly elementRef = inject(ElementRef);

  // Core comparison signals (Default empty so API only runs after both are selected)
  stock1 = signal<string>('');
  stock2 = signal<string>('');
  exchange = signal<string>('NSE');
  selectedPeriod = signal<string>('1yr');

  isLoading = signal<boolean>(false);
  hasError = signal<boolean>(false);
  errorMessage = signal<string>('');
  comparisonData = signal<StockComparisonResponse | null>(null);

  // Validation computed: Both stocks must be non-empty and distinct
  canCompare = computed(() => {
    const s1 = this.stock1().trim().toUpperCase();
    const s2 = this.stock2().trim().toUpperCase();
    return s1.length > 0 && s2.length > 0 && s1 !== s2;
  });

  // Period options
  readonly periods = [
    { key: '1mo', label: '1M' },
    { key: '3mo', label: '3M' },
    { key: '6mo', label: '6M' },
    { key: '1yr', label: '1Y' },
    { key: '3yr', label: '3Y' },
    { key: '5yr', label: '5Y' }
  ];

  // Stock 1 Autocomplete state
  searchQuery1 = signal<string>('');
  searchResults1 = signal<Company[]>([]);
  isSearching1 = signal<boolean>(false);
  isDropdownOpen1 = signal<boolean>(false);

  // Stock 2 Autocomplete state
  searchQuery2 = signal<string>('');
  searchResults2 = signal<Company[]>([]);
  isSearching2 = signal<boolean>(false);
  isDropdownOpen2 = signal<boolean>(false);

  // Highcharts configuration
  Highcharts: typeof Highcharts = Highcharts;
  chartOptions: Highcharts.Options = {};
  updateFlag = false;

  // Comparison Pattern Charts
  radarChartOptions: Highcharts.Options = {};
  valuationChartOptions: Highcharts.Options = {};
  profitabilityChartOptions: Highcharts.Options = {};
  growthChartOptions: Highcharts.Options = {};
  shareholdingChartOptions: Highcharts.Options = {};
  prosConsChartOptions: Highcharts.Options = {};
  chartsUpdateFlag = false;

  // Active tab in the Fundamental Dimension Hub
  activeChartTab = signal<'valuation' | 'profitability' | 'growth' | 'shareholding' | 'pros-cons'>('valuation');

  private lastComparisonKey = '';

  ngOnInit(): void {
    // Listen to query parameters (Single source of truth for triggering comparison)
    this.route.queryParams.subscribe((params) => {
      const s1 = (params['stock1'] || '').trim().toUpperCase();
      const s2 = (params['stock2'] || '').trim().toUpperCase();
      const ex = params['exchange'] || 'NSE';
      const per = params['period'] || '1yr';

      this.stock1.set(s1);
      this.stock2.set(s2);
      this.exchange.set(ex);
      this.selectedPeriod.set(per);
      this.searchQuery1.set(s1);
      this.searchQuery2.set(s2);

      // ONLY execute API call if BOTH Stock A and Stock B are explicitly provided and different!
      if (s1 && s2 && s1 !== s2) {
        this.fetchComparison(s1, s2, ex, per);
      } else {
        this.comparisonData.set(null);
        this.isLoading.set(false);
        this.lastComparisonKey = '';
      }
    });
  }

  fetchComparison(s1: string, s2: string, ex: string, per: string, refresh = false): void {
    const cleanA = (s1 || '').trim().toUpperCase();
    const cleanB = (s2 || '').trim().toUpperCase();

    // STRICT CHECK: Both stocks must be selected before calling the backend API!
    if (!cleanA || !cleanB) {
      this.isLoading.set(false);
      this.comparisonData.set(null);
      this.lastComparisonKey = '';
      return;
    }

    if (cleanA === cleanB) {
      this.isLoading.set(false);
      this.hasError.set(true);
      this.errorMessage.set('Please select two different stocks to compare.');
      this.comparisonData.set(null);
      this.lastComparisonKey = '';
      return;
    }

    // Deduplication key: Prevent double identical requests
    const comparisonKey = `${cleanA}_${cleanB}_${ex}_${per}`;
    if (!refresh && this.lastComparisonKey === comparisonKey && (this.comparisonData() || this.isLoading())) {
      return;
    }
    this.lastComparisonKey = comparisonKey;

    this.isLoading.set(true);
    this.hasError.set(false);
    this.errorMessage.set('');

    this.comparisonService.compareStocks(cleanA, cleanB, ex, per, refresh).subscribe({
      next: (data) => {
        this.comparisonData.set(data);
        this.isLoading.set(false);
        this.buildAllCharts(data);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.hasError.set(true);
        this.errorMessage.set(err?.error?.message || 'Failed to compare the selected stocks. Please check the symbols.');
        this.lastComparisonKey = '';
      }
    });
  }

  updateUrl(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        stock1: this.stock1(),
        stock2: this.stock2(),
        exchange: this.exchange(),
        period: this.selectedPeriod()
      },
      queryParamsHandling: 'merge'
    });
  }

  onCompareClick(): void {
    if (!this.canCompare()) return;
    this.fetchComparison(this.stock1(), this.stock2(), this.exchange(), this.selectedPeriod(), true);
    this.updateUrl();
  }

  setPeriod(periodKey: string): void {
    if (this.selectedPeriod() === periodKey) return;
    this.selectedPeriod.set(periodKey);
    if (this.comparisonData() && this.canCompare()) {
      this.fetchComparison(this.stock1(), this.stock2(), this.exchange(), periodKey);
      this.updateUrl();
    }
  }

  swapStocks(): void {
    if (!this.stock1() && !this.stock2()) return;
    const temp = this.stock1();
    this.stock1.set(this.stock2());
    this.stock2.set(temp);
    this.searchQuery1.set(this.stock1());
    this.searchQuery2.set(this.stock2());
  }

  navigateToStock(symbol: string): void {
    if (!symbol) return;
    this.router.navigate(['/stock', symbol]);
  }

  // --- Autocomplete for Stock 1 ---
  onSearchInput1(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchQuery1.set(val);
    if (!val || val.trim().length === 0) {
      this.searchResults1.set([]);
      this.isDropdownOpen1.set(false);
      this.stock1.set('');
      this.comparisonData.set(null);
      return;
    }
    this.isDropdownOpen1.set(true);
    this.isSearching1.set(true);
    this.newsService.searchCompanies(val.trim()).subscribe({
      next: (res) => {
        this.searchResults1.set(res || []);
        this.isSearching1.set(false);
      },
      error: () => this.isSearching1.set(false)
    });
  }

  selectStock1(company: Company): void {
    const clean = company.symbol.trim().toUpperCase();
    this.stock1.set(clean);
    this.searchQuery1.set(clean);
    this.isDropdownOpen1.set(false);
  }

  onSearchSubmit1(): void {
    const val = this.searchQuery1().trim().toUpperCase();
    if (val) {
      this.stock1.set(val);
      this.isDropdownOpen1.set(false);
    }
  }

  // --- Autocomplete for Stock 2 ---
  onSearchInput2(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchQuery2.set(val);
    if (!val || val.trim().length === 0) {
      this.searchResults2.set([]);
      this.isDropdownOpen2.set(false);
      this.stock2.set('');
      this.comparisonData.set(null);
      this.lastComparisonKey = '';
      return;
    }
    this.isDropdownOpen2.set(true);
    this.isSearching2.set(true);
    this.newsService.searchCompanies(val.trim()).subscribe({
      next: (res) => {
        this.searchResults2.set(res || []);
        this.isSearching2.set(false);
      },
      error: () => this.isSearching2.set(false)
    });
  }

  selectStock2(company: Company): void {
    const clean = company.symbol.trim().toUpperCase();
    this.stock2.set(clean);
    this.searchQuery2.set(clean);
    this.isDropdownOpen2.set(false);
  }

  onSearchSubmit2(): void {
    const val = this.searchQuery2().trim().toUpperCase();
    if (val) {
      this.stock2.set(val);
      this.isDropdownOpen2.set(false);
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target)) {
      this.isDropdownOpen1.set(false);
      this.isDropdownOpen2.set(false);
    }
  }

  // --- Chart Builders & Visual Pattern Suite ---
  buildAllCharts(data: StockComparisonResponse): void {
    this.buildChartOptions(data);
    this.buildRadarChart(data);
    this.buildValuationChart(data);
    this.buildProfitabilityChart(data);
    this.buildGrowthChart(data);
    this.buildShareholdingChart(data);
    this.buildProsConsChart(data);
    this.updateFlag = true;
    this.chartsUpdateFlag = true;
  }

  setActiveChartTab(tab: 'valuation' | 'profitability' | 'growth' | 'shareholding' | 'pros-cons'): void {
    this.activeChartTab.set(tab);
    setTimeout(() => {
      this.chartsUpdateFlag = true;
    }, 50);
  }

  // Helper for computing 5-pillar radar scores (0 to 100)
  getPillarScore(category: 'health' | 'valuation' | 'profitability' | 'growth' | 'ownership', isStockA: boolean): number {
    const data = this.comparisonData();
    if (!data) return 50;
    const a = data.stockA;
    const b = data.stockB;
    const target = isStockA ? a : b;
    const rival = isStockA ? b : a;

    if (category === 'health') {
      return Math.min(100, Math.max(0, Math.round(target.healthScore || 0)));
    }

    if (category === 'valuation') {
      // Lower PE and PB is better
      const peT = target.peRatio ?? 0;
      const peR = rival.peRatio ?? 0;
      let peScore = 50;
      if (peT > 0 && peR > 0) {
        if (peT < peR) {
          peScore = 50 + Math.min(45, Math.round(((peR - peT) / peR) * 60));
        } else if (peT > peR) {
          peScore = 50 - Math.min(40, Math.round(((peT - peR) / peT) * 60));
        }
      } else if (peT > 0 && peT < 25) {
        peScore = 70;
      }

      const pbT = target.pbRatio ?? 0;
      const pbR = rival.pbRatio ?? 0;
      let pbScore = 50;
      if (pbT > 0 && pbR > 0) {
        if (pbT < pbR) {
          pbScore = 50 + Math.min(45, Math.round(((pbR - pbT) / pbR) * 60));
        } else if (pbT > pbR) {
          pbScore = 50 - Math.min(40, Math.round(((pbT - pbR) / pbT) * 60));
        }
      }

      return Math.min(100, Math.max(10, Math.round((peScore + pbScore) / 2)));
    }

    if (category === 'profitability') {
      const roe = target.roe ?? 0;
      const roce = target.roce ?? 0;
      const opm = target.operatingMargin ?? 0;
      const rawAvg = roe * 0.4 + roce * 0.4 + opm * 0.2;
      return Math.min(100, Math.max(10, Math.round(rawAvg * 2.5)));
    }

    if (category === 'growth') {
      const sYoY = target.salesQuarterlyGrowthYoY ?? 0;
      const pYoY = target.profitQuarterlyGrowthYoY ?? 0;
      const sQoQ = target.salesQuarterlyGrowthQoQ ?? 0;
      const pQoQ = target.profitQuarterlyGrowthQoQ ?? 0;
      const avg = (sYoY + pYoY + sQoQ + pQoQ) / 4;
      return Math.min(100, Math.max(10, Math.round(50 + avg * 1.5)));
    }

    if (category === 'ownership') {
      const fii = target.fiiHolding ?? 0;
      const dii = target.diiHolding ?? 0;
      const inst = fii + dii;
      return Math.min(100, Math.max(10, Math.round(inst)));
    }

    return 50;
  }

  getPillarWinner(category: 'health' | 'valuation' | 'profitability' | 'growth' | 'ownership'): 'A' | 'B' | 'TIE' {
    const scoreA = this.getPillarScore(category, true);
    const scoreB = this.getPillarScore(category, false);
    if (Math.abs(scoreA - scoreB) < 2) return 'TIE';
    return scoreA > scoreB ? 'A' : 'B';
  }

  getMcapBarWidth(mcapA: number | null | undefined, mcapB: number | null | undefined, isStockA: boolean): number {
    const mA = mcapA == null ? 0 : mcapA;
    const mB = mcapB == null ? 0 : mcapB;
    const maxM = Math.max(mA, mB);
    if (maxM <= 0) return 50;
    const target = isStockA ? mA : mB;
    return Math.max(10, Math.min(100, Math.round((target / maxM) * 100)));
  }

  // 1. Performance Spline Chart
  private buildChartOptions(data: StockComparisonResponse): void {
    const symA = data.stockA.symbol;
    const symB = data.stockB.symbol;

    const seriesA: [number, number][] = [];
    const seriesB: [number, number][] = [];

    data.normalizedHistory.forEach((pt) => {
      const timestamp = new Date(pt.date).getTime();
      if (!isNaN(timestamp)) {
        if (pt.stockAPercentReturn != null) {
          seriesA.push([timestamp, pt.stockAPercentReturn]);
        }
        if (pt.stockBPercentReturn != null) {
          seriesB.push([timestamp, pt.stockBPercentReturn]);
        }
      }
    });

    this.chartOptions = {
      chart: {
        type: 'spline',
        backgroundColor: 'transparent',
        spacing: [16, 12, 16, 12]
      },
      title: {
        text: undefined
      },
      credits: {
        enabled: false
      },
      xAxis: {
        type: 'datetime',
        lineColor: '#e2e8f0',
        tickColor: '#e2e8f0',
        labels: {
          style: { color: '#64748b', fontSize: '11px', fontWeight: '500' }
        }
      },
      yAxis: {
        title: {
          text: 'Return (%)',
          style: { color: '#64748b', fontSize: '11px', fontWeight: '600' }
        },
        gridLineColor: '#f1f5f9',
        labels: {
          format: '{value}%',
          style: { color: '#64748b', fontSize: '11px' }
        },
        plotLines: [
          {
            value: 0,
            color: '#94a3b8',
            dashStyle: 'Dash',
            width: 1.5,
            zIndex: 3
          }
        ]
      },
      tooltip: {
        shared: true,
        backgroundColor: '#ffffff',
        borderColor: '#e2e8f0',
        borderRadius: 8,
        style: { color: '#0f172a', fontSize: '12px' },
        shadow: true,
        xDateFormat: '%b %d, %Y',
        valueDecimals: 2,
        valueSuffix: '%'
      },
      legend: {
        align: 'right',
        verticalAlign: 'top',
        itemStyle: { color: '#334155', fontSize: '12px', fontWeight: '600' },
        itemHoverStyle: { color: '#0f172a' }
      },
      plotOptions: {
        spline: {
          lineWidth: 2.4,
          marker: { enabled: false }
        }
      },
      series: [
        {
          name: symA,
          type: 'spline',
          data: seriesA,
          color: '#0284c7' // Bright Blue
        },
        {
          name: symB,
          type: 'spline',
          data: seriesB,
          color: '#e11d48' // Vivid Rose
        }
      ]
    };
  }

  // 2. Spider / Radar 5-Pillar Battleground Chart
  private buildRadarChart(data: StockComparisonResponse): void {
    const symA = data.stockA.symbol;
    const symB = data.stockB.symbol;

    const dataA = [
      this.getPillarScore('health', true),
      this.getPillarScore('valuation', true),
      this.getPillarScore('profitability', true),
      this.getPillarScore('growth', true),
      this.getPillarScore('ownership', true)
    ];

    const dataB = [
      this.getPillarScore('health', false),
      this.getPillarScore('valuation', false),
      this.getPillarScore('profitability', false),
      this.getPillarScore('growth', false),
      this.getPillarScore('ownership', false)
    ];

    this.radarChartOptions = {
      chart: {
        polar: true,
        type: 'area',
        backgroundColor: 'transparent',
        spacing: [16, 12, 16, 12]
      },
      title: { text: undefined },
      credits: { enabled: false },
      pane: { size: '80%' },
      xAxis: {
        categories: [
          'Health Score',
          'Valuation Appeal',
          'Profitability',
          'Quarterly Growth',
          'Institutional Trust'
        ],
        tickmarkPlacement: 'on',
        lineWidth: 0,
        labels: {
          style: { color: '#334155', fontSize: '11px', fontWeight: '700' }
        }
      },
      yAxis: {
        gridLineInterpolation: 'polygon',
        lineWidth: 0,
        min: 0,
        max: 100,
        labels: { enabled: false },
        gridLineColor: '#e2e8f0'
      },
      tooltip: {
        shared: true,
        backgroundColor: '#ffffff',
        borderColor: '#e2e8f0',
        borderRadius: 8,
        style: { color: '#0f172a', fontSize: '12px' },
        valueSuffix: ' / 100'
      },
      legend: {
        align: 'center',
        verticalAlign: 'bottom',
        itemStyle: { color: '#334155', fontSize: '12px', fontWeight: '600' }
      },
      series: [
        {
          name: symA,
          type: 'area',
          data: dataA,
          color: '#0284c7',
          fillColor: 'rgba(2, 132, 199, 0.22)',
          lineWidth: 2,
          marker: { radius: 4 }
        },
        {
          name: symB,
          type: 'area',
          data: dataB,
          color: '#e11d48',
          fillColor: 'rgba(225, 29, 72, 0.22)',
          lineWidth: 2,
          marker: { radius: 4 }
        }
      ]
    };
  }

  // 3. Valuation & Market Multiples Chart
  private buildValuationChart(data: StockComparisonResponse): void {
    const symA = data.stockA.symbol;
    const symB = data.stockB.symbol;

    this.valuationChartOptions = {
      chart: {
        type: 'column',
        backgroundColor: 'transparent',
        spacing: [16, 12, 16, 12]
      },
      title: { text: undefined },
      credits: { enabled: false },
      xAxis: {
        categories: ['P/E Multiple', 'P/B Multiple', 'Div Yield (%)'],
        labels: {
          style: { color: '#475569', fontSize: '11px', fontWeight: '600' }
        },
        lineColor: '#e2e8f0'
      },
      yAxis: {
        title: { text: 'Multiple / Ratio', style: { color: '#64748b', fontSize: '11px' } },
        gridLineColor: '#f1f5f9',
        labels: { style: { color: '#64748b', fontSize: '11px' } }
      },
      tooltip: {
        shared: true,
        backgroundColor: '#ffffff',
        borderColor: '#e2e8f0',
        borderRadius: 8,
        style: { color: '#0f172a', fontSize: '12px' },
        formatter: function() {
          let s = `<div style="font-weight:700;margin-bottom:4px;">${this.x}</div>`;
          (this.points || []).forEach((p: any) => {
            const isDiv = p.x === 'Div Yield (%)';
            const suffix = isDiv ? '%' : 'x';
            s += `<div><span style="color:${p.color};">●</span> ${p.series.name}: <b>${p.y != null ? p.y.toFixed(2) : '-'}${suffix}</b></div>`;
          });
          return s;
        }
      },
      legend: {
        align: 'right',
        verticalAlign: 'top',
        itemStyle: { color: '#334155', fontSize: '12px', fontWeight: '600' }
      },
      plotOptions: {
        column: {
          borderRadius: 6,
          borderWidth: 0,
          dataLabels: {
            enabled: true,
            formatter: function() {
              if (this.y == null) return '';
              return (this as any).index === 2 ? `${this.y.toFixed(1)}%` : `${this.y.toFixed(1)}x`;
            },
            style: { fontSize: '10px', fontWeight: '700', color: '#1e293b' }
          }
        }
      },
      series: [
        {
          name: symA,
          type: 'column',
          data: [
            data.stockA.peRatio ?? 0,
            data.stockA.pbRatio ?? 0,
            data.stockA.dividendYield ?? 0
          ],
          color: '#0284c7'
        },
        {
          name: symB,
          type: 'column',
          data: [
            data.stockB.peRatio ?? 0,
            data.stockB.pbRatio ?? 0,
            data.stockB.dividendYield ?? 0
          ],
          color: '#e11d48'
        }
      ]
    };
  }

  // 4. Profitability & Margins Chart
  private buildProfitabilityChart(data: StockComparisonResponse): void {
    const symA = data.stockA.symbol;
    const symB = data.stockB.symbol;

    this.profitabilityChartOptions = {
      chart: {
        type: 'column',
        backgroundColor: 'transparent',
        spacing: [16, 12, 16, 12]
      },
      title: { text: undefined },
      credits: { enabled: false },
      xAxis: {
        categories: ['Return on Equity (ROE)', 'ROCE Capital Return', 'Operating Margin (OPM)'],
        labels: {
          style: { color: '#475569', fontSize: '11px', fontWeight: '600' }
        },
        lineColor: '#e2e8f0'
      },
      yAxis: {
        title: { text: 'Percentage (%)', style: { color: '#64748b', fontSize: '11px' } },
        gridLineColor: '#f1f5f9',
        labels: { format: '{value}%', style: { color: '#64748b', fontSize: '11px' } }
      },
      tooltip: {
        shared: true,
        backgroundColor: '#ffffff',
        borderColor: '#e2e8f0',
        borderRadius: 8,
        valueSuffix: '%',
        valueDecimals: 2
      },
      legend: {
        align: 'right',
        verticalAlign: 'top',
        itemStyle: { color: '#334155', fontSize: '12px', fontWeight: '600' }
      },
      plotOptions: {
        column: {
          borderRadius: 6,
          borderWidth: 0,
          dataLabels: {
            enabled: true,
            format: '{y:.1f}%',
            style: { fontSize: '10px', fontWeight: '700', color: '#1e293b' }
          }
        }
      },
      series: [
        {
          name: symA,
          type: 'column',
          data: [
            data.stockA.roe ?? 0,
            data.stockA.roce ?? 0,
            data.stockA.operatingMargin ?? 0
          ],
          color: '#0284c7'
        },
        {
          name: symB,
          type: 'column',
          data: [
            data.stockB.roe ?? 0,
            data.stockB.roce ?? 0,
            data.stockB.operatingMargin ?? 0
          ],
          color: '#e11d48'
        }
      ]
    };
  }

  // 5. Quarterly Growth Dynamics Chart
  private buildGrowthChart(data: StockComparisonResponse): void {
    const symA = data.stockA.symbol;
    const symB = data.stockB.symbol;

    this.growthChartOptions = {
      chart: {
        type: 'column',
        backgroundColor: 'transparent',
        spacing: [16, 12, 16, 12]
      },
      title: { text: undefined },
      credits: { enabled: false },
      xAxis: {
        categories: ['Revenue YoY', 'Revenue QoQ', 'Net Profit YoY', 'Net Profit QoQ'],
        labels: {
          style: { color: '#475569', fontSize: '11px', fontWeight: '600' }
        },
        lineColor: '#e2e8f0'
      },
      yAxis: {
        title: { text: 'Growth Rate (%)', style: { color: '#64748b', fontSize: '11px' } },
        gridLineColor: '#f1f5f9',
        labels: { format: '{value}%', style: { color: '#64748b', fontSize: '11px' } },
        plotLines: [
          {
            value: 0,
            color: '#94a3b8',
            width: 1.5,
            dashStyle: 'Dash',
            zIndex: 3
          }
        ]
      },
      tooltip: {
        shared: true,
        backgroundColor: '#ffffff',
        borderColor: '#e2e8f0',
        borderRadius: 8,
        valueSuffix: '%',
        valueDecimals: 2
      },
      legend: {
        align: 'right',
        verticalAlign: 'top',
        itemStyle: { color: '#334155', fontSize: '12px', fontWeight: '600' }
      },
      plotOptions: {
        column: {
          borderRadius: 6,
          borderWidth: 0,
          dataLabels: {
            enabled: true,
            formatter: function() {
              if (this.y == null) return '';
              const prefix = this.y > 0 ? '+' : '';
              return `${prefix}${this.y.toFixed(1)}%`;
            },
            style: { fontSize: '10px', fontWeight: '700', color: '#1e293b' }
          }
        }
      },
      series: [
        {
          name: symA,
          type: 'column',
          data: [
            data.stockA.salesQuarterlyGrowthYoY ?? 0,
            data.stockA.salesQuarterlyGrowthQoQ ?? 0,
            data.stockA.profitQuarterlyGrowthYoY ?? 0,
            data.stockA.profitQuarterlyGrowthQoQ ?? 0
          ],
          color: '#0284c7'
        },
        {
          name: symB,
          type: 'column',
          data: [
            data.stockB.salesQuarterlyGrowthYoY ?? 0,
            data.stockB.salesQuarterlyGrowthQoQ ?? 0,
            data.stockB.profitQuarterlyGrowthYoY ?? 0,
            data.stockB.profitQuarterlyGrowthQoQ ?? 0
          ],
          color: '#e11d48'
        }
      ]
    };
  }

  // 6. Shareholding Distribution Chart
  private buildShareholdingChart(data: StockComparisonResponse): void {
    const symA = data.stockA.symbol;
    const symB = data.stockB.symbol;

    this.shareholdingChartOptions = {
      chart: {
        type: 'column',
        backgroundColor: 'transparent',
        spacing: [16, 12, 16, 12]
      },
      title: { text: undefined },
      credits: { enabled: false },
      xAxis: {
        categories: ['Promoter', 'Foreign (FII)', 'Domestic (DII)', 'Retail & Public'],
        labels: {
          style: { color: '#475569', fontSize: '11px', fontWeight: '600' }
        },
        lineColor: '#e2e8f0'
      },
      yAxis: {
        title: { text: 'Holding (%)', style: { color: '#64748b', fontSize: '11px' } },
        max: 100,
        gridLineColor: '#f1f5f9',
        labels: { format: '{value}%', style: { color: '#64748b', fontSize: '11px' } }
      },
      tooltip: {
        shared: true,
        backgroundColor: '#ffffff',
        borderColor: '#e2e8f0',
        borderRadius: 8,
        valueSuffix: '%',
        valueDecimals: 2
      },
      legend: {
        align: 'right',
        verticalAlign: 'top',
        itemStyle: { color: '#334155', fontSize: '12px', fontWeight: '600' }
      },
      plotOptions: {
        column: {
          borderRadius: 6,
          borderWidth: 0,
          dataLabels: {
            enabled: true,
            format: '{y:.1f}%',
            style: { fontSize: '10px', fontWeight: '700', color: '#1e293b' }
          }
        }
      },
      series: [
        {
          name: symA,
          type: 'column',
          data: [
            data.stockA.promoterHolding ?? 0,
            data.stockA.fiiHolding ?? 0,
            data.stockA.diiHolding ?? 0,
            data.stockA.publicHolding ?? 0
          ],
          color: '#0284c7'
        },
        {
          name: symB,
          type: 'column',
          data: [
            data.stockB.promoterHolding ?? 0,
            data.stockB.fiiHolding ?? 0,
            data.stockB.diiHolding ?? 0,
            data.stockB.publicHolding ?? 0
          ],
          color: '#e11d48'
        }
      ]
    };
  }

  // 7. Strengths & Risks (Pros & Cons) Count Balance Chart
  private buildProsConsChart(data: StockComparisonResponse): void {
    const symA = data.stockA.symbol;
    const symB = data.stockB.symbol;

    this.prosConsChartOptions = {
      chart: {
        type: 'column',
        backgroundColor: 'transparent',
        spacing: [16, 12, 16, 12]
      },
      title: { text: undefined },
      credits: { enabled: false },
      xAxis: {
        categories: ['Key Strengths (Pros)', 'Risk Factors (Cautions)'],
        labels: {
          style: { color: '#475569', fontSize: '11px', fontWeight: '600' }
        },
        lineColor: '#e2e8f0'
      },
      yAxis: {
        title: { text: 'Factor Count', style: { color: '#64748b', fontSize: '11px' } },
        allowDecimals: false,
        gridLineColor: '#f1f5f9',
        labels: { style: { color: '#64748b', fontSize: '11px' } }
      },
      tooltip: {
        shared: true,
        backgroundColor: '#ffffff',
        borderColor: '#e2e8f0',
        borderRadius: 8,
        valueSuffix: ' factors'
      },
      legend: {
        align: 'right',
        verticalAlign: 'top',
        itemStyle: { color: '#334155', fontSize: '12px', fontWeight: '600' }
      },
      plotOptions: {
        column: {
          borderRadius: 6,
          borderWidth: 0,
          dataLabels: {
            enabled: true,
            format: '{y}',
            style: { fontSize: '11px', fontWeight: '700', color: '#1e293b' }
          }
        }
      },
      series: [
        {
          name: symA,
          type: 'column',
          data: [
            data.stockA.pros?.length || 0,
            data.stockA.cons?.length || 0
          ],
          color: '#0284c7'
        },
        {
          name: symB,
          type: 'column',
          data: [
            data.stockB.pros?.length || 0,
            data.stockB.cons?.length || 0
          ],
          color: '#e11d48'
        }
      ]
    };
  }

  // --- Metrics comparison helpers ---
  getAdvantage(
    valA: number | undefined | null,
    valB: number | undefined | null,
    higherIsBetter: boolean,
    type: 'percent' | 'multiple' | 'currency' | 'ratio' = 'percent',
    symA: string = '',
    symB: string = ''
  ): { winner: 'A' | 'B' | 'TIE' | 'NA'; label: string; badgeClass: 'cyan' | 'rose' | 'tie' | 'na' } {
    // If either value is missing/null, it is NOT a tie! It's N/A (Incomplete Data)
    if (valA == null || valB == null) {
      return { winner: 'NA', label: '-', badgeClass: 'na' };
    }

    if (Math.abs(valA - valB) < 0.0001) {
      return { winner: 'TIE', label: 'Tie', badgeClass: 'tie' };
    }

    const isAWinner = higherIsBetter ? valA > valB : valA < valB;
    const winnerCode: 'A' | 'B' = isAWinner ? 'A' : 'B';
    const winnerSym = isAWinner ? symA : symB;
    const winnerClass: 'cyan' | 'rose' = isAWinner ? 'cyan' : 'rose';

    let detail = '';
    if (type === 'percent') {
      const diff = Math.abs(valA - valB);
      detail = `${winnerSym} (+${this.formatNum(diff, 2)}%)`;
    } else if (type === 'ratio') {
      const lower = Math.min(valA, valB);
      const higher = Math.max(valA, valB);
      if (higher > 0) {
        const discount = Math.round(((higher - lower) / higher) * 100);
        detail = `${winnerSym} (${discount}% Cheaper)`;
      } else {
        detail = `${winnerSym} (Better)`;
      }
    } else if (type === 'currency') {
      const diff = Math.abs(valA - valB);
      detail = `${winnerSym} (+${this.formatCr(diff)})`;
    } else if (type === 'multiple') {
      const minVal = Math.min(valA, valB);
      const maxVal = Math.max(valA, valB);
      if (minVal > 0) {
        const mult = (maxVal / minVal).toFixed(1);
        detail = `${winnerSym} (${mult}x Larger)`;
      } else {
        detail = `${winnerSym} (Larger)`;
      }
    }

    return {
      winner: winnerCode,
      label: detail || winnerSym,
      badgeClass: winnerClass
    };
  }

  formatNum(val: number | undefined | null, decimals = 2): string {
    return formatNumber(val, decimals, '-');
  }

  formatPct(val: number | undefined | null, decimals = 2, withSign = false): string {
    return formatPercent(val, decimals, withSign, '-');
  }

  formatCr(val: number | undefined | null): string {
    if (val == null) return '-';
    let inCrores = val;
    if (val >= 10000000) {
      inCrores = val / 10000000;
    }
    return `₹${formatNumber(inCrores, 2)} Cr`;
  }
}
