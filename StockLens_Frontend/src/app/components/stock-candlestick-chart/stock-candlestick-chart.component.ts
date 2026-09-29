import { PatternNames } from '../../constants/pattern-constants';
import { Component, Input, OnChanges, SimpleChanges, inject, Output, EventEmitter, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HighchartsChartComponent } from 'highcharts-angular';
import * as Highcharts from 'highcharts/highstock';
import HighchartsMore from 'highcharts/highcharts-more';
import HC_mouseWheelZoom from 'highcharts/modules/mouse-wheel-zoom';

if (typeof HighchartsMore === 'function') {
  (HighchartsMore as any)(Highcharts);
} else if (HighchartsMore && typeof (HighchartsMore as any).default === 'function') {
  (HighchartsMore as any).default(Highcharts);
}

if (typeof HC_mouseWheelZoom === 'function') {
  (HC_mouseWheelZoom as any)(Highcharts);
} else if (HC_mouseWheelZoom && typeof (HC_mouseWheelZoom as any).default === 'function') {
  (HC_mouseWheelZoom as any).default(Highcharts);
}
import { StockPriceHistoryService, PriceHistoryResponseDto } from '../../services/stock-price-history.service';

@Component({
  selector: 'app-stock-candlestick-chart',
  standalone: true,
  imports: [CommonModule, HighchartsChartComponent],
  templateUrl: './stock-candlestick-chart.component.html',
  styleUrls: ['./stock-candlestick-chart.component.css']
})
export class StockCandlestickChartComponent implements OnChanges, OnInit {
  @Input() symbol!: string;
  @Input() exchange: string = 'NSE';
  @Input() period: string = '1yr';
  @Input() chartType: 'candlestick' | 'area' = 'candlestick';
  @Input() customHeight: string = '100%';
  @Output() errorOccurred = new EventEmitter<string>();

  private priceService = inject(StockPriceHistoryService);
  private cd = inject(ChangeDetectorRef);

  loadingState: 'loading' | 'success' | 'error' | 'empty' = 'loading';
  errorMessage = '';
  priceHistory: PriceHistoryResponseDto | null = null;
  updateFlag = false;
  chart: any;

  // Visibility state for Candlestick series
  activeSeriesState: { [key: string]: boolean } = {
    'Candlestick': true,
    '50 DMA': false,
    '200 DMA': false,
    'Volume': true
  };

  // Pattern filtering state (Granular)
  activePatterns: { [key: string]: boolean } = {
    'Double Bottom': true,
    'Head & Shoulders': true,
    'Inverse H&S': true,
    'Double Top': true,
    'Bull Flag': true,
    'Bear Flag': true,
    'Ascending Triangle': true,
    'Descending Triangle': true,
    'Symmetrical Triangle': true,
    'Bullish Engulfing': true,
    'Bearish Engulfing': true,
    'Rectangle': true
  };

  // State for parent component to access without function calls (fixes NG0100)
  availablePatterns: { [key: string]: boolean } = {};
  patternCountsObj: { [key: string]: number } = {};

  Highcharts: typeof Highcharts = Highcharts;
  chartOptions: Highcharts.Options = {};

  legendData: any = null;

  updateLegendData(index: number, plotX?: number) {
    if (!this.priceHistory || index < 0 || index >= this.priceHistory.dates.length) return;
    const data = this.priceHistory;
    const open = data.opens && data.opens.length > index && data.opens[index] > 0 ? data.opens[index] : data.closePrices[index];
    const high = data.highs && data.highs.length > index && data.highs[index] > 0 ? Math.max(data.highs[index], Math.max(open, data.closePrices[index])) : Math.max(open, data.closePrices[index]);
    const low = data.lows && data.lows.length > index && data.lows[index] > 0 ? Math.min(data.lows[index], Math.min(open, data.closePrices[index])) : Math.min(open, data.closePrices[index]);
    const close = data.closePrices[index];
    const volume = data.volumes && data.volumes.length > index ? data.volumes[index] : 0;
    const currentDate = data.dates[index];
    const dateStr = Highcharts.dateFormat('%d %b %Y', new Date(currentDate).getTime());
    const isUp = close >= open;

    let hoverPattern = null;
    if (data.detectedPatterns) {
      hoverPattern = data.detectedPatterns.find((p: any) => {
        if (p.date !== currentDate) return false;
        const name = p.patternName || '';
        const isReversal = name === 'Double Bottom' || name === 'Double Top' || name === 'Head & Shoulders' || name === 'Inverse H&S';
        const isCandlestick = name.includes('Engulfing');
        return isReversal || isCandlestick;
      });
    }

    this.legendData = { dateStr, open, high, low, close, volume, isUp, plotX, hoverPattern };
    this.cd.detectChanges();
  }

  resetLegendData() {
    if (this.priceHistory && this.priceHistory.dates.length > 0) {
      this.updateLegendData(this.priceHistory.dates.length - 1);
    }
  }

  ngOnInit(): void {
    if (this.symbol && !this.priceHistory && this.loadingState !== 'success') {
      this.loadPriceHistory(false);
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['symbol'] && changes['symbol'].isFirstChange()) return;

    if (this.symbol && (changes['symbol'] || changes['exchange'])) {
      this.priceHistory = null;
      this.legendData = null;
      this.loadingState = 'loading';
      this.chartOptions = {};
      this.cd.detectChanges();
      this.loadPriceHistory(false);
    } else if (changes['period'] && !changes['symbol'] && !changes['exchange']) {
      this.zoomToPeriod();
    } else if (changes['chartType'] && this.priceHistory) {
      this.renderChart(this.priceHistory);
    }
  }

  zoomToPeriod(): void {
    if (!this.chart || !this.priceHistory || !this.priceHistory.dates.length) return;

    const dates = this.priceHistory.dates;
    const maxDate = new Date(dates[dates.length - 1]).getTime();
    let minDate = maxDate;

    const p = (this.period || '1yr').toLowerCase().trim();
    if (p === '1m') minDate = maxDate - (30 * 24 * 3600 * 1000);
    else if (p === '3m') minDate = maxDate - (90 * 24 * 3600 * 1000);
    else if (p === '6m') minDate = maxDate - (180 * 24 * 3600 * 1000);
    else if (p === '1yr' || p === '1y') minDate = maxDate - (365 * 24 * 3600 * 1000);
    else if (p === '3yr' || p === '3y') minDate = maxDate - (3 * 365 * 24 * 3600 * 1000);
    else if (p === '5yr' || p === '5y') minDate = maxDate - (5 * 365 * 24 * 3600 * 1000);
    else minDate = new Date(dates[0]).getTime();

    // Prevent zooming further back than the actual data we have to avoid huge visual gaps
    const actualMinDate = new Date(dates[0]).getTime();
    if (minDate < actualMinDate) {
      minDate = actualMinDate;
    }

    // Strict anchor to today's date (latest candle) so the right price axis remains perfectly sticky
    this.chart.xAxis[0].setExtremes(minDate, maxDate, true, true);
  }

  loadPriceHistory(refresh: boolean = false): void {
    this.loadingState = 'loading';
    this.priceHistory = null;
    this.legendData = null;
    this.chartOptions = {};
    this.errorMessage = '';
    this.cd.detectChanges();

    // ALWAYS fetch 5yr data regardless of current period. 
    // This ensures when user clicks '5Y' button later, we actually have the data to show, preventing huge gaps.
    this.priceService.getPriceHistory(this.symbol, this.exchange, '5y', refresh).subscribe({
      next: (data) => {
        if (!data || !data.dates || data.dates.length === 0) {
          this.loadingState = 'empty';
          this.cd.detectChanges();
        } else {
          this.priceHistory = data;

          // Pre-calculate pattern states for the template to prevent NG0100
          this.availablePatterns = {};
          if (data.detectedPatterns) {
            data.detectedPatterns.forEach((p: any) => {
              this.availablePatterns[p.patternName] = true;
            });
          }
          this.patternCountsObj = data.patternCounts || {};

          this.loadingState = 'success';
          this.renderChart(data);
          this.cd.detectChanges();
          // Auto-zoom to currently selected period after rendering
          setTimeout(() => this.zoomToPeriod(), 60);
        }
      },
      error: (err) => {
        console.error('Error fetching candlestick price history:', err);
        this.loadingState = 'error';

        if (err.status === 0 || (err.message && err.message.includes('Http failure response'))) {
          this.errorMessage = 'Failed to fetch OHLC data';
        } else {
          this.errorMessage = err.message || 'Failed to fetch OHLC data';
        }

        this.errorOccurred.emit(this.errorMessage);
        this.cd.detectChanges();
      }
    });
  }

  onRefreshClick(): void {
    this.loadPriceHistory(true);
  }

  hasPatternCategory(category: string): boolean {
    if (!this.priceHistory || !this.priceHistory.detectedPatterns) return false;
    const names = this.priceHistory.detectedPatterns.map((p: any) => p.patternName);
    if (category === 'Candlestick') return names.some((n: string) => n.includes('Engulfing'));
    if (category === 'Reversal') return names.some((n: string) => n === 'Double Bottom' || n === 'Double Top' || n === 'Head & Shoulders' || n === 'Inverse H&S');
    if (category === 'Continuation') return names.some((n: string) => n.includes('Flag') || n.includes('Triangle'));
    if (category === 'Neutral') return names.some((n: string) => n === 'Rectangle');
    return false;
  }

  toggleSeries(seriesName: string): void {
    this.activeSeriesState[seriesName] = !this.activeSeriesState[seriesName];
    if (this.chart && this.chart.series) {
      const s = this.chart.series.find((x: any) => x.name === seriesName);
      if (s) {
        s.setVisible(this.activeSeriesState[seriesName], true);
      }
    }
  }

  togglePattern(patternName: string) {
    this.activePatterns[patternName] = !this.activePatterns[patternName];

    // Toggle visibility of pattern series directly without rebuilding the entire chart
    if (this.chart && this.chart.series) {
      this.chart.series.forEach((s: any) => {
        const group = s.options?.customPatternGroup || s.userOptions?.customPatternGroup;
        if (group === patternName) {
          s.setVisible(this.activePatterns[patternName], false);
        }
      });
      this.chart.redraw();
    }
  }

  clearAllPatterns() {
    Object.keys(this.activePatterns).forEach(k => this.activePatterns[k] = false);
    if (this.chart && this.chart.series) {
      this.chart.series.forEach((s: any) => {
        const group = s.options?.customPatternGroup || s.userOptions?.customPatternGroup;
        if (group) {
          s.setVisible(false, false);
        }
      });
      this.chart.redraw();
    }
  }

  public getPatternColor(name: string): string {
    const nameLower = name.toLowerCase();

    // Check against standard constants
    if (nameLower.includes(PatternNames.DoubleBottom.toLowerCase())) return '#0ea5e9'; // Sky Blue
    if (nameLower.includes(PatternNames.DoubleTop.toLowerCase())) return '#f97316'; // Orange
    if (nameLower.includes(PatternNames.HeadAndShoulders.toLowerCase())) return '#8b5cf6'; // Purple
    if (nameLower.includes(PatternNames.InverseHeadAndShoulders.toLowerCase())) return '#06b6d4'; // Cyan
    if (nameLower.includes(PatternNames.BullFlag.toLowerCase())) return '#3b82f6'; // Blue
    if (nameLower.includes(PatternNames.BearFlag.toLowerCase())) return '#d946ef'; // Fuchsia
    if (nameLower.includes(PatternNames.AscendingTriangle.toLowerCase())) return '#0ea5e9'; // Sky Blue
    if (nameLower.includes(PatternNames.DescendingTriangle.toLowerCase())) return '#f59e0b'; // Amber
    if (nameLower.includes(PatternNames.SymmetricalTriangle.toLowerCase())) return '#8b5cf6'; // Purple

    // Check generic types like engulfing/hammer
    if (nameLower.includes('engulfing') || nameLower.includes('hammer') || nameLower.includes('piercing') || nameLower.includes('morning star')) {
      return '#22c55e'; // Green for bullish candlestick patterns
    }
    if (nameLower.includes('harami') || nameLower.includes('shooting star') || nameLower.includes('dark cloud') || nameLower.includes('evening star') || nameLower.includes('hanging man')) {
      return '#ef4444'; // Red for bearish candlestick patterns
    }

    if (nameLower.includes(PatternNames.Rectangle.toLowerCase())) return '#eab308'; // Yellow
    return '#38bdf8'; // Default Blue
  }

  private renderChart(data: PriceHistoryResponseDto): void {
    if (this.chart && this.chart.series) {
      this.chart.series.forEach((s: any) => {
        if (s.name) {
          this.activeSeriesState[s.name] = s.visible;
        }
      });
    }

    const p = (this.period || '1yr').toLowerCase().trim();

    // Prepare true OHLC Candlestick data: [timestamp, open, high, low, close]
    const candlestickData = data.dates.map((dateStr, i) => {
      const date = new Date(dateStr).getTime();
      const open = data.opens && data.opens.length > i && data.opens[i] > 0 ? data.opens[i] : data.closePrices[i];
      const high = data.highs && data.highs.length > i && data.highs[i] > 0 ? Math.max(data.highs[i], Math.max(open, data.closePrices[i])) : Math.max(open, data.closePrices[i]);
      const low = data.lows && data.lows.length > i && data.lows[i] > 0 ? Math.min(data.lows[i], Math.min(open, data.closePrices[i])) : Math.min(open, data.closePrices[i]);
      const close = data.closePrices[i];
      return [date, open, high, low, close];
    });

    // Prepare Volume data with bullish/bearish color encoding
    const volumeData = data.dates.map((dateStr, i) => {
      const date = new Date(dateStr).getTime();
      const open = data.opens && data.opens.length > i ? data.opens[i] : data.closePrices[i];
      const close = data.closePrices[i];
      const isBullish = close >= open;
      let gradientColor: any;
      if (this.chartType === 'area') {
        gradientColor = {
          linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
          stops: [[0, 'rgba(148, 163, 184, 0.4)'], [1, 'rgba(148, 163, 184, 0.05)']] // Slate-400
        };
      } else {
        gradientColor = isBullish ? {
          linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
          stops: [[0, 'rgba(34, 197, 94, 0.8)'], [1, 'rgba(34, 197, 94, 0.1)']] // Classic Green
        } : {
          linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
          stops: [[0, 'rgba(239, 68, 68, 0.8)'], [1, 'rgba(239, 68, 68, 0.1)']] // Classic Red
        };
      }
      return {
        x: date,
        y: data.volumes[i],
        color: gradientColor
      };
    });

    // Remove explicit tickInterval and label formats to allow Highcharts auto-scaling
    let tickInterval = undefined;
    let labelFormat = '{value:%b %Y}';

    if (p === '1m' || p === '3m') {
      labelFormat = '{value:%e %b}';
    }

    const mainSeries: any = this.chartType === 'area' ? {
      type: 'area',
      id: 'candlestick-series-' + p,
      name: 'Candlestick', // Kept same for activeSeriesState compatibility
      legendIndex: 1,
      data: data.dates.map((d, i) => [new Date(d).getTime(), data.closePrices[i]]),
      color: '#38bdf8', // Neon blue line
      lineColor: '#38bdf8', // Explicitly set line color
      threshold: null, // Prevents Area chart from forcing Y-axis to start at 0
      fillColor: {
        linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
        stops: [[0, 'rgba(56, 189, 248, 0.35)'], [1, 'rgba(56, 189, 248, 0.0)']]
      },
      lineWidth: 1.5, // Thinner, elegant line
      marker: { enabled: false },
      visible: this.activeSeriesState['Candlestick'] !== false,
      yAxis: 0,
      zIndex: 2,
      states: { inactive: { opacity: 1 } }
    } : {
      type: 'candlestick',
      id: 'candlestick-series-' + p,
      name: 'Candlestick',
      legendIndex: 1,
      point: { events: {} },
      data: candlestickData as any,
      color: '#ef4444', // Classic Red
      upColor: '#22c55e', // Classic Green
      lineColor: '#ef4444',
      upLineColor: '#22c55e',
      visible: this.activeSeriesState['Candlestick'] !== false,
      yAxis: 0,
      zIndex: 2,
      dataGrouping: { enabled: false },
      states: { inactive: { opacity: 1 } }
    };

    const chartSeries: Highcharts.SeriesOptionsType[] = [
      mainSeries,
      {
        type: 'column',
        id: 'volume-series-' + p,
        name: 'Volume',
        legendIndex: 4,
        data: volumeData as any,
        visible: this.activeSeriesState['Volume'] !== false,
        yAxis: 1,
        zIndex: 1,
        borderWidth: 0,
        crisp: false, // Prevents pixel-snapping jitter while panning
        dataGrouping: { enabled: false }, // Prevents bars from grouping/flashing on zoom/pan
        turboThreshold: 0, // IMPORTANT: Allows >1000 points when data contains object arrays {x, y, color}
        states: {
          inactive: { opacity: 1 } // Do not dim volume bars
        }
      } as any
    ];

    // 50 DMA
    if (data.dma50 && data.dma50.length > 0) {
      const dma50Points = data.dates
        .map((dateStr, i) => [new Date(dateStr).getTime(), data.dma50![i]])
        .filter(pt => pt[1] !== null && pt[1] !== undefined);

      if (dma50Points.length > 0) {
        chartSeries.push({
          type: 'line',
          id: 'dma50-series-' + p,
          name: '50 DMA',
          legendIndex: 2,
          data: dma50Points as any,
          color: '#eab308', // Amber-500
          visible: this.activeSeriesState['50 DMA'] !== false,
          yAxis: 0,
          lineWidth: 2,
          marker: { enabled: false },
          zIndex: 3,
          shadow: { color: 'rgba(234, 179, 8, 0.6)', width: 6, offsetX: 0, offsetY: 0 }
        } as any);
      }
    }

    // 200 DMA
    if (data.dma200 && data.dma200.length > 0) {
      const dma200Points = data.dates
        .map((dateStr, i) => [new Date(dateStr).getTime(), data.dma200![i]])
        .filter(pt => pt[1] !== null && pt[1] !== undefined);

      if (dma200Points.length > 0) {
        chartSeries.push({
          type: 'line',
          id: 'dma200-series-' + p,
          name: '200 DMA',
          legendIndex: 3,
          data: dma200Points as any,
          color: '#a855f7', // Purple-500
          visible: this.activeSeriesState['200 DMA'] !== false,
          yAxis: 0,
          lineWidth: 2,
          marker: { enabled: false },
          zIndex: 3,
          shadow: { color: 'rgba(168, 85, 247, 0.6)', width: 6, offsetX: 0, offsetY: 0 }
        } as any);
      }
    }

    // Pattern Detection (TradingView "Chart Patterns FEELS" Style)
    if (this.chartType === 'candlestick' && data.detectedPatterns && data.detectedPatterns.length > 0) {
      const bullishFlags: any[] = [];
      const bearishFlags: any[] = [];

      data.detectedPatterns.forEach((pat, idx) => {
        const groupName = pat.patternName;
        const isBullish = pat.signal === 'Bullish';
        const isClassical = pat.coordinates && pat.coordinates.length > 0;

        if (isClassical) {
          let patternColor = isBullish ? '#38bdf8' : '#f97316';
          const nameLower = pat.patternName.toLowerCase();

          // Fix self-intersecting polygons (hourglass shape) for Triangles/Flags
          // Backend sends points chronologically (A, B, C, D). Polygon needs perimeter order.
          let polygonCoords = [...pat.coordinates!];
          const isPolygon = nameLower.includes('triangle') || nameLower.includes('flag') || nameLower.includes('wedge') || nameLower.includes(PatternNames.Rectangle.toLowerCase());

          if (isPolygon && !nameLower.includes(PatternNames.Rectangle.toLowerCase())) {
            const peaks: any[] = [];
            const troughs: any[] = [];
            pat.coordinates!.forEach((pt, i) => {
              if (i % 2 === 0) peaks.push(pt);
              else troughs.push(pt);
            });
            // Perimeter: left-to-right on top, right-to-left on bottom
            polygonCoords = [...peaks, ...troughs.reverse()];
          }

          if (nameLower.includes(PatternNames.DoubleBottom.toLowerCase())) patternColor = '#0ea5e9'; // Sky Blue
          else if (nameLower.includes(PatternNames.DoubleTop.toLowerCase())) patternColor = '#f97316'; // Orange
          else if (nameLower.includes(PatternNames.HeadAndShoulders.toLowerCase())) patternColor = '#8b5cf6'; // Purple
          else if (nameLower.includes(PatternNames.InverseHeadAndShoulders.toLowerCase())) patternColor = '#06b6d4'; // Cyan
          else if (nameLower.includes(PatternNames.BullFlag.toLowerCase())) patternColor = '#3b82f6'; // Blue
          else if (nameLower.includes(PatternNames.BearFlag.toLowerCase())) patternColor = '#d946ef'; // Fuchsia
          else if (nameLower.includes(PatternNames.AscendingTriangle.toLowerCase())) patternColor = '#0ea5e9'; // Sky Blue
          else if (nameLower.includes(PatternNames.DescendingTriangle.toLowerCase())) patternColor = '#f59e0b'; // Amber
          else if (nameLower.includes(PatternNames.SymmetricalTriangle.toLowerCase())) patternColor = '#8b5cf6'; // Purple
          else if (nameLower.includes(PatternNames.Rectangle.toLowerCase())) patternColor = '#eab308'; // Yellow

          const lineId = 'pattern-line-' + p + '-' + idx;

          // 1. Main Pattern Line (Parent Series)
          const labelIndex = pat.coordinates!.length === 6 ? 3 : 0; // Head for H&S, First Peak/Trough for Double
          const labelPoint = pat.coordinates![labelIndex];
          const middlePoint = pat.coordinates!.length === 6 ? pat.coordinates![3] : pat.coordinates![1]; // For Neckline height
          const startsWithPeak = !(nameLower.includes('bottom') || nameLower.includes('inverse'));
          const labelsData = pat.coordinates!.map((c, i) => {
            const isPeak = startsWithPeak ? (i % 2 === 0) : (i % 2 !== 0);
            return {
              x: new Date(c.date).getTime(),
              y: c.price,
              dataLabels: {
                enabled: i === pat.coordinates!.length - 1,
                format: pat.patternName,
                allowOverlap: true,
                crop: false,
                overflow: 'allow',
                align: 'center',
                verticalAlign: isPeak ? 'bottom' : 'top',
                y: isPeak ? -10 : 10,
                style: { color: patternColor, fontSize: '13px', fontWeight: 'bold', textOutline: '2px white' }
              }
            };
          });

          // But for the polygon perimeter, we use polygonCoords
          const lineData = polygonCoords.map(c => [new Date(c.date).getTime(), c.price]);

          const seriesOptions: any = {
            type: isPolygon ? 'polygon' : 'line',
            customPatternGroup: groupName,
            id: lineId,
            name: pat.patternName,
            data: lineData,
            color: patternColor,
            lineWidth: nameLower.includes('triangle') ? 1.5 : 2,
            dashStyle: nameLower.includes('triangle') ? 'ShortDot' : 'Solid',
            marker: { enabled: false }, // Turn off default markers on polygon
            zIndex: 4,
            showInLegend: false,
            states: {
              hover: { enabled: true, lineWidth: 3, halo: { size: 9, opacity: 0.25 } },
              inactive: { opacity: 1 }
            }
          };

          if (isPolygon) {
            seriesOptions.fillColor = patternColor + '33'; // 20% opacity
          }

          chartSeries.push(seriesOptions);

          // Only add A,B,C,D labels for non-Double patterns
          if (!nameLower.includes('double')) {
            chartSeries.push({
              type: 'scatter',
              customPatternGroup: groupName,
              linkedTo: lineId,
              name: pat.patternName + ' Labels',
              data: labelsData,
              marker: { enabled: true, radius: 4, symbol: 'circle', fillColor: patternColor },
              enableMouseTracking: false,
              showInLegend: false,
              zIndex: 5
            } as any);
          }

          // TradingView Style Reversal Pattern Extras (Aesthetic UI for Double Patterns)
          if (pat.patternName === PatternNames.DoubleBottom || pat.patternName === PatternNames.DoubleTop) {
            const isDoubleTop = pat.patternName === PatternNames.DoubleTop;
            const p1 = pat.coordinates![0];
            const middle = pat.coordinates![1];
            const p2 = pat.coordinates![2];

            const p1X = new Date(p1.date).getTime();
            const p1Y = p1.price;
            const midX = new Date(middle.date).getTime();
            const midY = middle.price;
            const p2X = new Date(p2.date).getTime();
            const p2Y = p2.price;

            // Target calculation
            const height = Math.abs(p2Y - midY);
            const targetPrice = isDoubleTop ? (midY - height) : (midY + height);
            const lastDataTime = new Date(data.dates[data.dates.length - 1]).getTime();
            const targetEndX = Math.min(p2X + (20 * 24 * 60 * 60 * 1000), lastDataTime); // 20 days forward or last date

            const boxColor = patternColor;
            const boxFill = patternColor + '26';

            // 1. Support/Resistance Line connecting the two peaks/bottoms
            chartSeries.push({
              type: 'line',
              customPatternGroup: groupName,
              linkedTo: lineId,
              name: 'Support/Resistance',
              data: [
                { x: p1X, y: p1Y },
                {
                  x: p2X,
                  y: p2Y,
                  dataLabels: {
                    enabled: true,
                    allowOverlap: true,
                    crop: false,
                    overflow: 'allow',
                    format: pat.patternName,
                    align: 'center',
                    verticalAlign: isDoubleTop ? 'bottom' : 'top',
                    y: isDoubleTop ? -15 : 15,
                    style: { color: patternColor, fontSize: '13px', fontWeight: 'bold', textOutline: '2px white', backgroundColor: 'rgba(255,255,255,0.6)', borderRadius: '3px' }
                  }
                }
              ],
              color: patternColor,
              lineWidth: 1.5,
              dashStyle: 'ShortDash',
              enableMouseTracking: false,
              showInLegend: false,
              zIndex: 3
            } as any);

            // 2. Horizontal Neckline (Dotted line showing the breakout point)
            chartSeries.push({
              type: 'line',
              customPatternGroup: groupName,
              linkedTo: lineId,
              name: 'Neckline',
              data: [
                [midX, midY],
                [targetEndX, midY]
              ],
              color: patternColor,
              lineWidth: 2,
              dashStyle: 'ShortDot',
              enableMouseTracking: false,
              showInLegend: false,
              zIndex: 3
            } as any);

            // 3. Target Projection Box (Shaded Rectangle from Peak/Bottom to Target)
            chartSeries.push({
              type: 'polygon',
              customPatternGroup: groupName,
              linkedTo: lineId,
              name: 'Target Zone',
              data: [
                [p2X, p2Y],
                [p2X, targetPrice],
                [targetEndX, targetPrice],
                [targetEndX, p2Y]
              ],
              color: boxColor,
              fillColor: boxFill,
              lineWidth: 1,
              enableMouseTracking: false,
              showInLegend: false,
              zIndex: 1
            } as any);

            // 4. Vertical Target Arrow inside the box
            const arrowX = p2X + (targetEndX - p2X) / 2; // Middle of the box
            chartSeries.push({
              type: 'line',
              customPatternGroup: groupName,
              linkedTo: lineId,
              data: [
                [arrowX, p2Y],
                { x: arrowX, y: targetPrice, marker: { enabled: true, symbol: isDoubleTop ? 'triangle-down' : 'triangle', radius: 7, fillColor: boxColor } }
              ],
              color: boxColor,
              lineWidth: 2,
              enableMouseTracking: false,
              showInLegend: false,
              zIndex: 2
            } as any);

          } else {
            // General Neckline for other reversal patterns (like H&S)
            let necklineData: any[] = [];

            if (pat.patternName === PatternNames.HeadAndShoulders || pat.patternName === PatternNames.InverseHeadAndShoulders) {
              // Neckline connects the two troughs (or peaks) which are at index 1 and 3 in our coordinate array
              const p1 = pat.coordinates![1];
              const p2 = pat.coordinates![3];
              const p1X = new Date(p1.date).getTime();
              const p2X = new Date(p2.date).getTime();

              // Calculate slope to extend the line forward
              const slope = (p2.price - p1.price) / (p2X - p1X);
              const lastDataTime = new Date(data.dates[data.dates.length - 1]).getTime();
              const extensionTime = Math.min(p2X + (20 * 24 * 60 * 60 * 1000), lastDataTime); // 20 days forward or last date
              const extendedY = p2.price + (slope * (extensionTime - p2X));

              necklineData = [
                [p1X, p1.price],
                [p2X, p2.price],
                [extensionTime, extendedY]
              ];
            } else {
              const endPoint = pat.coordinates![pat.coordinates!.length - 1];
              const lastDataTime = new Date(data.dates[data.dates.length - 1]).getTime();
              const extensionTime = Math.min(new Date(endPoint.date).getTime() + (10 * 24 * 60 * 60 * 1000), lastDataTime);
              necklineData = [
                [new Date(pat.coordinates![0].date).getTime(), middlePoint.price],
                [extensionTime, middlePoint.price]
              ];
            }

            chartSeries.push({
              type: 'line',
              customPatternGroup: groupName,
              id: 'pattern-neckline-' + p + '-' + idx,
              linkedTo: lineId, // Link to parent line
              name: pat.patternName + ' Neckline',
              data: necklineData,
              color: patternColor,
              lineWidth: 2,
              dashStyle: 'ShortDash', // Dashed neckline like TradingView
              marker: { enabled: false },
              zIndex: 3,
              showInLegend: false,
              states: {
                hover: { enabled: true, lineWidth: 3 },
                inactive: { opacity: 1 }
              }
            } as any);
          }

          // ✅ Classical pattern ke liye BUY/SELL flag add karo
          const confirmationTime = new Date(pat.date).getTime();
          const priceIndex = data.dates.findIndex(d => d === pat.date);
          const confirmationPrice = priceIndex >= 0 ? data.closePrices[priceIndex] : 0;
          const highPrice = priceIndex >= 0 && data.highs && data.highs.length > priceIndex ? data.highs[priceIndex] : confirmationPrice;
          const lowPrice = priceIndex >= 0 && data.lows && data.lows.length > priceIndex ? data.lows[priceIndex] : confirmationPrice;

          const classicalFlag = {
            x: confirmationTime,
            y: isBullish ? lowPrice : highPrice,
            dataLabels: {
              enabled: true,
              useHTML: true,
              backgroundColor: 'transparent',
              shape: 'none',
              borderWidth: 0,
              shadow: false,
              padding: 0,
              format: isBullish
                ? `<div style="padding:16px;"><div class="marker-blink" style="--marker-color:#10b981; background-color:#10b981; color:white; border-radius:50%; width:22px; height:22px; text-align:center; line-height:20px; font-weight:bold; font-size:11px; border:1px solid white;">B</div></div>`
                : `<div style="padding:16px;"><div class="marker-blink" style="--marker-color:#ef4444; background-color:#ef4444; color:white; border-radius:50%; width:22px; height:22px; text-align:center; line-height:20px; font-weight:bold; font-size:11px; border:1px solid white;">S</div></div>`,
              align: 'center',
              verticalAlign: isBullish ? 'top' : 'bottom',
              y: 0,
              allowOverlap: true,
              crop: false,
              overflow: 'allow'
            }
          };

          chartSeries.push({
            type: 'scatter',
            customPatternGroup: groupName,
            linkedTo: lineId,
            name: pat.patternName + ' Signal',
            data: [classicalFlag],
            marker: { enabled: false },
            enableMouseTracking: false,
            showInLegend: false,
            zIndex: 5
          } as any);

        } else {
          // Small Abbreviation Label for Engulfing (B-ENG / S-ENG)
          const currTime = new Date(pat.date).getTime();
          const priceObj = this.priceHistory?.dates.findIndex(d => d === pat.date);
          const closePrice = priceObj !== undefined && priceObj >= 0 ? this.priceHistory!.closePrices[priceObj].toFixed(1) : '';
          const highPrice = priceObj !== undefined && priceObj >= 0 && this.priceHistory!.highs && this.priceHistory!.highs.length > priceObj ? this.priceHistory!.highs[priceObj] : 0;
          const lowPrice = priceObj !== undefined && priceObj >= 0 && this.priceHistory!.lows && this.priceHistory!.lows.length > priceObj ? this.priceHistory!.lows[priceObj] : 0;

          // Box highlighting the engulfing pattern
          if (priceObj !== undefined && priceObj > 0) {
            const prevIndex = priceObj - 1;
            const prevTime = new Date(this.priceHistory!.dates[prevIndex]).getTime();
            const prevHigh = this.priceHistory!.highs && this.priceHistory!.highs.length > prevIndex ? this.priceHistory!.highs[prevIndex] : highPrice;
            const prevLow = this.priceHistory!.lows && this.priceHistory!.lows.length > prevIndex ? this.priceHistory!.lows[prevIndex] : lowPrice;

            const maxH = Math.max(prevHigh, highPrice);
            const minL = Math.min(prevLow, lowPrice);

            // Give a little time padding around the 2 candles to draw a box
            const halfCandleMs = (currTime - prevTime) / 2.5;

            const boxX1 = prevTime - halfCandleMs;
            const boxX2 = currTime + halfCandleMs;

            const boxColor = isBullish ? '#10b981' : '#ef4444';
            const boxFill = isBullish ? 'rgba(16, 185, 129, 0.15)' : 'rgba(239, 68, 68, 0.15)';

            chartSeries.push({
              type: 'polygon',
              customPatternGroup: groupName,
              name: pat.patternName + ' Highlight',
              data: [
                [boxX1, maxH],
                [boxX2, maxH],
                [boxX2, minL],
                [boxX1, minL]
              ],
              color: boxColor,
              fillColor: boxFill,
              lineWidth: 1.5,
              dashStyle: 'ShortDot',
              enableMouseTracking: false,
              showInLegend: false,
              zIndex: 1
            } as any);
          }

          const flag = {
            x: currTime,
            y: isBullish ? lowPrice : highPrice,
            dataLabels: {
              enabled: true,
              useHTML: true,
              backgroundColor: 'transparent',
              shape: 'none',
              borderWidth: 0,
              shadow: false,
              padding: 0,
              format: isBullish
                ? `<div style="padding:16px;"><div class="marker-blink" style="--marker-color:#10b981; background-color:#10b981; color:white; border-radius:4px; padding: 2px 6px; height:22px; text-align:center; line-height:16px; font-weight:bold; font-size:10px; border:1px solid white; white-space:nowrap;">B-ENG</div></div>`
                : `<div style="padding:16px;"><div class="marker-blink" style="--marker-color:#ef4444; background-color:#ef4444; color:white; border-radius:4px; padding: 2px 6px; height:22px; text-align:center; line-height:16px; font-weight:bold; font-size:10px; border:1px solid white; white-space:nowrap;">S-ENG</div></div>`,
              align: 'center',
              verticalAlign: isBullish ? 'top' : 'bottom',
              y: 0,
              allowOverlap: true,
              crop: false,
              overflow: 'allow'
            }
          };

          chartSeries.push({
            type: 'scatter',
            customPatternGroup: groupName,
            name: pat.patternName,
            data: [flag],
            marker: { enabled: false },
            enableMouseTracking: false,
            showInLegend: false,
            zIndex: 5
          } as any);
        }
      });
    }

    this.resetLegendData();
    const self = this;
    chartSeries.forEach((s: any) => {
      if (s.customPatternGroup && this.activePatterns[s.customPatternGroup] === false) {
        s.visible = false;
      }
    });

    this.chartOptions = {
      chart: {
        animation: {
          duration: 400,
          easing: 'easeOutQuart'
        },
        alignTicks: false,
        backgroundColor: 'transparent',
        style: { fontFamily: 'Inter, sans-serif' },
        marginRight: 60,
        marginLeft: 48,
        marginTop: 65,
        marginBottom: 38,
        height: 600,
        spacing: [4, 4, 8, 4],
        zooming: {
          mouseWheel: {
            enabled: true,
            type: 'x'
          }
        },
        panning: {
          enabled: true,
          type: 'x'
        }
      },

      title: {
        text: ''
      },
      legend: {
        enabled: false
      },
      credits: {
        enabled: false
      },
      rangeSelector: {
        enabled: false
      },
      navigator: {
        enabled: false
      },
      scrollbar: {
        enabled: false
      },
      xAxis: {
        type: 'datetime',
        gridLineColor: 'rgba(255, 255, 255, 0.05)',
        crosshair: {
          color: '#38bdf8',
          dashStyle: 'ShortDot',
          width: 2,
          zIndex: 5,
          label: {
            enabled: true,
            formatter: function (this: any): string {
              return Highcharts.dateFormat('%d %b %Y', this.value);
            },
            backgroundColor: '#38bdf8',
            style: { color: '#0f141c', fontWeight: 'bold', fontSize: '11px' }
          }
        },
        labels: {
          style: { color: 'rgba(255, 255, 255, 0.9)', fontSize: '11px', fontWeight: '600' },
          formatter: function (this: any): string {
            const date = new Date(this.value);
            const tickPositions = this.axis.tickPositions;
            const index = tickPositions.indexOf(this.value);

            let isMonthStart = false;
            let isYearStart = false;

            if (index === 0) {
              isMonthStart = true;
            } else if (index > 0) {
              const prevDate = new Date(tickPositions[index - 1]);
              if (date.getMonth() !== prevDate.getMonth()) {
                isMonthStart = true;
              }
              if (date.getFullYear() !== prevDate.getFullYear()) {
                isYearStart = true;
              }
            }

            if (isYearStart) {
              return Highcharts.dateFormat('%Y', this.value);
            }
            if (isMonthStart) {
              return Highcharts.dateFormat('%b', this.value);
            }
            return Highcharts.dateFormat('%e', this.value);
          },
          y: 20
        },
        tickInterval: tickInterval,
        lineColor: 'rgba(255, 255, 255, 0.15)',
        tickColor: 'rgba(255, 255, 255, 0.15)',
        events: {
          setExtremes: (e: any) => {
            // TradingView-style Sticky Right Axis for Mouse Wheel Zoom
            if ((e.trigger === 'zoom' || e.trigger === 'mouseWheel') && e.min !== undefined && e.max !== undefined) {
              const latestDate = new Date(this.priceHistory!.dates[this.priceHistory!.dates.length - 1]).getTime();
              const currentMax = this.chart?.xAxis[0]?.max;

              // If the chart's right edge is currently at the latest date (meaning it is sticky to today)
              if (currentMax && currentMax >= latestDate - (24 * 3600 * 1000)) {
                e.preventDefault();
                const range = e.max - e.min;
                const newMax = latestDate;
                const newMin = Math.max(new Date(this.priceHistory!.dates[0]).getTime(), latestDate - range);

                // Apply the new extremes asynchronously to avoid infinite loops and Highcharts event conflicts
                setTimeout(() => {
                  this.chart?.xAxis[0]?.setExtremes(newMin, newMax, true, false, { trigger: 'stickyZoom' });
                }, 0);
              }
            }
          }
        }
      },
      yAxis: [
        {
          // Primary yAxis for Candlestick Price (Right Side)
          opposite: true,
          height: '100%', // Full height
          title: { text: '' },
          minPadding: 0.3, // Adds empty space BELOW the lowest candle to avoid volume overlap
          crosshair: {
            color: '#38bdf8',
            dashStyle: 'ShortDot',
            width: 2,
            label: {
              enabled: true,
              format: '₹{value:.2f}',
              backgroundColor: '#38bdf8',
              style: { color: '#0f141c', fontSize: '11px', fontWeight: 'bold' }
            }
          },
          gridLineColor: 'rgba(255, 255, 255, 0.05)',
          labels: {
            style: { color: 'rgba(255, 255, 255, 0.75)', fontSize: '11px', fontWeight: '500' },
            formatter: function (this: any) {
              return '₹' + Highcharts.numberFormat(this.value, 0, '', ',');
            }
          },
          plotLines: [] as any[]
        },
        {
          // Secondary yAxis for Volumes (Left Side)
          opposite: false,
          height: '100%', // Full height
          offset: 0,
          title: { text: '' },
          maxPadding: 3, // Forces the volume bars to stay in the lower 25% of the chart
          gridLineWidth: 0,
          labels: {
            style: { color: 'rgba(255, 255, 255, 0.45)', fontSize: '10px', fontWeight: '500' },
            formatter: function (this: any) {
              const val = this.value as number;
              if (val >= 10000000) return (val / 10000000).toFixed(0) + 'Cr';
              if (val >= 1000000) return (val / 1000000).toFixed(0) + 'M';
              if (val >= 1000) return (val / 1000).toFixed(0) + 'K';
              return val.toString();
            }
          }
        }
      ],
      tooltip: {
        shared: true,
        useHTML: true,
        backgroundColor: 'transparent',
        borderColor: 'transparent',
        shadow: false,
        borderWidth: 0,
        padding: 0,
        style: { pointerEvents: 'none', zIndex: 100 },
        positioner: function (labelWidth, labelHeight, point) {
          if (self.chartType === 'area') {
            let xPos = this.chart.plotLeft + point.plotX + 15;
            let yPos = this.chart.plotTop + point.plotY - 15;
            if (xPos + labelWidth > this.chart.plotLeft + this.chart.plotWidth) {
              xPos = this.chart.plotLeft + point.plotX - labelWidth - 15;
            }
            if (yPos < this.chart.plotTop) {
              yPos = this.chart.plotTop + 15;
            }
            return { x: xPos, y: yPos };
          }
          // Keep it floating exactly above the crosshair vertical line, lowered to avoid custom legend
          return { x: this.chart.plotLeft + point.plotX - (labelWidth / 2), y: 55 };
        },
        formatter: function (this: any) {
          const pt = this.points?.[0]?.point;
          if (pt && pt.index !== undefined) {
            self.updateLegendData(pt.index);
          }

          if (self.chartType === 'area') {
            let html = `<div style="background: rgba(15, 23, 42, 0.85); backdrop-filter: blur(8px); -webkit-backdrop-filter: blur(8px); border: 1px solid rgba(255,255,255,0.1); border-radius: 6px; padding: 8px 12px; font-size: 12px; color: #f8fafc; box-shadow: 0 4px 12px rgba(0,0,0,0.5);">`;
            html += `<div style="font-weight: 700; font-size: 13px; margin-bottom: 6px;">${Highcharts.dateFormat('%d %b %Y', this.x)}</div>`;

            if (this.points) {
              this.points.forEach((p: any) => {
                let name = p.series.name === 'Candlestick' ? 'Price' : p.series.name;
                let valStr = p.y.toLocaleString('en-IN');
                if (name !== 'Volume') valStr = '₹' + p.y.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

                let color = p.series.color;
                if (name === 'Volume') color = '#94a3b8';
                else if (name === '50 DMA') color = '#eab308';
                else if (name === '200 DMA') color = '#a855f7';
                else if (name === 'Price') color = '#38bdf8';

                html += `<div style="display: flex; align-items: center; gap: 6px; margin-bottom: 3px;">
                  <div style="width: 6px; height: 6px; border-radius: 50%; background: ${color}; box-shadow: 0 0 4px ${color};"></div>
                  <span style="color: #cbd5e1; flex: 1;">${name}:</span>
                  <span style="font-weight: 700;">${valStr}</span>
                </div>`;
              });
            }
            html += `</div>`;
            return html;
          }

          // Default Candlestick tooltip
          let html = `<div style="backdrop-filter: blur(12px); -webkit-backdrop-filter: blur(12px); background: rgba(15, 23, 42, 0.6); border: 1px solid rgba(255, 255, 255, 0.15); border-radius: 8px; padding: 6px 12px; color: #f8fafc; font-size: 12px; box-shadow: 0 8px 24px rgba(0,0,0,0.4); display: flex; flex-direction: column; align-items: center; gap: 4px;">`;
          html += `<span style="font-weight:700; color: #38bdf8; letter-spacing: 0.5px;">${Highcharts.dateFormat('%d %b %Y', this.x)}</span>`;
          if (self.legendData && self.legendData.hoverPattern) {
            const p = self.legendData.hoverPattern;
            const pColor = p.signal === 'Bullish' ? '#10b981' : (p.signal === 'Bearish' ? '#ef4444' : '#eab308');
            html += `<div style="padding-top: 4px; border-top: 1px solid rgba(255,255,255,0.1); width: 100%; text-align: center;"><span style="color: ${pColor}; font-weight:800; font-size: 11px;">${p.patternName}</span></div>`;
          }
          html += `</div>`;
          return html;
        }
      },
      plotOptions: {
        series: {
          animation: {
            duration: 1200,
            easing: 'easeOutQuart'
          },
          states: {
            inactive: { opacity: 1 }
          },
          events: {
            mouseOut: () => {
              this.resetLegendData();
            }
          }
        },
        candlestick: {
          color: '#ef4444',
          upColor: '#22c55e',
          lineColor: '#ef4444',
          upLineColor: '#22c55e',
          pointPadding: 0.1,
          groupPadding: 0.1
        },
        column: {
          borderWidth: 0,
          borderRadius: 2
        }
      },
      series: chartSeries
    };



    this.updateFlag = true;
    this.cd.detectChanges();
  }
}





