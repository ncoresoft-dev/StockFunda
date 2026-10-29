import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BalanceSheetResponseDto } from '../../services/stock-balancesheet.service';
import { StockCashflowResponse } from '../../models/stock-cashflow.model';
import { formatCrores, formatPercent } from '../../utils/formatters.util';

@Component({
  selector: 'app-stock-asset-growth-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-asset-growth-card.component.html',
  styleUrl: './stock-asset-growth-card.component.css'
})
export class StockAssetGrowthCardComponent {
  @Input() balanceSheet: BalanceSheetResponseDto | null = null;
  @Input() cashflow: StockCashflowResponse | null = null;
  @Input() isLightTheme: boolean = true;

  // --- Formatting Delegation ---
  formatCrores(val?: number | null): string {
    return formatCrores(val, 0, '—', 0, ' Cr.');
  }

  formatPercent(val?: number | null): string {
    return formatPercent(val, 1, true, '—');
  }

  // --- Fixed Assets & Total Assets ---
  getLatestTotalAssets(): number | null {
    const bs = this.balanceSheet;
    if (!bs || !bs.lineItems || bs.lineItems.length === 0) return null;
    const item = bs.lineItems.find(i => i.isTotal || (i.name && i.name.toLowerCase().includes('total assets')));
    if (!item || !item.values || item.values.length === 0) return null;
    return item.values[item.values.length - 1];
  }

  getLatestFixedAssets(): number | null {
    const bs = this.balanceSheet;
    if (!bs || !bs.lineItems || bs.lineItems.length === 0) return null;
    const item = bs.lineItems.find(i => i.name && (i.name.toLowerCase().includes('fixed assets') || i.name.toLowerCase().includes('property')));
    if (!item || !item.values || item.values.length === 0) return this.getLatestTotalAssets();
    return item.values[item.values.length - 1];
  }

  // --- Valuation & PEG Ratio ---
  getPegRatio(): number | null {
    const directPeg = this.cashflow?.ratios?.pegRatio ??
      this.cashflow?.summary?.ratios?.pegRatio;

    if (directPeg !== null && directPeg !== undefined && !isNaN(directPeg) && directPeg > 0) {
      return directPeg;
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

  getArrow(value: number | null | undefined): string {
    if (value === null || value === undefined) return '';
    if (value > 0) return '▲';
    if (value < 0) return '▼';
    return '';
  }
}
