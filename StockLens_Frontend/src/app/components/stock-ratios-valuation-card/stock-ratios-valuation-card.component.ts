import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockCashflowResponse, StockRatios, LoadingState } from '../../models/stock-cashflow.model';

@Component({
  selector: 'app-stock-ratios-valuation-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-ratios-valuation-card.component.html',
  styleUrl: './stock-ratios-valuation-card.component.css'
})
export class StockRatiosValuationCardComponent {
  @Input() cashflow: StockCashflowResponse | null = null;
  @Input() loadingState: LoadingState = 'idle';
  @Input() errorMessage: string = '';
  @Input() isRefreshing: boolean = false;

  @Output() refreshRequested = new EventEmitter<void>();

  get current() {
    return this.cashflow?.summary;
  }

  get ratios(): StockRatios | null {
    return this.cashflow?.summary?.ratios || this.cashflow?.ratios || null;
  }

  onRefreshClick(): void {
    if (!this.isRefreshing) {
      this.refreshRequested.emit();
    }
  }

  formatCurrency(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';

    const isNegative = value < 0;
    const absVal = Math.abs(value);
    const valInCrores = absVal >= 10_000_000 ? absVal / 10_000_000 : absVal;

    const formatted = new Intl.NumberFormat('en-IN', {
      maximumFractionDigits: 2,
      minimumFractionDigits: 0
    }).format(valInCrores);

    return `${isNegative ? '-' : ''}₹${formatted} Cr`;
  }

  formatPrice(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      maximumFractionDigits: 2,
      minimumFractionDigits: 2
    }).format(value);
  }

  formatPercentage(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return `${value.toFixed(2)}%`;
  }

  formatRatio(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    return `${value.toFixed(2)}x`;
  }

  getRoeTooltip(): string {
    const yoy = this.ratios?.roeYoY;
    const prev = this.ratios?.roePrevYear;
    const chg = this.ratios?.roeChange;
    if (yoy === null || yoy === undefined) return '';
    let tip = `YoY Growth: ${yoy >= 0 ? '+' : ''}${yoy.toFixed(1)}%`;
    if (chg !== null && chg !== undefined) tip += ` (${chg >= 0 ? '+' : ''}${chg.toFixed(2)}% pts)`;
    if (prev !== null && prev !== undefined) tip += ` vs Prev Year: ${this.formatPercentage(prev)}`;
    return tip;
  }

  getRoceTooltip(): string {
    const yoy = this.ratios?.roceYoY;
    const prev = this.ratios?.rocePrevYear;
    const chg = this.ratios?.roceChange;
    if (yoy === null || yoy === undefined) return '';
    let tip = `YoY Growth: ${yoy >= 0 ? '+' : ''}${yoy.toFixed(1)}%`;
    if (chg !== null && chg !== undefined) tip += ` (${chg >= 0 ? '+' : ''}${chg.toFixed(2)}% pts)`;
    if (prev !== null && prev !== undefined) tip += ` vs Prev Year: ${this.formatPercentage(prev)}`;
    return tip;
  }

  getRoeProgress(roe: number | null | undefined): number {
    if (!roe || roe <= 0) return 4;
    const pct = Math.round((roe / 25.0) * 100);
    return Math.min(100, Math.max(8, pct));
  }

  getRoceProgress(roce: number | null | undefined): number {
    if (!roce || roce <= 0) return 4;
    const pct = Math.round((roce / 30.0) * 100);
    return Math.min(100, Math.max(8, pct));
  }

  getPeProgress(pe: number | null | undefined): number {
    if (!pe || pe <= 0) return 4;
    const pct = Math.round((pe / 50.0) * 100);
    return Math.min(100, Math.max(8, pct));
  }

  get52WeekProgress(price: number | null | undefined, low: number | null | undefined, high: number | null | undefined): number {
    if (!low || !high || high <= low) return 50;
    const current = price ?? ((low + high) / 2);
    const range = high - low;
    const pos = ((current - low) / range) * 100;
    return Math.min(100, Math.max(4, Math.round(pos)));
  }

  getFaceValueProgress(fv: number | null | undefined): number {
    if (!fv || fv <= 0) return 10;
    const pct = Math.round((fv / 10.0) * 100);
    return Math.min(100, Math.max(10, pct));
  }

  getTotalEquityProgress(te: number | null | undefined): number {
    if (!te || te <= 0) return 60;
    const absVal = Math.abs(te);
    const inCr = absVal >= 10_000_000 ? absVal / 10_000_000 : absVal;
    const pct = Math.round((inCr / 500_000) * 100);
    return Math.min(100, Math.max(15, pct));
  }

  getBookValueProgress(bv: number | null | undefined): number {
    if (!bv || bv <= 0) return 20;
    const pct = Math.round((bv / 1500.0) * 100);
    return Math.min(100, Math.max(12, pct));
  }

  getMarketCapProgress(mc: number | null | undefined): number {
    if (!mc || mc <= 0) return 30;
    const absVal = Math.abs(mc);
    const inCr = absVal >= 10_000_000 ? absVal / 10_000_000 : absVal;
    const pct = Math.round((inCr / 2_000_000) * 100);
    return Math.min(100, Math.max(15, pct));
  }

  getEffectiveShares(): number | null {
    if (this.ratios?.totalShares !== undefined && this.ratios?.totalShares !== null) {
      return this.ratios.totalShares;
    }
    const eqCap = this.ratios?.equityCapital;
    const fv = this.ratios?.faceValue;
    if (eqCap && fv && fv > 0) {
      return eqCap / fv;
    }
    return null;
  }

  getEffectiveMarketCap(): number | null {
    if (this.ratios?.marketCap !== undefined && this.ratios?.marketCap !== null) {
      return this.ratios.marketCap;
    }
    const shares = this.getEffectiveShares();
    const price = this.ratios?.currentPrice;
    if (shares && price && price > 0) {
      return shares * price;
    }
    return null;
  }

  formatShares(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—';
    const formatted = new Intl.NumberFormat('en-IN', {
      maximumFractionDigits: 2,
      minimumFractionDigits: 2
    }).format(value);
    return `${formatted} Cr`;
  }

  getMarketCapSubMetric(): string {
    const shares = this.getEffectiveShares();
    const price = this.ratios?.currentPrice;
    if (shares && price) {
      return `${this.formatShares(shares)} shares @ ${this.formatPrice(price)}`;
    }
    return 'Market Capitalization';
  }

  getMarketCapTooltip(): string {
    const eqCap = this.ratios?.equityCapital;
    const fv = this.ratios?.faceValue;
    const shares = this.getEffectiveShares();
    const price = this.ratios?.currentPrice;
    const mc = this.getEffectiveMarketCap();

    if (eqCap && fv && shares && price && mc) {
      return `Total Shares = Equity Capital (₹${eqCap} Cr) ÷ Face Value (₹${fv}) = ${this.formatShares(shares)} shares\nMarket Cap = ${this.formatShares(shares)} × ${this.formatPrice(price)} = ${this.formatCurrency(mc)}`;
    }
    return this.ratios?.marketCapSource || 'Market Capitalization = Outstanding Shares × Current Price';
  }

  getSectorPeProgress(pe: number | null | undefined): number {
    if (!pe || pe <= 0) return 40;
    const pct = Math.round((pe / 50.0) * 100);
    return Math.min(100, Math.max(10, pct));
  }
}
