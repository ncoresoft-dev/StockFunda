import { Component, OnInit, OnDestroy, signal, computed, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { SectorAnalysisService } from '../../services/sector-analysis.service';
import { MoneyFlowResponse, SectorFlowItem, StockFlowItem } from '../../models/sector-analysis.model';

export interface FlowStrand {
  id: string;
  side: 'left' | 'right';
  targetType: 'inflow' | 'outflow';
  sourceSector?: string;
  stockSymbol?: string;
  stockSector?: string;
  amountCr: number;
  thickness: number;
  speed: number;
  isSurge: boolean;
  pathD: string;
}

export interface BuildupBadge {
  code: string;
  label: string;
  cssClass: string;
}

@Component({
  selector: 'app-money-flow-chart',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './money-flow-chart.component.html',
  styleUrls: ['./money-flow-chart.component.css']
})
export class MoneyFlowChartComponent implements OnInit, OnDestroy {
  // Input: Selected sector from parent (e.g. SectorAnalysisComponent)
  @Input() activeSector?: string | null;
  // Output: Emitted when user clicks any sector row in Money Flow
  @Output() sectorSelect = new EventEmitter<string>();
  // Output: Emitted when data is loaded from API
  @Output() dataLoaded = new EventEmitter<MoneyFlowResponse>();

  // State
  data = signal<MoneyFlowResponse | null>(null);
  isLoading = signal<boolean>(true);
  errorMessage = signal<string | null>(null);

  // Interactive Hover Highlight State (matches Image 2 behavior)
  hoveredSector = signal<string | null>(null);
  hoveredStock = signal<string | null>(null);

  private sub?: Subscription;

  // Geometry Constants (exact pixel locks for zero-drift alignment)
  readonly ROW_HEIGHT = 26; // px
  readonly ROW_GAP = 4;    // px
  readonly ROW_STEP = 30;   // ROW_HEIGHT + ROW_GAP = 30px

  readonly CANVAS_WIDTH = 420;
  readonly CANVAS_HEIGHT = 570; // 19 rows * 30px = 570px

  readonly GATE_X = 190;
  readonly INFLOW_GATE_Y = 190;
  readonly OUTFLOW_GATE_Y = 410;

  // Right column offsets:
  // Inflow header = 22px + 6px MB = 28px
  readonly INFLOW_START_Y = 28;
  // Outflow group starts at 320px
  readonly OUTFLOW_START_Y = 320;

  // Computed flow strands:
  // Inflow (Green / Positive): Sectors -> Center Gate -> Companies
  // Outflow (Red / Negative): Companies -> Center Gate -> Sectors
  flowStrands = computed<FlowStrand[]>(() => {
    const d = this.data();
    if (!d || !d.sectors || d.sectors.length === 0) return [];

    const strands: FlowStrand[] = [];
    const gateX = this.GATE_X;
    const inGateY = this.INFLOW_GATE_Y;
    const outGateY = this.OUTFLOW_GATE_Y;

    // 1. LEFT STRANDS
    d.sectors.forEach((sec, idx) => {
      const secY = idx * this.ROW_STEP + (this.ROW_HEIGHT / 2); // Exact vertical center of sector row

      // Inflow strand to Top Green Gate: Sector -> Center Inflow Gate
      if (sec.inflowCr > 0) {
        const thickness = Math.max(1.8, Math.min(6.0, (sec.inflowCr / 3000) * 3.5));
        const c1X = gateX * 0.55;
        const c1Y = secY;
        const c2X = gateX * 0.75;
        const c2Y = inGateY;

        strands.push({
          id: `left-${sec.sector}-inflow`,
          side: 'left',
          targetType: 'inflow',
          sourceSector: sec.sector,
          amountCr: sec.inflowCr,
          thickness,
          speed: Math.max(0.6, Math.min(3.8, sec.speed)),
          isSurge: sec.isSurge,
          pathD: `M 0 ${secY} C ${c1X} ${c1Y}, ${c2X} ${c2Y}, ${gateX} ${inGateY}`
        });
      }

      // Outflow strand: Red navigation passes Company -> Center Gate -> Sector (Negative sign flow)
      // Path directed from Center Outflow Gate to Sector (Leftwards movement)
      if (sec.outflowCr < 0) {
        const absOut = Math.abs(sec.outflowCr);
        const thickness = Math.max(1.8, Math.min(6.0, (absOut / 3000) * 3.5));
        const c1X = gateX * 0.55;
        const c1Y = secY;
        const c2X = gateX * 0.75;
        const c2Y = outGateY;

        strands.push({
          id: `left-${sec.sector}-outflow`,
          side: 'left',
          targetType: 'outflow',
          sourceSector: sec.sector,
          amountCr: absOut,
          thickness,
          speed: Math.max(0.6, Math.min(3.8, sec.speed)),
          isSurge: sec.isSurge,
          pathD: `M ${gateX} ${outGateY} C ${c2X} ${c2Y}, ${c1X} ${c1Y}, 0 ${secY}`
        });
      }
    });

    // 2. RIGHT STRANDS
    const rightX = this.CANVAS_WIDTH;

    // Top Inflow Stocks (up to 8 stocks): Center Inflow Gate -> Stock (Company)
    const topInflows = (d.topInflowStocks || []).slice(0, 8);

    topInflows.forEach((st, idx) => {
      // Exact center Y of stock row idx: INFLOW_START_Y + (idx * ROW_STEP) + (ROW_HEIGHT / 2)
      const stockY = this.INFLOW_START_Y + (idx * this.ROW_STEP) + (this.ROW_HEIGHT / 2);
      const thickness = Math.max(1.8, Math.min(5.2, (st.flowCr / 2500) * 3.0));
      const c1X = gateX + (rightX - gateX) * 0.25;
      const c1Y = inGateY;
      const c2X = gateX + (rightX - gateX) * 0.55;
      const c2Y = stockY;

      strands.push({
        id: `right-inflow-${st.symbol}`,
        side: 'right',
        targetType: 'inflow',
        stockSymbol: st.symbol,
        stockSector: st.sector,
        amountCr: st.flowCr,
        thickness,
        speed: Math.max(0.6, Math.min(3.8, st.speed)),
        isSurge: st.isSurge,
        pathD: `M ${gateX} ${inGateY} C ${c1X} ${c1Y}, ${c2X} ${c2Y}, ${rightX} ${stockY}`
      });
    });

    // Bottom Outflow Stocks (up to 8 stocks): Stock (Company) -> Center Outflow Gate
    // For negative flow, animation navigates from Company towards Center Gate
    const topOutflows = (d.topOutflowStocks || []).slice(0, 8);

    topOutflows.forEach((st, idx) => {
      // Exact center Y of stock row idx: OUTFLOW_START_Y + (idx * ROW_STEP) + (ROW_HEIGHT / 2)
      const stockY = this.OUTFLOW_START_Y + (idx * this.ROW_STEP) + (this.ROW_HEIGHT / 2);
      const absFlow = Math.abs(st.flowCr);
      const thickness = Math.max(1.8, Math.min(5.2, (absFlow / 2500) * 3.0));
      const c1X = gateX + (rightX - gateX) * 0.25;
      const c1Y = outGateY;
      const c2X = gateX + (rightX - gateX) * 0.55;
      const c2Y = stockY;

      strands.push({
        id: `right-outflow-${st.symbol}`,
        side: 'right',
        targetType: 'outflow',
        stockSymbol: st.symbol,
        stockSector: st.sector,
        amountCr: absFlow,
        thickness,
        speed: Math.max(0.6, Math.min(3.8, st.speed)),
        isSurge: st.isSurge,
        pathD: `M ${rightX} ${stockY} C ${c2X} ${c2Y}, ${c1X} ${c1Y}, ${gateX} ${outGateY}`
      });
    });

    return strands;
  });

  constructor(
    private sectorService: SectorAnalysisService,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.fetchMoneyFlow();
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  fetchMoneyFlow(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.sub?.unsubscribe();
    this.sub = this.sectorService.getMoneyFlow(5).subscribe({
      next: (res) => {
        if (res && res.sectors && res.sectors.length > 0) {
          this.data.set(res);
          this.errorMessage.set(null);
          this.dataLoaded.emit(res);
        } else {
          this.data.set(null);
          this.errorMessage.set('No institutional money flow data currently available from API.');
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Money flow API fetch error:', err);
        this.data.set(null);
        this.errorMessage.set('Unable to load live money flow. Please verify backend API server is running.');
        this.isLoading.set(false);
      }
    });
  }

  onSectorHover(sector: string): void {
    this.hoveredSector.set(sector);
    this.hoveredStock.set(null);
  }

  onStockHover(symbol: string): void {
    this.hoveredStock.set(symbol);
    const d = this.data();
    if (d) {
      const match = [...d.topInflowStocks, ...d.topOutflowStocks].find(s => s.symbol.toUpperCase() === symbol.toUpperCase());
      if (match && match.sector) {
        this.hoveredSector.set(match.sector);
      }
    }
  }

  onClearHover(): void {
    this.hoveredSector.set(null);
    this.hoveredStock.set(null);
  }

  onStockClick(symbol: string): void {
    if (!symbol) return;
    this.router.navigate(['/stock', symbol.toUpperCase()]);
  }

  // Interactive connection filtering for SVG strands (matches Image 2)
  isStrandActive(s: FlowStrand): boolean {
    const hSec = this.hoveredSector();
    const hSt = this.hoveredStock();

    // If nothing hovered, all strands are softly active
    if (!hSec && !hSt) return true;

    if (s.side === 'left') {
      if (!hSec) return false;
      return s.sourceSector?.toLowerCase() === hSec.toLowerCase();
    } else {
      // Right strand
      if (hSt) {
        return s.stockSymbol?.toUpperCase() === hSt.toUpperCase();
      }
      if (hSec) {
        return s.stockSector?.toLowerCase() === hSec.toLowerCase();
      }
      return false;
    }
  }

  isSectorDimmed(secName: string): boolean {
    const hSec = this.hoveredSector();
    return !!hSec && hSec.toLowerCase() !== secName.toLowerCase();
  }

  onSectorRowClick(sector: string): void {
    if (!sector) return;
    this.sectorSelect.emit(sector);
  }

  isSectorHighlighted(secName: string): boolean {
    const hSec = this.hoveredSector();
    if (hSec) return hSec.toLowerCase() === secName.toLowerCase();
    if (this.activeSector) return this.activeSector.toLowerCase() === secName.toLowerCase();
    return false;
  }

  isStockDimmed(stock: StockFlowItem): boolean {
    const hSt = this.hoveredStock();
    const hSec = this.hoveredSector();

    if (hSt) return hSt.toUpperCase() !== stock.symbol.toUpperCase();
    if (hSec) return !stock.sector || stock.sector.toLowerCase() !== hSec.toLowerCase();
    return false;
  }

  isStockHighlighted(stock: StockFlowItem): boolean {
    const hSt = this.hoveredStock();
    const hSec = this.hoveredSector();

    if (hSt) return hSt.toUpperCase() === stock.symbol.toUpperCase();
    if (hSec) return !!stock.sector && stock.sector.toLowerCase() === hSec.toLowerCase();
    return false;
  }

  getBuildupBadge(buildup: string | null | undefined): BuildupBadge {
    if (!buildup) return { code: 'LB', label: 'Long buildup', cssClass: 'badge-lb' };
    const lower = buildup.toLowerCase();
    if (lower.includes('long buildup')) return { code: 'LB', label: 'Long buildup', cssClass: 'badge-lb' };
    if (lower.includes('short covering')) return { code: 'SC', label: 'Short covering', cssClass: 'badge-sc' };
    if (lower.includes('short buildup')) return { code: 'SB', label: 'Short buildup', cssClass: 'badge-sb' };
    if (lower.includes('long unwinding')) return { code: 'LU', label: 'Long unwinding', cssClass: 'badge-lu' };
    return { code: 'LB', label: 'Long buildup', cssClass: 'badge-lb' };
  }

  getClvBarWidth(clv: number): number {
    const abs = Math.abs(clv);
    return Math.max(4, Math.min(22, Math.round(abs * 22)));
  }

  getAdvancesPercent(adv: number, dec: number): number {
    const tot = adv + dec;
    if (tot <= 0) return 50;
    return Math.round((adv / tot) * 100);
  }

  getDeclinesPercent(adv: number, dec: number): number {
    const tot = adv + dec;
    if (tot <= 0) return 50;
    return Math.round((dec / tot) * 100);
  }

  formatCrores(val: number): string {
    const abs = Math.abs(val);
    const sign = val < 0 ? '-' : '';
    if (abs >= 100000) {
      return `${sign}₹${(abs / 100000).toFixed(1)}L cr`;
    }
    return `${sign}₹${abs.toLocaleString('en-IN', { maximumFractionDigits: 0 })} cr`;
  }

  formatStockFlow(val: number): string {
    const abs = Math.abs(val);
    const sign = val < 0 ? '-' : '+';
    return `${sign}₹${abs.toLocaleString('en-IN', { maximumFractionDigits: 0 })}`;
  }

  MathAbs(val: number): number {
    return Math.abs(val);
  }
}
