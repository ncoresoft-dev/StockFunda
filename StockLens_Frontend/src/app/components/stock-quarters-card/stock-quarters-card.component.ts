import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockQuarterlyResultsResponse } from '../../models/stock-quarterly-results.model';
import { formatCrores } from '../../utils/formatters.util';

@Component({
  selector: 'app-stock-quarters-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-quarters-card.component.html',
  styleUrl: './stock-quarters-card.component.css'
})
export class StockQuartersCardComponent {
  @Input() quarters: StockQuarterlyResultsResponse | null = null;
  @Input() isLightTheme: boolean = true;

  get latestQuarterPeriod(): string {
    const q = this.quarters;
    return q?.latestQuarter || q?.summary?.period || '—';
  }

  get quarterlyFinancialsData(): {
    sales: string;
    salesQoQ: { text: string; isPositive: boolean; isNegative: boolean };
    salesYoY: { text: string; isPositive: boolean; isNegative: boolean };
    netProfit: string;
    netProfitQoQ: { text: string; isPositive: boolean; isNegative: boolean };
    netProfitYoY: { text: string; isPositive: boolean; isNegative: boolean };
    operatingProfit: string;
    operatingProfitQoQ: { text: string; isPositive: boolean; isNegative: boolean };
    operatingProfitYoY: { text: string; isPositive: boolean; isNegative: boolean };
    opMargin: string;
    eps: string;
    epsQoQ: { text: string; isPositive: boolean; isNegative: boolean };
    interest: string;
    interestQoQ: { text: string; isPositive: boolean; isNegative: boolean };
    tax: string;
    taxQoQ: { text: string; isPositive: boolean; isNegative: boolean };
    depreciation: string;
    depreciationQoQ: { text: string; isPositive: boolean; isNegative: boolean };
  } {
    const q = this.quarters;
    const sum = q?.summary;

    const formatGrowth = (val?: number | null, prefix: string = 'QoQ') => {
      if (val === null || val === undefined || isNaN(val)) {
        return { text: `${prefix}: —`, isPositive: false, isNegative: false };
      }
      const rounded = Number(val.toFixed(1));
      if (rounded === 0) {
        return { text: `${prefix}: 0.0%`, isPositive: false, isNegative: false };
      }
      const isPositive = rounded > 0;
      const sign = isPositive ? '+' : '';
      return {
        text: `${prefix}: ${sign}${val.toFixed(1)}%`,
        isPositive,
        isNegative: !isPositive
      };
    };

    const sales = sum?.sales !== null && sum?.sales !== undefined ? formatCrores(sum.sales, 0, '—', 0, ' Cr.') : '—';
    const salesQoQ = formatGrowth(q?.qoQGrowth?.salesGrowthPercent, 'QoQ');
    const salesYoY = formatGrowth(q?.yoYGrowth?.salesGrowthPercent, 'YoY');

    const netProfit = sum?.netProfit !== null && sum?.netProfit !== undefined ? formatCrores(sum.netProfit, 0, '—', 0, ' Cr.') : '—';
    const netProfitQoQ = formatGrowth(q?.qoQGrowth?.netProfitGrowthPercent, 'QoQ');
    const netProfitYoY = formatGrowth(q?.yoYGrowth?.netProfitGrowthPercent, 'YoY');

    const operatingProfit = sum?.operatingProfit !== null && sum?.operatingProfit !== undefined ? formatCrores(sum.operatingProfit, 0, '—', 0, ' Cr.') : '—';
    const operatingProfitQoQ = formatGrowth(q?.qoQGrowth?.operatingProfitGrowthPercent, 'QoQ');
    const operatingProfitYoY = formatGrowth(q?.yoYGrowth?.operatingProfitGrowthPercent, 'YoY');
    const opMargin = sum?.opmPercentage !== null && sum?.opmPercentage !== undefined ? `Margin: ${sum.opmPercentage.toFixed(1)}%` : 'Margin: —';

    const eps = sum?.eps !== null && sum?.eps !== undefined ? `₹${sum.eps.toFixed(2)}` : '—';
    const epsQoQ = formatGrowth(q?.qoQGrowth?.epsGrowthPercent, 'QoQ');

    const interest = sum?.interest !== null && sum?.interest !== undefined ? formatCrores(sum.interest, 0, '—', 0, ' Cr.') : '—';
    const interestQoQ = formatGrowth(q?.qoQGrowth?.interestGrowthPercent, 'QoQ');

    const tax = sum?.tax !== null && sum?.tax !== undefined ? formatCrores(sum.tax, 0, '—', 0, ' Cr.') : '—';
    const taxQoQ = formatGrowth(q?.qoQGrowth?.taxGrowthPercent, 'QoQ');

    const depreciation = sum?.depreciation !== null && sum?.depreciation !== undefined ? formatCrores(sum.depreciation, 0, '—', 0, ' Cr.') : '—';
    const depreciationQoQ = formatGrowth(q?.qoQGrowth?.depreciationGrowthPercent, 'QoQ');

    return {
      sales,
      salesQoQ,
      salesYoY,
      netProfit,
      netProfitQoQ,
      netProfitYoY,
      operatingProfit,
      operatingProfitQoQ,
      operatingProfitYoY,
      opMargin,
      eps,
      epsQoQ,
      interest,
      interestQoQ,
      tax,
      taxQoQ,
      depreciation,
      depreciationQoQ
    };
  }
}
