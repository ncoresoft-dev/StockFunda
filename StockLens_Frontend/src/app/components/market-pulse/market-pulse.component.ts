import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { MarketTickerService } from '../../services/market-ticker.service';
import { StockNewsService } from '../../services/stock-news.service';
import { MarketPulseResponse, MarketPulseIndex } from '../../models/market-ticker.model';
import { MarketNewsItem, MarketNewsResponse } from '../../models/stock-news.model';

@Component({
  selector: 'app-market-pulse',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './market-pulse.component.html',
  styleUrls: ['./market-pulse.component.css']
})
export class MarketPulseComponent implements OnInit {
  private readonly tickerService = inject(MarketTickerService);
  private readonly newsService = inject(StockNewsService);
  private readonly router = inject(Router);

  readonly timeframes: Array<'1D' | '1W' | '1M'> = ['1D', '1W', '1M'];
  selectedTimeframe = signal<'1D' | '1W' | '1M'>('1D');
  isLoading = signal<boolean>(false);
  errorMessage = signal<string | null>(null);
  pulseData = signal<MarketPulseResponse | null>(null);

  // Live Market News Feed (Option 2 - 30% Column)
  latestNews = signal<MarketNewsItem[]>([]);
  isNewsLoading = signal<boolean>(false);

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
    this.loadLatestNews();
  }

  loadLatestNews(): void {
    this.isNewsLoading.set(true);
    this.newsService.getMarketNews(5).subscribe({
      next: (res: MarketNewsResponse) => {
        if (res && res.news && res.news.length > 0) {
          this.latestNews.set(res.news.slice(0, 5));
        } else {
          this.latestNews.set([]);
        }
        this.isNewsLoading.set(false);
      },
      error: (err) => {
        console.error('Failed to load market news for indices feed:', err);
        this.latestNews.set([]);
        this.isNewsLoading.set(false);
      }
    });
  }

  navigateToNewsHub(): void {
    this.router.navigate(['/news']);
  }

  openArticle(url?: string): void {
    if (!url) {
      this.navigateToNewsHub();
      return;
    }
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  setTimeframe(tf: '1D' | '1W' | '1M'): void {
    if (this.selectedTimeframe() === tf) return;
    this.selectedTimeframe.set(tf);
    this.loadPulseData();
  }

  loadPulseData(forceRefresh = false): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    this.tickerService.getMarketPulse(this.selectedTimeframe(), forceRefresh).subscribe({
      next: (data) => {
        if (data && data.indices && data.indices.length > 0) {
          this.pulseData.set(data);
          this.errorMessage.set(null);
        } else {
          this.pulseData.set(null);
          this.errorMessage.set('Market indices data is currently unavailable.');
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Failed to load market pulse data:', err);
        this.pulseData.set(null);
        const msg = err?.error?.message || err?.message || 'Failed to load live market indices data. Please check your connection and retry.';
        this.errorMessage.set(msg);
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
    return item.takeawayNote || '';
  }
}
