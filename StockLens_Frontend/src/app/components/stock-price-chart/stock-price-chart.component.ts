import { Component, Input, OnChanges, SimpleChanges, inject, Output, EventEmitter, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HighchartsChartComponent } from 'highcharts-angular';
import * as Highcharts from 'highcharts/highstock';
import { StockPriceHistoryService, PriceHistoryResponseDto } from '../../services/stock-price-history.service';

@Component({
  selector: 'app-stock-price-chart',
  standalone: true,
  imports: [CommonModule, HighchartsChartComponent],
  templateUrl: './stock-price-chart.component.html',
  styleUrls: ['./stock-price-chart.component.css']
})
export class StockPriceChartComponent implements OnChanges, OnInit {
  @Input() symbol!: string;
  @Input() exchange: string = 'NSE';
  @Input() period: string = '1yr';
  @Input() customHeight: string = '100%';
  @Output() errorOccurred = new EventEmitter<string>();

  private priceService = inject(StockPriceHistoryService);
  private cd = inject(ChangeDetectorRef);

  loadingState: 'loading' | 'success' | 'error' | 'empty' = 'loading';
  errorMessage = '';
  priceHistory: PriceHistoryResponseDto | null = null;
  updateFlag = false;
  chart: any;

  activeSeriesState: { [key: string]: boolean } = {
    'Price': true,
    '50 DMA': true,
    '200 DMA': true,
    'Volume': true
  };

  Highcharts: typeof Highcharts = Highcharts;
  chartOptions: Highcharts.Options = {};

  ngOnInit(): void {
    if (this.symbol && !this.priceHistory && this.loadingState !== 'success') {
      this.loadPriceHistory(false);
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['symbol'] && changes['symbol'].isFirstChange()) return;

    if (this.symbol && (changes['symbol'] || changes['exchange'] || changes['period'])) {
      this.loadPriceHistory(false);
    }
  }

  loadPriceHistory(refresh: boolean = false): void {
    this.loadingState = 'loading';
    this.errorMessage = '';

    this.priceService.getPriceHistory(this.symbol, this.exchange, this.period, refresh).subscribe({
      next: (data) => {
        if (!data || !data.dates || data.dates.length === 0) {
          this.loadingState = 'empty';
          this.cd.detectChanges();
        } else {
          this.priceHistory = data;
          this.loadingState = 'success';
          this.renderChart(data);
        }
      },
      error: (err) => {
        console.error('Error fetching price history:', err);
        this.loadingState = 'error';

        if (err.status === 0 || (err.message && err.message.includes('Http failure response'))) {
          this.errorMessage = 'Failed to fetch';
        } else {
          this.errorMessage = err.message || 'Failed to fetch';
        }

        this.errorOccurred.emit(this.errorMessage);
        this.cd.detectChanges();
      }
    });
  }

  onRefreshClick(): void {
    this.loadPriceHistory(true);
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

  private renderChart(data: PriceHistoryResponseDto): void {
    if (this.chart && this.chart.series) {
      this.chart.series.forEach((s: any) => {
        if (s.name) {
          this.activeSeriesState[s.name] = s.visible;
        }
      });
    }

    const priceData = data.dates.map((dateStr, i) => {
      const date = new Date(dateStr).getTime();
      return [date, data.closePrices[i]];
    });

    const volumeData = data.dates.map((dateStr, i) => {
      const date = new Date(dateStr).getTime();
      return [date, data.volumes[i]];
    });

    let tickInterval = 365 * 24 * 3600 * 1000;
    let labelFormat = '{value:%Y}';
    const p = (this.period || '1yr').toLowerCase().trim();

    if (p === '1m') {
      tickInterval = 7 * 24 * 3600 * 1000;
      labelFormat = '{value:%e %b}';
    } else if (p === '6m') {
      tickInterval = 30 * 24 * 3600 * 1000;
      labelFormat = '{value:%e %b}';
    } else if (p === '1yr' || p === '1y') {
      tickInterval = 3 * 30 * 24 * 3600 * 1000;
      labelFormat = '{value:%b %Y}';
    } else if (p === '3yr' || p === '3y') {
      tickInterval = 6 * 30 * 24 * 3600 * 1000;
      labelFormat = '{value:%b %Y}';
    } else if (p === '5yr' || p === '5y') {
      tickInterval = 365 * 24 * 3600 * 1000;
      labelFormat = '{value:%b %Y}';
    } else {
      tickInterval = 2 * 365 * 24 * 3600 * 1000;
      labelFormat = '{value:%Y}';
    }

    const chartSeries: Highcharts.SeriesOptionsType[] = [
      {
        type: 'area',
        id: 'price-' + p,
        name: 'Price',
        legendIndex: 1,
        data: priceData,
        color: '#38bdf8',
        visible: this.activeSeriesState['Price'],
        yAxis: 0
      },
      {
        type: 'column',
        id: 'volume-' + p,
        name: 'Volume',
        legendIndex: 4,
        data: volumeData,
        color: 'rgba(148, 163, 184, 0.4)',
        visible: this.activeSeriesState['Volume'],
        yAxis: 1
      }
    ];

    if (data.dma50 && data.dma50.length > 0) {
      chartSeries.push({
        type: 'line',
        id: 'dma50-' + p,
        name: '50 DMA',
        legendIndex: 2,
        data: data.dates.map((dateStr, i) => [new Date(dateStr).getTime(), data.dma50![i]]),
        color: '#f97316',
        visible: this.activeSeriesState['50 DMA'],
        yAxis: 0,
        lineWidth: 1.5,
        marker: { enabled: false },
        tooltip: { pointFormat: '<b>₹{point.y:,.2f}</b><br/>' }
      } as any);
    }

    if (data.dma200 && data.dma200.length > 0) {
      chartSeries.push({
        type: 'line',
        id: 'dma200-' + p,
        name: '200 DMA',
        legendIndex: 3,
        data: data.dates.map((dateStr, i) => [new Date(dateStr).getTime(), data.dma200![i]]),
        color: '#a855f7',
        visible: this.activeSeriesState['200 DMA'],
        yAxis: 0,
        lineWidth: 1.5,
        marker: { enabled: false },
        tooltip: { pointFormat: '<b>₹{point.y:,.2f}</b><br/>' }
      } as any);
    }

    this.chartOptions = {
      chart: {
        backgroundColor: 'transparent',
        style: { fontFamily: 'Inter, sans-serif' },
        marginRight: 55,
        marginLeft: 45,
        marginTop: 10,
        marginBottom: 44,
        spacing: [4, 4, 12, 4]
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
      xAxis: {
        type: 'datetime',
        gridLineColor: 'rgba(255, 255, 255, 0.05)',
        labels: {
          style: { color: 'rgba(255, 255, 255, 0.9)', fontSize: '11px', fontWeight: '600' },
          format: labelFormat,
          y: 20
        },
        tickInterval: tickInterval,
        lineColor: 'rgba(255, 255, 255, 0.15)',
        tickColor: 'rgba(255, 255, 255, 0.15)'
      },
      yAxis: [
        {
          opposite: true,
          title: { text: '' },
          tickInterval: 100,
          gridLineColor: 'rgba(255, 255, 255, 0.05)',
          labels: {
            style: { color: 'rgba(255, 255, 255, 0.7)', fontSize: '11px', fontWeight: '500' },
            formatter: function (this: any) {
              return Highcharts.numberFormat(this.value, 0, '', ',');
            }
          }
        },
        {
          opposite: false,
          title: { text: '' },
          gridLineWidth: 0,
          tickInterval: 20000000,
          labels: {
            style: { color: 'rgba(255, 255, 255, 0.5)', fontSize: '10px', fontWeight: '500' },
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
        backgroundColor: 'rgba(20, 25, 35, 0.95)',
        borderColor: 'rgba(255, 255, 255, 0.1)',
        style: { color: '#fff' },
        formatter: function (this: any) {
          const dateStr = Highcharts.dateFormat('%d %b %Y', this.x);
          let tooltipHtml = `<div style="margin-bottom: 6px; font-size: 13px;"><b>${dateStr}</b></div>`;

          const pointMap: { [key: string]: any } = {};
          if (this.points) {
            this.points.forEach((p: any) => {
              pointMap[p.series.name] = p;
            });
          }

          if (pointMap['Price']) {
            tooltipHtml += `<span style="color:${pointMap['Price'].color}">\u25CF</span> Price: <b>₹${Highcharts.numberFormat(pointMap['Price'].y, 2, '.', ',')}</b><br/>`;
          }
          if (pointMap['50 DMA']) {
            tooltipHtml += `<span style="color:${pointMap['50 DMA'].color}">\u25CF</span> 50 DMA: <b>₹${Highcharts.numberFormat(pointMap['50 DMA'].y, 2, '.', ',')}</b><br/>`;
          }
          if (pointMap['200 DMA']) {
            tooltipHtml += `<span style="color:${pointMap['200 DMA'].color}">\u25CF</span> 200 DMA: <b>₹${Highcharts.numberFormat(pointMap['200 DMA'].y, 2, '.', ',')}</b><br/>`;
          }
          if (pointMap['Volume']) {
            const val = pointMap['Volume'].y;
            const formattedVol = new Intl.NumberFormat('en-IN').format(val);
            tooltipHtml += `<span style="color:${pointMap['Volume'].color}">\u25CF</span> Volume: <b>${formattedVol}</b><br/>`;
          }

          return tooltipHtml;
        }
      },
      plotOptions: {
        series: {
          states: {
            inactive: {
              opacity: 1
            }
          }
        },
        area: {
          fillColor: {
            linearGradient: { x1: 0, y1: 0, x2: 0, y2: 1 },
            stops: [
              [0, 'rgba(56, 189, 248, 0.5)'],
              [1, 'rgba(56, 189, 248, 0.0)']
            ]
          },
          marker: { radius: 2 },
          lineWidth: 2,
          states: { hover: { lineWidth: 3 } },
          threshold: null
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
