import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockShareholdingResponse, ShareholdingPeriod } from '../../models/stock-shareholding.model';

@Component({
  selector: 'app-stock-shareholding-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-shareholding-card.component.html',
  styleUrl: './stock-shareholding-card.component.css'
})
export class StockShareholdingCardComponent {
  @Input() shareholding: StockShareholdingResponse | null = null;
  @Input() loadingState: 'idle' | 'loading' | 'success' | 'empty' | 'error' = 'idle';
  @Input() errorMessage: string = '';
  @Input() isRefreshing: boolean = false;

  @Output() refreshRequested = new EventEmitter<void>();

  selectedHistoricalPeriod = signal<ShareholdingPeriod | null>(null);

  get activePeriod(): ShareholdingPeriod | null {
    return this.selectedHistoricalPeriod() || this.shareholding?.currentPeriod || null;
  }

  get displayedHistory(): ShareholdingPeriod[] {
    return this.shareholding?.history?.slice(0, 3) || [];
  }

  selectPeriod(period: ShareholdingPeriod | null): void {
    this.selectedHistoricalPeriod.set(period);
  }

  onRefreshClick(): void {
    this.selectedHistoricalPeriod.set(null);
    this.refreshRequested.emit();
  }

  getChangeClass(val: number | null | undefined): 'positive' | 'negative' | 'neutral' {
    if (val === null || val === undefined || val === 0) return 'neutral';
    return val > 0 ? 'positive' : 'negative';
  }

  getArrow(val: number | null | undefined): string {
    if (val === null || val === undefined || val === 0) return '—';
    return val > 0 ? '↑' : '↓';
  }

  formatPp(val: number | null | undefined): string {
    if (val === null || val === undefined) return '—';
    const sign = val > 0 ? '+' : '';
    return `${sign}${val.toFixed(2)}%`;
  }

  formatHolding(val: number | null | undefined): string {
    if (val === null || val === undefined) return '—';
    return `${val.toFixed(2)}%`;
  }

  formatRelative(val: number | null | undefined): string {
    if (val === null || val === undefined) return '';
    const sign = val > 0 ? '+' : '';
    return `${sign}${val.toFixed(2)}% relative`;
  }

  formatCount(val: number | null | undefined): string {
    if (val === null || val === undefined) return '—';
    return val.toLocaleString('en-IN');
  }
}
