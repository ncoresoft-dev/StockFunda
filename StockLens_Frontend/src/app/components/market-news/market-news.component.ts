import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StockNewsService } from '../../services/stock-news.service';
import { MarketNewsItem, MarketNewsResponse, LoadingState } from '../../models/stock-news.model';

@Component({
  selector: 'app-market-news',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './market-news.component.html',
  styleUrls: ['./market-news.component.css']
})
export class MarketNewsComponent implements OnInit {
  private readonly newsService = inject(StockNewsService);

  // State signals
  allNews = signal<MarketNewsItem[]>([]);
  loadingState = signal<LoadingState>('loading');
  isRefreshing = signal<boolean>(false);
  errorMessage = signal<string>('');
  lastUpdated = signal<string>('');
  filterQuery = signal<string>('');

  // Filtered news items computed signal
  filteredNews = computed(() => {
    let list = this.allNews();
    const query = this.filterQuery().trim().toLowerCase();

    if (query) {
      list = list.filter(item =>
        item.title.toLowerCase().includes(query) ||
        (item.description && item.description.toLowerCase().includes(query)) ||
        item.sourceName.toLowerCase().includes(query)
      );
    }

    return list;
  });

  // Top featured headline
  featuredArticle = computed(() => {
    const list = this.filteredNews();
    return list.length > 0 ? list[0] : null;
  });

  // Remaining articles
  feedArticles = computed(() => {
    const list = this.filteredNews();
    return list.length > 1 ? list.slice(1) : (list.length === 1 ? list : []);
  });

  ngOnInit(): void {
    this.loadNews();
  }

  loadNews(isUserRefresh = false): void {
    if (isUserRefresh) {
      this.isRefreshing.set(true);
    } else {
      this.loadingState.set('loading');
    }

    this.newsService.getMarketNews(30).subscribe({
      next: (response: MarketNewsResponse) => {
        if (response && response.news && response.news.length > 0) {
          this.allNews.set(response.news);
          this.loadingState.set('success');

          if (response.lastFetchedAt) {
            const dt = new Date(response.lastFetchedAt);
            this.lastUpdated.set(this.formatRelativeTime(dt));
          } else {
            this.lastUpdated.set('Just now');
          }
        } else {
          this.allNews.set([]);
          this.loadingState.set('empty');
        }
        this.isRefreshing.set(false);
      },
      error: (err) => {
        console.error('Failed to load market news:', err);
        // If we already have items in memory, keep showing them
        if (this.allNews().length === 0) {
          this.loadingState.set('error');
          this.errorMessage.set('Unable to load market news at this moment.');
        }
        this.isRefreshing.set(false);
      }
    });
  }

  openArticle(url?: string): void {
    if (!url) return;
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  private formatRelativeTime(date: Date): string {
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffSec = Math.floor(diffMs / 1000);
    const diffMin = Math.floor(diffSec / 60);
    const diffHours = Math.floor(diffMin / 60);

    if (diffMin < 1) return 'Just now';
    if (diffMin < 60) return `${diffMin}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    return `${Math.floor(diffHours / 24)}d ago`;
  }
}
