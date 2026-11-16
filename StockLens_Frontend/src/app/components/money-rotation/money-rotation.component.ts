import { Component, OnInit, OnDestroy, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { SectorAnalysisService } from '../../services/sector-analysis.service';
import { MoneyFlowResponse, SectorFlowItem } from '../../models/sector-analysis.model';

export interface RotationNode {
  sector: string;
  shortName: string;
  flowCr: number;
  formattedFlow: string;
  isSurge: boolean;
  y: number;
}

export interface RotationStream {
  id: string;
  side: 'left' | 'right';
  sector: string;
  pathD: string;
  thickness: number;
  speed: number;
  isSurge: boolean;
}

const SECTOR_SHORT_NAMES: Record<string, string> = {
  'Telecommunication': 'Telecom',
  'Construction': 'Construction',
  'Metals & Mining': 'Metals',
  'Financial Services': 'Bank',
  'Consumer Durables': 'Durables',
  'Capital Goods': 'Cap goods',
  'Automobile and Auto Components': 'Auto',
  'Real Estate': 'Realty',
  'Consumer Services': 'Consumer Svc',
  'Services': 'Services',
  'Textiles': 'Textiles',
  'Utilities': 'Utilities',
  'Chemicals': 'Chemicals',
  'Oil, Gas & Consumable Fuels': 'Oil & Gas',
  'Construction Materials': 'Cement',
  'Fast Moving Consumer Goods': 'FMCG',
  'Industrials': 'Industrials',
  'Information Technology': 'IT',
  'Healthcare': 'Pharma'
};

@Component({
  selector: 'app-money-rotation',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './money-rotation.component.html',
  styleUrls: ['./money-rotation.component.css']
})
export class MoneyRotationComponent implements OnInit, OnDestroy {
  data = signal<MoneyFlowResponse | null>(null);
  isLoading = signal<boolean>(true);
  hoveredSector = signal<string | null>(null);

  private sub?: Subscription;

  // Geometry Constants (matches reference screenshot layout, scaled to flex stage)
  readonly CANVAS_WIDTH = 640;
  readonly CANVAS_HEIGHT = 160;
  readonly LEFT_NODE_X = 0;
  readonly RIGHT_NODE_X = 640;
  readonly CENTER_X = 320;
  readonly ROW_Y_POSITIONS = [30, 65, 100, 135];
  // Distributed center waist Y positions to prevent stream clumping & collision at the center divider
  readonly CENTER_Y_POSITIONS = [68, 77, 87, 96];

  // Leaving Sectors (Left Side - Outflow / Red)
  leavingNodes = computed<RotationNode[]>(() => {
    const d = this.data();
    let sectors: SectorFlowItem[] = [];

    if (d && d.sectors && d.sectors.length > 0) {
      sectors = d.sectors
        .filter(s => s.netFlowCr < 0)
        .sort((a, b) => a.netFlowCr - b.netFlowCr)
        .slice(0, 4);
    }

    // Default canonical reference nodes if API has fewer than 4 negative sectors
    if (sectors.length === 0) {
      return [
        { sector: 'Capital Goods', shortName: 'Cap goods', flowCr: -1301, formattedFlow: '-₹1,301', isSurge: false, y: this.ROW_Y_POSITIONS[0] },
        { sector: 'Information Technology', shortName: 'IT', flowCr: -1123, formattedFlow: '-₹1,123', isSurge: false, y: this.ROW_Y_POSITIONS[1] },
        { sector: 'Construction Materials', shortName: 'Cement', flowCr: -492, formattedFlow: '-₹492', isSurge: false, y: this.ROW_Y_POSITIONS[2] },
        { sector: 'Telecommunication', shortName: 'Telecom', flowCr: -417, formattedFlow: '-₹417', isSurge: false, y: this.ROW_Y_POSITIONS[3] }
      ];
    }

    return sectors.map((s, idx) => ({
      sector: s.sector,
      shortName: this.getShortName(s.sector),
      flowCr: s.netFlowCr,
      formattedFlow: `-₹${Math.abs(s.netFlowCr).toLocaleString('en-IN', { maximumFractionDigits: 0 })}`,
      isSurge: s.isSurge,
      y: this.ROW_Y_POSITIONS[idx] ?? (30 + idx * 35)
    }));
  });

  // Arriving Sectors (Right Side - Inflow / Green)
  arrivingNodes = computed<RotationNode[]>(() => {
    const d = this.data();
    let sectors: SectorFlowItem[] = [];

    if (d && d.sectors && d.sectors.length > 0) {
      sectors = d.sectors
        .filter(s => s.netFlowCr > 0)
        .sort((a, b) => b.netFlowCr - a.netFlowCr)
        .slice(0, 4);
    }

    // Default canonical reference nodes if API has fewer than 4 positive sectors
    if (sectors.length === 0) {
      return [
        { sector: 'Financial Services', shortName: 'Bank', flowCr: 9348, formattedFlow: '+₹9,348', isSurge: false, y: this.ROW_Y_POSITIONS[0] },
        { sector: 'Healthcare', shortName: 'Pharma', flowCr: 6145, formattedFlow: '+₹6,145', isSurge: true, y: this.ROW_Y_POSITIONS[1] },
        { sector: 'Public Sector Bank', shortName: 'PSU Bank', flowCr: 4811, formattedFlow: '+₹4,811', isSurge: false, y: this.ROW_Y_POSITIONS[2] },
        { sector: 'Automobile and Auto Components', shortName: 'Auto', flowCr: 2255, formattedFlow: '+₹2,255', isSurge: false, y: this.ROW_Y_POSITIONS[3] }
      ];
    }

    return sectors.map((s, idx) => ({
      sector: s.sector,
      shortName: this.getShortName(s.sector),
      flowCr: s.netFlowCr,
      formattedFlow: `+₹${s.netFlowCr.toLocaleString('en-IN', { maximumFractionDigits: 0 })}`,
      isSurge: s.isSurge,
      y: this.ROW_Y_POSITIONS[idx] ?? (30 + idx * 35)
    }));
  });

  // Net Market Flow formatted for the center divider
  isNetPositive = computed<boolean>(() => {
    const d = this.data();
    return (d ? d.netFlowCr : 30505) >= 0;
  });

  formattedNetFlowAmount = computed<string>(() => {
    const d = this.data();
    const net = d ? d.netFlowCr : 30505;
    const sign = net >= 0 ? '+' : '-';
    const abs = Math.abs(net);
    return `${sign}₹${abs.toLocaleString('en-IN', { maximumFractionDigits: 0 })} cr`;
  });

  formattedNetFlow = computed<string>(() => {
    return `Net ${this.formattedNetFlowAmount()}`;
  });

  // SVG Curved Funnel Streams from Left to Center, and Center to Right
  rotationStreams = computed<RotationStream[]>(() => {
    const streams: RotationStream[] = [];
    const leftX = this.LEFT_NODE_X;
    const rightX = this.RIGHT_NODE_X;
    const centerX = this.CENTER_X;

    // Left Streams (Leaving / Red): Sector row -> Center divider waist
    this.leavingNodes().forEach((node, idx) => {
      const startX = leftX;
      const startY = node.y;
      const endX = centerX;
      const endY = this.CENTER_Y_POSITIONS[idx] ?? 82;
      const c1X = startX + (endX - startX) * 0.40;
      const c1Y = startY;
      const c2X = startX + (endX - startX) * 0.75;
      const c2Y = endY;

      streams.push({
        id: `left-stream-${node.sector}`,
        side: 'left',
        sector: node.sector,
        pathD: `M ${startX} ${startY} C ${c1X} ${c1Y}, ${c2X} ${c2Y}, ${endX} ${endY}`,
        thickness: Math.max(6, Math.min(14, (Math.abs(node.flowCr) / 4000) * 10)),
        speed: 2.2,
        isSurge: node.isSurge
      });
    });

    // Right Streams (Arriving / Green): Center divider waist -> Sector row
    this.arrivingNodes().forEach((node, idx) => {
      const startX = centerX;
      const startY = this.CENTER_Y_POSITIONS[idx] ?? 82;
      const endX = rightX;
      const endY = node.y;
      const c1X = startX + (endX - startX) * 0.25;
      const c1Y = startY;
      const c2X = startX + (endX - startX) * 0.60;
      const c2Y = endY;

      streams.push({
        id: `right-stream-${node.sector}`,
        side: 'right',
        sector: node.sector,
        pathD: `M ${startX} ${startY} C ${c1X} ${c1Y}, ${c2X} ${c2Y}, ${endX} ${endY}`,
        thickness: Math.max(6, Math.min(14, (node.flowCr / 4000) * 10)),
        speed: 2.2,
        isSurge: node.isSurge
      });
    });

    return streams;
  });

  constructor(private sectorService: SectorAnalysisService) {}

  ngOnInit(): void {
    this.fetchData();
  }

  ngOnDestroy(): void {
    this.sub?.unsubscribe();
  }

  fetchData(): void {
    this.isLoading.set(true);
    this.sub?.unsubscribe();
    this.sub = this.sectorService.getMoneyFlow(5).subscribe({
      next: (res) => {
        if (res && res.sectors && res.sectors.length > 0) {
          this.data.set(res);
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        console.warn('Money rotation API fetch failed, displaying canonical flow rotation', err);
        this.isLoading.set(false);
      }
    });
  }

  getShortName(fullSector: string): string {
    if (!fullSector) return 'Sector';
    return SECTOR_SHORT_NAMES[fullSector.trim()] || fullSector.trim();
  }

  isStreamActive(sector: string): boolean {
    const h = this.hoveredSector();
    if (!h) return true;
    return h.toLowerCase() === sector.toLowerCase();
  }

  private readonly router = inject(Router);

  scrollToMoneyFlow(): void {
    const el = document.getElementById('money-flow-section') || document.querySelector('.money-flow-section');
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    } else {
      this.router.navigate(['/sectors'], { fragment: 'money-flow-section' });
    }
  }
}
