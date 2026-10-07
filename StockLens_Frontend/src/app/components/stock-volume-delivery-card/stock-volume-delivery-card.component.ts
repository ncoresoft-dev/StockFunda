import { Component, Input, OnChanges, OnInit, SimpleChanges, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockVolumeDeliveryService } from '../../services/stock-volume-delivery.service';
import { VolumeDeliveryAnalysisResponse } from '../../models/stock-volume-delivery.model';

export type DeliveryPeriodTab = 'Day' | 'Week' | '1 Month';

@Component({
  selector: 'app-stock-volume-delivery-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-volume-delivery-card.component.html',
  styleUrl: './stock-volume-delivery-card.component.css'
})
export class StockVolumeDeliveryCardComponent implements OnInit, OnChanges {
  @Input({ required: true }) symbol: string = 'RELIANCE';
  @Input() exchange: string = 'NSE';
  @Input() companyName: string = '';
  @Input() analysisData: VolumeDeliveryAnalysisResponse | null = null;
  @Input() isLightTheme: boolean = false;

  private readonly deliveryService = inject(StockVolumeDeliveryService);

  deliveryData = signal<VolumeDeliveryAnalysisResponse | null>(null);
  isLoading = computed(() => !this.deliveryData());
  errorMessage = signal<string | null>(null);
  activePeriod = signal<DeliveryPeriodTab>('Day');

  ngOnInit(): void {
    if (this.analysisData) {
      this.deliveryData.set(this.analysisData);
    } else {
      this.loadDeliveryData();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['analysisData'] && this.analysisData) {
      this.deliveryData.set(this.analysisData);
    } else if (changes['symbol'] && this.symbol && !this.analysisData) {
      this.loadDeliveryData();
    }
  }

  getDisplayTitle(): string {
    const name = this.companyName || this.deliveryData()?.companyName || this.symbol;
    return `${name} Day VOLUME ANALYSIS`;
  }

  getMaxTradedVolume(): number {
    const d = this.deliveryData();
    if (!d) return 1;
    const v1 = d.day?.tradedVolume || 0;
    const v2 = d.week?.tradedVolume || 0;
    const v3 = d.month?.tradedVolume || 0;
    return Math.max(v1, v2, v3, 1);
  }

  getTradedBarHeightPct(tradedVol?: number): number {
    if (!tradedVol || tradedVol <= 0) return 60;
    const max = this.getMaxTradedVolume();
    const minHeight = 65;
    const ratio = Math.min(1, Math.max(0, tradedVol / max));
    return Math.round(minHeight + (100 - minHeight) * ratio);
  }

  getDayTradedVsWeekTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
    const d = this.deliveryData();
    if (!d?.day?.tradedVolume || !d?.week?.tradedVolume) return null;

    const dayVol = d.day.tradedVolume;
    const weekVol = d.week.tradedVolume;
    if (weekVol <= 0 || dayVol <= 0) return null;

    const isHigher = dayVol >= weekVol;
    const mult = isHigher ? (dayVol / weekVol) : (weekVol / dayVol);
    const multStr = mult.toFixed(2) + 'x';
    const arrow = isHigher ? '▲' : '▼';
    const tag = isHigher ? 'higher' : 'lower';

    const dayFmt = d.day.formattedTradedVolume || this.formatVolume(dayVol);
    const weekFmt = d.week.formattedTradedVolume || this.formatVolume(weekVol);

    return {
      multiplier: +mult.toFixed(2),
      isHigher,
      multiplierText: `${arrow} ${multStr}`,
      tooltip: `Traded Volume: ${arrow} ${multStr} ${tag} vs week avg (${dayFmt} vs ${weekFmt})`
    };
  }

  getDayDeliveryVsWeekTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
    const d = this.deliveryData();
    if (!d?.day || !d?.week) return null;

    const dayVol = d.day.deliveryVolume || (d.day.tradedVolume * (d.day.deliveryPercentage / 100));
    const weekVol = d.week.deliveryVolume || (d.week.tradedVolume * (d.week.deliveryPercentage / 100));

    if (!dayVol || !weekVol || weekVol <= 0 || dayVol <= 0) return null;

    const isHigher = dayVol >= weekVol;
    const mult = isHigher ? (dayVol / weekVol) : (weekVol / dayVol);
    const multStr = mult.toFixed(2) + 'x';
    const arrow = isHigher ? '▲' : '▼';
    const tag = isHigher ? 'higher' : 'lower';

    const dayFmt = d.day.formattedDeliveryVolume || this.formatVolume(dayVol);
    const weekFmt = d.week.formattedDeliveryVolume || this.formatVolume(weekVol);

    return {
      multiplier: +mult.toFixed(2),
      isHigher,
      multiplierText: `${arrow} ${multStr}`,
      tooltip: `Delivery Volume: ${arrow} ${multStr} ${tag} vs week avg (${dayFmt} vs ${weekFmt})`
    };
  }

  getDayVsWeekTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
    return this.getDayDeliveryVsWeekTrend();
  }

  getDayVsWeekPtsDiff(): { diff: number; text: string; isPositive: boolean } | null {
    const d = this.deliveryData();
    if (d?.day?.deliveryPercentage === undefined || d?.day?.deliveryPercentage === null ||
      d?.week?.deliveryPercentage === undefined || d?.week?.deliveryPercentage === null) {
      return null;
    }

    const diff = +(d.day.deliveryPercentage - d.week.deliveryPercentage).toFixed(2);
    const sign = diff > 0 ? '+' : '';
    return {
      diff,
      text: `${sign}${diff.toFixed(2)}%`,
      isPositive: diff >= 0
    };
  }

  getWeekTradedVsMonthTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
    const d = this.deliveryData();
    if (!d?.week?.tradedVolume || !d?.month?.tradedVolume) return null;

    const weekVol = d.week.tradedVolume;
    const monthVol = d.month.tradedVolume;
    if (monthVol <= 0 || weekVol <= 0) return null;

    const isHigher = weekVol >= monthVol;
    const mult = isHigher ? (weekVol / monthVol) : (monthVol / weekVol);
    const multStr = mult.toFixed(2) + 'x';
    const arrow = isHigher ? '▲' : '▼';
    const tag = isHigher ? 'higher' : 'lower';

    const weekFmt = d.week.formattedTradedVolume || this.formatVolume(weekVol);
    const monthFmt = d.month.formattedTradedVolume || this.formatVolume(monthVol);

    return {
      multiplier: +mult.toFixed(2),
      isHigher,
      multiplierText: `${arrow} ${multStr}`,
      tooltip: `Traded Volume: ${arrow} ${multStr} ${tag} vs month avg (${weekFmt} vs ${monthFmt})`
    };
  }

  getWeekDeliveryVsMonthTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
    const d = this.deliveryData();
    if (!d?.week || !d?.month) return null;

    const weekVol = d.week.deliveryVolume || (d.week.tradedVolume * (d.week.deliveryPercentage / 100));
    const monthVol = d.month.deliveryVolume || (d.month.tradedVolume * (d.month.deliveryPercentage / 100));

    if (!weekVol || !monthVol || monthVol <= 0 || weekVol <= 0) return null;

    const isHigher = weekVol >= monthVol;
    const mult = isHigher ? (weekVol / monthVol) : (monthVol / weekVol);
    const multStr = mult.toFixed(2) + 'x';
    const arrow = isHigher ? '▲' : '▼';
    const tag = isHigher ? 'higher' : 'lower';

    const weekFmt = d.week.formattedDeliveryVolume || this.formatVolume(weekVol);
    const monthFmt = d.month.formattedDeliveryVolume || this.formatVolume(monthVol);

    return {
      multiplier: +mult.toFixed(2),
      isHigher,
      multiplierText: `${arrow} ${multStr}`,
      tooltip: `Delivery Volume: ${arrow} ${multStr} ${tag} vs month avg (${weekFmt} vs ${monthFmt})`
    };
  }

  getWeekVsMonthTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
    return this.getWeekDeliveryVsMonthTrend();
  }

  getWeekVsMonthPtsDiff(): { diff: number; text: string; isPositive: boolean } | null {
    const d = this.deliveryData();
    if (d?.week?.deliveryPercentage === undefined || d?.week?.deliveryPercentage === null ||
      d?.month?.deliveryPercentage === undefined || d?.month?.deliveryPercentage === null) {
      return null;
    }

    const diff = +(d.week.deliveryPercentage - d.month.deliveryPercentage).toFixed(2);
    const sign = diff > 0 ? '+' : '';
    return {
      diff,
      text: `${sign}${diff.toFixed(2)}%`,
      isPositive: diff >= 0
    };
  }

  loadDeliveryData(): void {
    if (!this.symbol) return;

    this.deliveryData.set(null);
    this.errorMessage.set(null);

    // TODO: Verify if this data should come directly from the price history/chart data instead of a separate API call.
    // this.deliveryService.getDeliveryAnalysis(this.symbol, this.exchange).subscribe({
    //   next: (data) => {
    //     this.deliveryData.set(data);
    //   },
    //   error: (err) => {
    //     console.warn('Could not fetch delivery analysis from API, using fallback calculations:', err);
    //     this.deliveryData.set(this.getFallbackData());
    //   }
    // });
    
    this.deliveryData.set(this.getFallbackData());
  }

  formatVolume(val?: number): string {
    if (val === null || val === undefined || isNaN(val)) return '0';
    if (val >= 10000000) {
      return (val / 1000000).toFixed(2) + 'M';
    }
    if (val >= 1000000) {
      return (val / 1000000).toFixed(2) + 'M';
    }
    if (val >= 1000) {
      return (val / 1000).toFixed(1) + 'K';
    }
    return val.toLocaleString();
  }

  private getFallbackData(): VolumeDeliveryAnalysisResponse {
    return {
      symbol: this.symbol,
      companyName: this.companyName || this.symbol,
      exchange: this.exchange,
      asOfDate: new Date().toISOString().split('T')[0],
      day: {
        tradedVolume: 4100000,
        deliveryVolume: 2800000,
        deliveryPercentage: 69.54,
        formattedTradedVolume: '4.10M',
        formattedDeliveryVolume: '2.80M'
      },
      week: {
        tradedVolume: 3900000,
        deliveryVolume: 2000000,
        deliveryPercentage: 50.34,
        formattedTradedVolume: '3.90M',
        formattedDeliveryVolume: '2.00M'
      },
      month: {
        tradedVolume: 4400000,
        deliveryVolume: 1900000,
        deliveryPercentage: 42.66,
        formattedTradedVolume: '4.40M',
        formattedDeliveryVolume: '1.90M'
      }
    };
  }
}
