import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { MarketTickerService } from '../../services/market-ticker.service';
import { MarketPulseResponse, MarketPulseIndex } from '../../models/market-ticker.model';

@Component({
  selector: 'app-market-pulse',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './market-pulse.component.html',
  styleUrls: ['./market-pulse.component.css']
})
export class MarketPulseComponent implements OnInit {
  private readonly tickerService = inject(MarketTickerService);
  private readonly router = inject(Router);

  readonly timeframes: Array<'1D' | '1W' | '1M'> = ['1D', '1W', '1M'];
  selectedTimeframe = signal<'1D' | '1W' | '1M'>('1D');
  isLoading = signal<boolean>(false);
  pulseData = signal<MarketPulseResponse | null>(null);

  indices = computed<MarketPulseIndex[]>(() => this.pulseData()?.indices ?? []);

  // Aperture Ring Gauge Math (Center: 120, 102 | Radius: 82 | 250deg sweep | Zoomed)
  readonly gaugeArcLength = 357.8;

  getStrokeDashoffset(item: MarketPulseIndex): number {
    const pct = Math.max(0, Math.min(100, item.rangeProgressPct || 50));
    return this.gaugeArcLength * (1 - pct / 100);
  }

  getMarkerX(item: MarketPulseIndex): number {
    const pct = Math.max(0, Math.min(100, item.rangeProgressPct || 50));
    const angleDeg = 145 + (pct / 100) * 250;
    const rad = (angleDeg * Math.PI) / 180;
    return Math.round((120 + 82 * Math.cos(rad)) * 100) / 100;
  }

  getMarkerY(item: MarketPulseIndex): number {
    const pct = Math.max(0, Math.min(100, item.rangeProgressPct || 50));
    const angleDeg = 145 + (pct / 100) * 250;
    const rad = (angleDeg * Math.PI) / 180;
    return Math.round((102 + 82 * Math.sin(rad)) * 100) / 100;
  }

  ngOnInit(): void {
    this.loadPulseData();
  }

  setTimeframe(tf: '1D' | '1W' | '1M'): void {
    if (this.selectedTimeframe() === tf) return;
    this.selectedTimeframe.set(tf);
    this.loadPulseData();
  }

  loadPulseData(forceRefresh = false): void {
    this.isLoading.set(true);
    this.tickerService.getMarketPulse(this.selectedTimeframe(), forceRefresh).subscribe({
      next: (data) => {
        this.pulseData.set(data);
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Failed to load market pulse data:', err);
        this.isLoading.set(false);
      }
    });
  }

  navigateToSectorMap(): void {
    this.router.navigate(['/sector-analysis']);
  }

  onIndexCardClick(index: MarketPulseIndex): void {
    const sym = (index.symbol || '').toUpperCase();
    const name = (index.name || '').toLowerCase();

    if (sym === '^NSEBANK' || name.includes('bank')) {
      this.router.navigate(['/nifty50'], { queryParams: { tab: 'banknifty' } });
    } else if (sym === '^BSESN' || name.includes('sensex')) {
      this.router.navigate(['/nifty50'], { queryParams: { tab: 'sensex' } });
    } else {
      this.router.navigate(['/nifty50'], { queryParams: { tab: 'nifty50' } });
    }
  }

  getDefaultNote(item: MarketPulseIndex): string {
    const sym = (item.symbol || '').toUpperCase();
    const isUp = item.isPositive ?? (item.change >= 0);
    const tf = this.selectedTimeframe();

    if (tf === '1W') {
      return isUp 
        ? 'Weekly momentum positive; steady accumulation in large caps.' 
        : 'Weekly consolidation after prior rally.';
    }
    if (tf === '1M') {
      return isUp 
        ? 'Monthly trend strongly bullish; benchmarks up over 3%.' 
        : 'Monthly range-bound trading with selective stock picking.';
    }

    if (sym.includes('BANK')) {
      return isUp 
        ? 'Banking leads rally; private banks showing strong inflows.' 
        : 'Private banks weak; PSU banks holding.';
    }
    if (sym.includes('BSESN') || (item.name || '').toLowerCase().includes('sensex')) {
      return isUp 
        ? 'Large caps drive the move; BSE breadth positive.' 
        : 'Large cap drag; profit booking across heavyweights.';
    }
    return isUp 
      ? 'IT and auto lead; 36 of 50 stocks up.' 
      : 'Broad market cautious; 18 of 50 stocks up.';
  }
}
