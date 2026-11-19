import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { StockNewsService } from '../../services/stock-news.service';
import { WatchlistService } from '../../services/watchlist.service';
import { StockAnnouncement, StockAnnouncementResponse } from '../../models/stock-announcement.model';
import { LoadingState } from '../../models/stock-news.model';

@Component({
  selector: 'app-corporate-announcements',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './corporate-announcements.component.html',
  styleUrls: ['./corporate-announcements.component.css']
})
export class CorporateAnnouncementsComponent implements OnInit {
  private readonly newsService = inject(StockNewsService);
  readonly watchlistService = inject(WatchlistService);
  private readonly router = inject(Router);

  // State signals
  announcements = signal<StockAnnouncement[]>([]);
  totalCount = signal<number>(0);
  loadingState = signal<LoadingState>('loading');
  isRefreshing = signal<boolean>(false);
  errorMessage = signal<string>('');
  lastUpdated = signal<string>('');
  feedScope = signal<'all' | 'watchlist'>('all');
  
  // Filter state
  selectedCategory = signal<string>('All');
  searchQuery = signal<string>('');
  currentPage = signal<number>(1);
  pageSize = signal<number>(25);
  hasMore = signal<boolean>(false);

  // Category filters
  categories = signal<string[]>([
    'All',
    'Financial Results',
    'Board Meeting',
    'Updates',
    'Trading Window',
    'Loss of Share Certificates',
    'General'
  ]);

  // Expanded announcement details set
  expandedIds = signal<Set<number>>(new Set<number>());

  // Filtered computed list (client-side fine filter on top of backend query)
  filteredAnnouncements = computed(() => {
    const list = this.announcements();
    const query = this.searchQuery().trim().toLowerCase();
    const cat = this.selectedCategory();

    return list.filter(item => {
      const matchCat = cat === 'All' || (item.subject && item.subject.toLowerCase().includes(cat.toLowerCase()));
      if (!matchCat) return false;

      if (!query) return true;
      return (
        item.symbol.toLowerCase().includes(query) ||
        item.companyName.toLowerCase().includes(query) ||
        (item.subject && item.subject.toLowerCase().includes(query)) ||
        (item.details && item.details.toLowerCase().includes(query))
      );
    });
  });

  ngOnInit(): void {
    this.loadCategories();
    this.loadAnnouncements();
  }

  loadCategories(): void {
    this.newsService.getAnnouncementCategories().subscribe({
      next: (cats) => {
        if (cats && cats.length > 0) {
          const defaults = ['All', 'Financial Results', 'Board Meeting', 'Updates', 'Trading Window'];
          const combined = Array.from(new Set([...defaults, ...cats.slice(0, 15)]));
          this.categories.set(combined);
        }
      },
      error: (err) => console.warn('Could not fetch distinct categories:', err)
    });
  }

  setScope(scope: 'all' | 'watchlist'): void {
    if (this.feedScope() === scope) return;
    this.feedScope.set(scope);
    this.loadAnnouncements(true);
  }

  loadAnnouncements(isRefresh = false, append = false): void {
    if (isRefresh) {
      this.isRefreshing.set(true);
      this.currentPage.set(1);
    } else if (!append) {
      this.loadingState.set('loading');
      this.currentPage.set(1);
    }

    const page = this.currentPage();
    const limit = this.pageSize();
    const cat = this.selectedCategory() !== 'All' ? this.selectedCategory() : undefined;
    const query = this.searchQuery().trim() || undefined;

    let symbols: string[] | undefined = undefined;
    if (this.feedScope() === 'watchlist') {
      const tracked = this.watchlistService.watchlistSymbols();
      if (tracked.length === 0) {
        this.announcements.set([]);
        this.totalCount.set(0);
        this.loadingState.set('empty');
        this.isRefreshing.set(false);
        return;
      }
      symbols = tracked;
    }

    this.newsService.getCorporateAnnouncements(undefined, query, cat, limit, page, symbols).subscribe({
      next: (response: StockAnnouncementResponse) => {
        if (response && response.announcements) {
          if (append) {
            this.announcements.update(prev => [...prev, ...response.announcements]);
          } else {
            this.announcements.set(response.announcements);
          }

          this.totalCount.set(response.totalCount);
          this.hasMore.set(page * limit < response.totalCount);
          this.loadingState.set(response.announcements.length > 0 || append ? 'success' : 'empty');

          if (response.lastSyncedAt) {
            const dt = new Date(response.lastSyncedAt);
            this.lastUpdated.set(this.formatRelativeTime(dt));
          } else {
            this.lastUpdated.set('Recently synced');
          }
        } else {
          if (!append) {
            this.announcements.set([]);
            this.loadingState.set('empty');
          }
        }
        this.isRefreshing.set(false);
      },
      error: (err) => {
        console.error('Failed to load corporate announcements:', err);
        if (this.announcements().length === 0) {
          this.loadingState.set('error');
          this.errorMessage.set('Unable to load corporate announcements from database.');
        }
        this.isRefreshing.set(false);
      }
    });
  }

  loadMore(): void {
    this.currentPage.update(p => p + 1);
    this.loadAnnouncements(false, true);
  }

  setCategory(category: string): void {
    this.selectedCategory.set(category);
    this.loadAnnouncements();
  }

  onSearchSubmit(): void {
    this.loadAnnouncements();
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.loadAnnouncements();
  }

  triggerRefresh(): void {
    this.isRefreshing.set(true);
    // Request backend to fetch latest feed from NSE, then reload local announcements
    this.newsService.triggerAnnouncementSync().subscribe({
      next: (res) => {
        console.log('NSE Announcements sync completed:', res);
        this.loadAnnouncements(true);
      },
      error: (err) => {
        console.warn('Sync request failed, refreshing local DB view:', err);
        this.loadAnnouncements(true);
      }
    });
  }

  toggleExpand(id: number): void {
    this.expandedIds.update(set => {
      const next = new Set(set);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  isExpanded(id: number): boolean {
    return this.expandedIds().has(id);
  }

  openPdf(url?: string): void {
    if (!url) return;
    window.open(url, '_blank', 'noopener,noreferrer');
  }

  navigateToStock(symbol: string): void {
    if (!symbol) return;
    this.router.navigate(['/stock', symbol.toUpperCase()]);
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
