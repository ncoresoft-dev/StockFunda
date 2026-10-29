import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockShareholdingResponse } from '../../models/stock-shareholding.model';

@Component({
  selector: 'app-stock-shareholding-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-shareholding-card.component.html',
  styleUrl: './stock-shareholding-card.component.css'
})
export class StockShareholdingCardComponent {
  @Input() shareholding: StockShareholdingResponse | null = null;
  @Input() isLightTheme: boolean = true;

  get ownershipBreakdown(): {
    promoter: string;
    fii: string;
    dii: string;
    public: string;
    promoterPp: string;
    fiiPp: string;
    diiPp: string;
    publicPp: string;
    promoterIsPos: boolean;
    promoterIsNeg: boolean;
    fiiIsPos: boolean;
    fiiIsNeg: boolean;
    diiIsPos: boolean;
    diiIsNeg: boolean;
    publicIsPos: boolean;
    publicIsNeg: boolean;
    totalInstitutional: string;
    history: { period: string; promoter: string; fii: string; dii: string; public: string }[];
  } {
    const sh = this.shareholding;
    const curr = sh?.currentPeriod;

    const formatPct = (val?: number | null) => {
      if (val === null || val === undefined || isNaN(val)) return '—';
      return `${val.toFixed(2)}%`;
    };

    const promoter = formatPct(curr?.promoter);
    const fii = formatPct(curr?.fii);
    const dii = formatPct(curr?.dii);
    const pub = formatPct(curr?.public);

    // Deltas: handles positive, negative, and neutral (0 / missing) cleanly
    const formatDelta = (val?: number | null) => {
      if (val === null || val === undefined || isNaN(val)) {
        return { text: '—', isPos: false, isNeg: false };
      }
      const rounded = Number(val.toFixed(2));
      if (rounded === 0) {
        return { text: '0.00%', isPos: false, isNeg: false };
      }
      const isPos = rounded > 0;
      const arrow = isPos ? '▲' : '▼';
      const sign = isPos ? '+' : '';
      return {
        text: `${arrow} ${sign}${Math.abs(val).toFixed(2)}%`,
        isPos,
        isNeg: !isPos
      };
    };

    const promDelta = formatDelta(sh?.change?.promoter);
    const fiiDelta = formatDelta(sh?.change?.fii);
    const diiDelta = formatDelta(sh?.change?.dii);
    const pubDelta = formatDelta(sh?.change?.public);

    const fiiNum = curr?.fii ?? 0;
    const diiNum = curr?.dii ?? 0;
    const totalInst =
      curr?.fii !== null && curr?.fii !== undefined && curr?.dii !== null && curr?.dii !== undefined
        ? (fiiNum + diiNum).toFixed(2) + '%'
        : '—';

    // History table rows from actual API response
    let historyRows: { period: string; promoter: string; fii: string; dii: string; public: string }[] = [];
    if (sh?.history && sh.history.length > 0) {
      historyRows = sh.history.slice(0, 4).map(h => ({
        period: h.period || '—',
        promoter: h.promoter !== null && h.promoter !== undefined ? `${h.promoter}%` : '—',
        fii: h.fii !== null && h.fii !== undefined ? `${h.fii}%` : '—',
        dii: h.dii !== null && h.dii !== undefined ? `${h.dii}%` : '—',
        public: h.public !== null && h.public !== undefined ? `${h.public}%` : '—'
      }));
    }

    return {
      promoter,
      fii,
      dii,
      public: pub,
      promoterPp: promDelta.text,
      fiiPp: fiiDelta.text,
      diiPp: diiDelta.text,
      publicPp: pubDelta.text,
      promoterIsPos: promDelta.isPos,
      promoterIsNeg: promDelta.isNeg,
      fiiIsPos: fiiDelta.isPos,
      fiiIsNeg: fiiDelta.isNeg,
      diiIsPos: diiDelta.isPos,
      diiIsNeg: diiDelta.isNeg,
      publicIsPos: pubDelta.isPos,
      publicIsNeg: pubDelta.isNeg,
      totalInstitutional: totalInst,
      history: historyRows
    };
  }
}
