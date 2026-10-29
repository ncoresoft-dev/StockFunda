import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockCashflowResponse } from '../../models/stock-cashflow.model';
import { BalanceSheetResponseDto } from '../../services/stock-balancesheet.service';
import { formatCrores, formatPercent } from '../../utils/formatters.util';

@Component({
  selector: 'app-stock-cashflow-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-cashflow-card.component.html',
  styleUrl: './stock-cashflow-card.component.css'
})
export class StockCashflowCardComponent {
  @Input() cashflow: StockCashflowResponse | null = null;
  @Input() balanceSheet: BalanceSheetResponseDto | null = null;
  @Input() isLightTheme: boolean = true;

  get latestFiscalYear(): string {
    return this.cashflow?.latestFiscalYear || 'Annual';
  }

  // --- Formatting Delegation ---
  formatCrores(val?: number | null): string {
    return formatCrores(val, 0, '—', 0, ' Cr.');
  }

  formatPercent(val?: number | null): string {
    return formatPercent(val, 1, true, '—');
  }

  // --- CFO / OP Ratio ---
  getCfoToOpRatio(): number | null {
    const fromSummary = this.cashflow?.summary?.cfoToOperatingProfitRatio;
    if (fromSummary !== null && fromSummary !== undefined && !isNaN(fromSummary)) {
      return fromSummary;
    }
    const cfo = this.cashflow?.summary?.operatingCashFlow;
    const op = this.cashflow?.summary?.operatingProfit;
    if (cfo !== null && cfo !== undefined && op !== null && op !== undefined && op !== 0) {
      return +(cfo / op).toFixed(2);
    }
    return null;
  }

  formatCfoToOp(value?: number | null): string {
    const val = value !== undefined ? value : this.getCfoToOpRatio();
    if (val === null || val === undefined || isNaN(val)) return '—';
    const pct = val > 5 ? val : val * 100;
    return `${Math.round(pct)}%`;
  }

  getCfoConfidenceClass(ratio: number | null): string {
    if (ratio === null) return 'neutral';
    return (ratio >= 0.8 || ratio >= 80) ? 'positive' : ((ratio >= 0.5 || ratio >= 50) ? 'neutral' : 'negative');
  }

  getCfoConfidenceText(ratio: number | null): string {
    if (ratio === null) return '—';
    return (ratio >= 0.8 || ratio >= 80) ? 'High Conv.' : ((ratio >= 0.5 || ratio >= 50) ? 'Moderate' : 'Low');
  }

  // --- Borrowings Calculations ---
  getLatestLongTermBorrowings(): number | null {
    const bs = this.balanceSheet;
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
    const bs = this.balanceSheet;
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
    const bs = this.balanceSheet;
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
    const bs = this.balanceSheet;
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

  // --- Visual Helpers ---
  getChangeClass(value: number | null | undefined): string {
    if (value === null || value === undefined) return 'neutral';
    if (value > 0) return 'positive';
    if (value < 0) return 'negative';
    return 'neutral';
  }

  getInvertedChangeClass(value: number | null | undefined): string {
    if (value === null || value === undefined) return 'neutral';
    if (value > 0) return 'negative';
    if (value < 0) return 'positive';
    return 'neutral';
  }

  getValueColorClass(value: number | null | undefined): string {
    if (value === null || value === undefined || isNaN(value)) return 'neutral';
    return value < 0 ? 'text-red' : 'text-green';
  }

  getArrow(value: number | null | undefined): string {
    if (value === null || value === undefined) return '';
    if (value > 0) return '▲';
    if (value < 0) return '▼';
    return '';
  }
}
