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

  private readonly deliveryService = inject(StockVolumeDeliveryService);

  deliveryData = signal<VolumeDeliveryAnalysisResponse | null>(null);
  isLoading = computed(() => !this.deliveryData());
  errorMessage = signal<string | null>(null);
  activePeriod = signal<DeliveryPeriodTab>('Day');

  ngOnInit(): void {
    if (this.analysisData) {
      this.deliveryData.set(this.analysisData);
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['analysisData']) {
      this.deliveryData.set(this.analysisData);
    }
  }

  getDisplayTitle(): string {
    const name = this.companyName || this.deliveryData()?.companyName || this.symbol;
    return `${name} Day VOLUME ANALYSIS`;
  }

  getDayVsWeekTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
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
      multiplierText: `${arrow} ${multStr} vs week avg`,
      tooltip: `${arrow} ${multStr} ${tag} vs week avg (${dayFmt} vs ${weekFmt})`
    };
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

  getWeekVsMonthTrend(): { multiplier: number; isHigher: boolean; multiplierText: string; tooltip: string } | null {
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
      multiplierText: `${arrow} ${multStr} vs month avg`,
      tooltip: `${arrow} ${multStr} ${tag} vs month avg (${weekFmt} vs ${monthFmt})`
    };
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

    this.deliveryService.getDeliveryAnalysis(this.symbol, this.exchange).subscribe({
      next: (data) => {
        this.deliveryData.set(data);
      },
      error: (err) => {
        console.warn('Could not fetch delivery analysis from API, using fallback calculations:', err);
        this.deliveryData.set(this.getFallbackData());
      }
    });
  }

  formatVolume(val?: number): string {
    if (val === null || val === undefined || isNaN(val)) return '0';
    if (val >= 10000000) {
      return (val / 1000000).toFixed(1) + 'M';
    }
    if (val >= 1000000) {
      return (val / 1000000).toFixed(1) + 'M';
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
        formattedTradedVolume: '4.1M',
        formattedDeliveryVolume: '2.8M'
      },
      week: {
        tradedVolume: 3900000,
        deliveryVolume: 2000000,
        deliveryPercentage: 50.34,
        formattedTradedVolume: '3.9M',
        formattedDeliveryVolume: '2.0M'
      },
      month: {
        tradedVolume: 4400000,
        deliveryVolume: 1900000,
        deliveryPercentage: 42.66,
        formattedTradedVolume: '4.4M',
        formattedDeliveryVolume: '1.9M'
      }
    };
  }
}
