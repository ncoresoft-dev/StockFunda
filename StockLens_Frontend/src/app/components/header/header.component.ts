import {
  Component,
  Input,
  Output,
  EventEmitter,
  OnInit,
  OnDestroy,
  inject,
  signal,
  computed,
  ViewChild,
  ElementRef,
  HostListener
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { Subject, Subscription, of } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, catchError, map } from 'rxjs/operators';
import { StockNewsService } from '../../services/stock-news.service';
import { WatchlistService } from '../../services/watchlist.service';
import { Company, Stock } from '../../models/stock-news.model';
import { AdminNotificationBellComponent } from '../admin-notification-bell/admin-notification-bell.component';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterModule, AdminNotificationBellComponent],
  templateUrl: './header.component.html',
  styleUrls: ['./header.component.css'],
  host: {
    '[class.is-sticky]': 'isSticky',
    '[style.position]': "isSticky ? 'sticky' : 'relative'",
    '[style.top]': "isSticky ? '0' : 'auto'",
    '[style.z-index]': "'1000'",
    '[style.display]': "'block'",
    '[style.width]': "'100%'",
    '[style.flex-shrink]': "'0'"
  }
})
export class HeaderComponent implements OnInit, OnDestroy {
  protected readonly router = inject(Router);
  protected readonly newsService = inject(StockNewsService);
  readonly watchlistService = inject(WatchlistService);

  // Configuration Inputs
  @Input() variant: 'cockpit' | 'transparent' | 'flat' = 'cockpit';
  @Input() isSticky: boolean = true;
  @Input() theme: 'auto' | 'light' | 'dark' = 'auto';

  // Brand / Title Inputs
  @Input() tagline: string = '';
  @Input() pageTag: string = '';

  // Search Inputs & Options
  @Input() showSearch: boolean = true;
  @Input() searchPlaceholder: string = 'Search company (e.g. RELIANCE, TCS, etc...)';
  @Input() availableStocks: (Company | Stock | any)[] = [];

  // Right Section Nav Action Toggles
  @Input() showAnnualReports: boolean = true;
  @Input() showWatchlist: boolean = true;
  @Input() showAdminBell: boolean = true;

  // Exchange Switcher Inputs
  @Input() showExchangeSwitcher: boolean = false;
  @Input() selectedExchange: string = 'NSE';

  // Live Sync Inputs
  @Input() showSync: boolean = false;
  @Input() isSyncing: boolean = false;

  // Back Button Inputs
  @Input() showBackButton: boolean = false;
  @Input() backButtonText: string = 'Back to NIFTY 50';
  @Input() backButtonUrl: string = '/nifty50';

  // Top Progress Bar
  @Input() showProgressBar: boolean = false;
  @Input() isLoading: boolean = false;

  // Outputs / Events
  @Output() stockSelected = new EventEmitter<Company | Stock>();
  @Output() searchSubmit = new EventEmitter<string>();
  @Output() exchangeChange = new EventEmitter<'NSE' | 'BSE' | string>();
  @Output() sync = new EventEmitter<void>();
  @Output() backClick = new EventEmitter<void>();

  // Search internal state
  searchQuery = signal<string>('');
  searchResults = signal<Company[]>([]);
  isSearching = signal<boolean>(false);
  isSearchDropdownOpen = signal<boolean>(false);
  failedLogos = signal<Set<string>>(new Set());

  @ViewChild('searchWrapper') searchWrapper?: ElementRef<HTMLElement>;
  @ViewChild('searchInput') searchInput?: ElementRef<HTMLInputElement>;

  private searchSubject = new Subject<string>();
  private searchSubscription?: Subscription;

  // Computed helper to detect active annual reports route
  isAnnualReportsActive = computed(() => {
    return this.router.url.startsWith('/annual-reports');
  });

  ngOnInit(): void {
    if (this.showSearch) {
      this.setupSearchPipeline();
    }
  }

  ngOnDestroy(): void {
    this.searchSubscription?.unsubscribe();
  }

  // Set up debounced search observable pipeline
  private setupSearchPipeline(): void {
    this.searchSubscription = this.searchSubject.pipe(
      debounceTime(200),
      distinctUntilChanged(),
      switchMap((query) => {
        const clean = query.trim();
        if (!clean || clean.length < 2) {
          this.searchResults.set([]);
          this.isSearching.set(false);
          this.isSearchDropdownOpen.set(false);
          return of([]);
        }
        this.isSearching.set(true);

        const localMatches: Company[] = (this.availableStocks || [])
          .filter(s => {
            const sym = (s.symbol || '').toLowerCase();
            const cName = (s.companyName || s.name || '').toLowerCase();
            const target = clean.toLowerCase();
            return sym.includes(target) || cName.includes(target);
          })
          .map(s => ({
            id: s.id || 0,
            symbol: s.symbol,
            companyName: s.companyName || s.name || s.symbol,
            industry: s.industry || s.sector || '',
            exchange: (s as any).exchange || 'NSE',
            logoUrl: (s as any).logoUrl
          } as Company));

        return this.newsService.searchCompanies(clean).pipe(
          map(apiResults => {
            if (apiResults && apiResults.length > 0) {
              return apiResults;
            }
            return localMatches;
          }),
          catchError(() => of(localMatches))
        );
      })
    ).subscribe((results) => {
      this.searchResults.set(results || []);
      this.isSearching.set(false);
      if (results && results.length > 0) {
        this.isSearchDropdownOpen.set(true);
      }
    });
  }

  // Input & Focus event handlers
  onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchQuery.set(value);
    if (value.trim().length >= 2) {
      this.isSearchDropdownOpen.set(true);
    } else {
      this.isSearchDropdownOpen.set(false);
      this.searchResults.set([]);
    }
    this.searchSubject.next(value);
  }

  onSearchFocus(): void {
    if (this.searchResults().length > 0 && this.searchQuery().trim().length >= 2) {
      this.isSearchDropdownOpen.set(true);
    }
  }

  // Document-level shortcuts & outside click listeners
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as Node;
    if (
      this.isSearchDropdownOpen() &&
      this.searchWrapper?.nativeElement &&
      !this.searchWrapper.nativeElement.contains(target)
    ) {
      this.isSearchDropdownOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  onEscapePress(): void {
    this.isSearchDropdownOpen.set(false);
  }

  @HostListener('document:keydown', ['$event'])
  onKeyDown(event: KeyboardEvent): void {
    // Ctrl+K or Cmd+K to focus search input
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      if (this.showSearch && this.searchInput?.nativeElement) {
        event.preventDefault();
        this.searchInput.nativeElement.focus();
        this.searchInput.nativeElement.select();
      }
    }
  }

  // Selection & Submission
  selectSearchResult(company: Company | Stock): void {
    this.isSearchDropdownOpen.set(false);
    this.searchQuery.set('');
    this.searchResults.set([]);

    if (this.stockSelected.observed) {
      this.stockSelected.emit(company);
    } else {
      this.router.navigate(['/stock', company.symbol.toUpperCase()]);
    }
  }

  onSearchSubmit(): void {
    this.isSearchDropdownOpen.set(false);
    const query = this.searchQuery().trim();
    if (!query) return;

    this.searchQuery.set('');
    this.searchResults.set([]);

    if (this.searchSubmit.observed) {
      this.searchSubmit.emit(query);
    } else {
      this.router.navigate(['/stock', query.toUpperCase()]);
    }
  }

  onLogoError(symbol: string): void {
    if (!symbol) return;
    this.failedLogos.update(set => {
      const next = new Set(set);
      next.add(symbol.toUpperCase());
      return next;
    });
  }

  isLogoFailed(symbol: string): boolean {
    return this.failedLogos().has((symbol || '').toUpperCase());
  }

  // Navigation methods
  navigateToHome(): void {
    this.router.navigate(['/']);
  }

  navigateToAnnualReports(): void {
    this.router.navigate(['/annual-reports']);
  }

  navigateToWatchlist(): void {
    this.router.navigate(['/nifty50'], { queryParams: { tab: 'watchlist' } });
  }

  toggleExchange(exchange: string): void {
    this.selectedExchange = exchange;
    this.exchangeChange.emit(exchange);
  }

  onSyncClick(): void {
    this.sync.emit();
  }

  onBackClick(): void {
    if (this.backClick.observed) {
      this.backClick.emit();
    } else if (this.backButtonUrl) {
      this.router.navigateByUrl(this.backButtonUrl);
    }
  }
}
