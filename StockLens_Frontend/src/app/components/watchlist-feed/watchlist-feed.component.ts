import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';
import { StockNewsService } from '../../services/stock-news.service';
import { WatchlistService, WatchlistStockMeta } from '../../services/watchlist.service';
import { StockAnnouncement, StockAnnouncementResponse } from '../../models/stock-announcement.model';

const FEED_LAST_VISIT_STORAGE_KEY = 'stocklens_watchlist_feed_last_visit';

export interface AnnouncementItemVM extends StockAnnouncement {
  isNewSinceLastVisit: boolean;
  categoryBadgeClass: string;
  categoryLabel: string;
}

export interface DateGroupedFeed {
  dateKey: string;
  dateLabel: string;
  items: AnnouncementItemVM[];
  hasDividerAfterIndex?: number;
}

@Component({
  selector: 'app-watchlist-feed',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './watchlist-feed.component.html',
  styleUrls: ['./watchlist-feed.component.css']
})
export class WatchlistFeedComponent implements OnInit, OnDestroy {
  readonly watchlistService = inject(WatchlistService);
  private readonly newsService = inject(StockNewsService);
  private readonly router = inject(Router);

  // Filter signals
  selectedSymbolFilter = signal<string>('ALL'); // 'ALL' or a specific stock symbol
  searchQuery = signal<string>('');

  // State signals
  loadingState = signal<'loading' | 'success' | 'empty' | 'error'>('loading');
  isRefreshing = signal<boolean>(false);
  isLoadingMore = signal<boolean>(false);
  errorMessage = signal<string | null>(null);
  announcements = signal<StockAnnouncement[]>([]);
  totalCount = signal<number>(0);
  lastSyncedAt = signal<string | null>(null);
  expandedItems = signal<Set<number>>(new Set());

  // Progressive loading (display ~2-3 weeks / 20 items per batch)
  currentPage = signal<number>(1);
  pageSize = signal<number>(20);
  hasMore = signal<boolean>(false);

  // Last visit tracking
  lastVisitTimestamp = signal<number>(this.loadLastVisit());
  feedVisitedAt = Date.now(); // Record current session visit time

  // Search debounce subject
  private searchSubject = new Subject<string>();
  private subs = new Subscription();

  // Followed stocks list from WatchlistService
  followedSymbols = computed(() => this.watchlistService.watchlistSymbols());
  followedCount = computed(() => this.followedSymbols().length);

  // Map of followed stocks metadata
  followedStocksMeta = computed(() => {
    const symbols = this.followedSymbols();
    return symbols.map(sym => {
      const meta = this.watchlistService.getMeta(sym);
      return {
        symbol: sym,
        companyName: meta?.companyName || sym,
        price: meta?.price,
        changePercent: meta?.changePercent,
        isPositive: meta?.isPositive ?? ((meta?.changePercent ?? 0) >= 0)
      };
    });
  });

  // Grouped announcements by Date with "Last Visit" divider detection
  dateGroups = computed<DateGroupedFeed[]>(() => {
    const rawList = this.announcements();
    const query = this.searchQuery().trim().toLowerCase();
    const symFilter = this.selectedSymbolFilter();
    const lastVisit = this.lastVisitTimestamp();

    // 1. Filter
    const filtered = rawList.filter(item => {
      if (symFilter !== 'ALL' && item.symbol.toUpperCase() !== symFilter.toUpperCase()) {
        return false;
      }
      if (query) {
        const inSym = item.symbol.toLowerCase().includes(query);
        const inName = item.companyName?.toLowerCase().includes(query);
        const inSub = item.subject?.toLowerCase().includes(query);
        const inDet = item.details?.toLowerCase().includes(query);
        if (!inSym && !inName && !inSub && !inDet) return false;
      }
      return true;
    });

    if (filtered.length === 0) return [];

    // 2. Map to View Model
    const vmList: AnnouncementItemVM[] = filtered.map(item => {
      const itemTime = item.broadcastDateTime ? new Date(item.broadcastDateTime).getTime() : new Date(item.createdAt).getTime();
      const isNew = lastVisit > 0 && itemTime > lastVisit;
      const { badgeClass, label } = this.categorizeSubject(item.subject);

      return {
        ...item,
        isNewSinceLastVisit: isNew,
        categoryBadgeClass: badgeClass,
        categoryLabel: label
      };
    });

    // 3. Group by Date
    const groupsMap = new Map<string, { label: string; items: AnnouncementItemVM[] }>();
    const now = new Date();
    const todayStr = this.toDateKey(now);
    const yesterday = new Date(now.getTime() - 86400000);
    const yesterdayStr = this.toDateKey(yesterday);

    for (const item of vmList) {
      const dateObj = item.broadcastDateTime ? new Date(item.broadcastDateTime) : new Date(item.createdAt);
      const key = this.toDateKey(dateObj);

      if (!groupsMap.has(key)) {
        let label = this.formatDateLabel(dateObj);
        if (key === todayStr) label = 'TODAY';
        else if (key === yesterdayStr) label = 'YESTERDAY';

        groupsMap.set(key, { label, items: [] });
      }
      groupsMap.get(key)!.items.push(item);
    }

    // Convert map to array
    return Array.from(groupsMap.entries()).map(([dateKey, group]) => ({
      dateKey,
      dateLabel: group.label,
      items: group.items
    }));
  });

  // Count of announcements newer than last visit
  newCountSinceLastVisit = computed(() => {
    const lastVisit = this.lastVisitTimestamp();
    if (lastVisit <= 0) return 0;
    return this.announcements().filter(item => {
      const itemTime = item.broadcastDateTime ? new Date(item.broadcastDateTime).getTime() : new Date(item.createdAt).getTime();
      return itemTime > lastVisit;
    }).length;
  });

  ngOnInit(): void {
    this.subs.add(
      this.searchSubject.pipe(
        debounceTime(300),
        distinctUntilChanged()
      ).subscribe(term => {
        this.searchQuery.set(term);
        this.loadFeed(false, false);
      })
    );

    this.loadFeed();
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
    // Persist session visit time when navigating away
    this.saveLastVisit(this.feedVisitedAt);
  }

  loadFeed(isRefresh = false, append = false): void {
    const symbols = this.followedSymbols();

    if (symbols.length === 0) {
      this.loadingState.set('empty');
      this.announcements.set([]);
      this.totalCount.set(0);
      this.hasMore.set(false);
      return;
    }

    if (isRefresh) {
      this.isRefreshing.set(true);
      this.currentPage.set(1);
    } else if (append) {
      this.isLoadingMore.set(true);
    } else {
      this.loadingState.set('loading');
      this.currentPage.set(1);
    }
    this.errorMessage.set(null);

    const page = this.currentPage();
    const limit = this.pageSize();
    const symFilter = this.selectedSymbolFilter() !== 'ALL' ? this.selectedSymbolFilter() : undefined;
    const searchFilter = this.searchQuery().trim() || undefined;

    this.newsService.getCorporateAnnouncements(
      symFilter,
      searchFilter,
      undefined,
      limit,
      page,
      symFilter ? undefined : symbols // pass all followed symbols if no single symbol filter is selected
    ).subscribe({
      next: (res: StockAnnouncementResponse) => {
        const incoming = res.announcements || [];
        if (append) {
          this.announcements.update(prev => [...prev, ...incoming]);
        } else {
          this.announcements.set(incoming);
        }

        this.totalCount.set(res.totalCount || 0);
        this.hasMore.set(page * limit < (res.totalCount || 0));

        if (res.lastSyncedAt) {
          const syncDate = new Date(res.lastSyncedAt);
          this.lastSyncedAt.set(this.formatTime(syncDate));
        }

        this.loadingState.set(this.announcements().length > 0 || append ? 'success' : 'empty');
        this.isRefreshing.set(false);
        this.isLoadingMore.set(false);
      },
      error: (err) => {
        console.error('Failed to load watchlist announcements:', err);
        if (this.announcements().length === 0) {
          this.errorMessage.set('Unable to load corporate announcements. Please check your connection.');
          this.loadingState.set('error');
        }
        this.isRefreshing.set(false);
        this.isLoadingMore.set(false);
      }
    });
  }

  loadMore(): void {
    if (this.isLoadingMore() || !this.hasMore()) return;
    this.currentPage.update(p => p + 1);
    this.loadFeed(false, true);
  }

  triggerRefresh(): void {
    if (this.isRefreshing()) return;
    this.isRefreshing.set(true);

    this.newsService.triggerAnnouncementSync().subscribe({
      next: () => {
        this.loadFeed(true);
      },
      error: () => {
        // Fallback to loading existing feed
        this.loadFeed(true);
      }
    });
  }

  markAllAsSeen(): void {
    const now = Date.now();
    this.lastVisitTimestamp.set(now);
    this.saveLastVisit(now);
  }

  setSymbolFilter(symbol: string): void {
    if (this.selectedSymbolFilter() === symbol) return;
    this.selectedSymbolFilter.set(symbol);
    this.loadFeed(false, false);
  }

  onSearchChange(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchSubject.next(val);
  }

  clearSearch(): void {
    this.searchQuery.set('');
    this.loadFeed(false, false);
  }

  toggleExpand(id: number): void {
    const set = new Set(this.expandedItems());
    if (set.has(id)) {
      set.delete(id);
    } else {
      set.add(id);
    }
    this.expandedItems.set(set);
  }

  isExpanded(id: number): boolean {
    return this.expandedItems().has(id);
  }

  navigateToStock(symbol: string): void {
    if (symbol) {
      this.router.navigate(['/stock', symbol.toUpperCase()]);
    }
  }

  navigateToNifty(): void {
    this.router.navigate(['/nifty50']);
  }

  openPdf(url?: string): void {
    if (url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  }

  private loadLastVisit(): number {
    try {
      const val = localStorage.getItem(FEED_LAST_VISIT_STORAGE_KEY);
      return val ? parseInt(val, 10) : 0;
    } catch {
      return 0;
    }
  }

  private saveLastVisit(time: number): void {
    try {
      localStorage.setItem(FEED_LAST_VISIT_STORAGE_KEY, time.toString());
    } catch {
      // ignore
    }
  }

  private toDateKey(d: Date): string {
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }

  private formatDateLabel(d: Date): string {
    const day = d.getDate();
    const months = ['JANUARY', 'FEBRUARY', 'MARCH', 'APRIL', 'MAY', 'JUNE', 'JULY', 'AUGUST', 'SEPTEMBER', 'OCTOBER', 'NOVEMBER', 'DECEMBER'];
    const month = months[d.getMonth()];
    const year = d.getFullYear();
    return `${day} ${month} ${year}`;
  }

  private formatTime(d: Date): string {
    return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  }

  private categorizeSubject(subject?: string): { badgeClass: string; label: string } {
    if (!subject) return { badgeClass: 'badge-general', label: 'Disclosure' };
    const s = subject.toLowerCase();

    if (s.includes('regulation 30') || s.includes('reg 30') || s.includes('lodr')) {
      return { badgeClass: 'badge-reg30', label: 'SEBI Reg 30' };
    }
    if (s.includes('financial result') || s.includes('quarterly') || s.includes('audited')) {
      return { badgeClass: 'badge-results', label: 'Financial Results' };
    }
    if (s.includes('board meeting') || s.includes('dividend') || s.includes('bonus')) {
      return { badgeClass: 'badge-board', label: 'Board Meeting / Dividend' };
    }
    if (s.includes('press release') || s.includes('media release')) {
      return { badgeClass: 'badge-press', label: 'Press Release' };
    }
    if (s.includes('investor') || s.includes('analyst') || s.includes('conference') || s.includes('presentation')) {
      return { badgeClass: 'badge-investor', label: 'Investor Meet' };
    }
    if (s.includes('insider') || s.includes('sast') || s.includes('pit')) {
      return { badgeClass: 'badge-insider', label: 'Insider / SAST' };
    }

    return { badgeClass: 'badge-general', label: 'General Announcement' };
  }
}
