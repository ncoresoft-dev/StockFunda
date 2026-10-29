import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { StockAnnualReportService } from '../../services/stock-annual-report.service';
import { AdminAuthService } from '../../services/admin-auth.service';
import { AdminModalService } from '../../services/admin-modal.service';
import { AnnualReportDirectoryItem } from '../../models/stock-annual-report.model';
import { AdminAnnualReportModalComponent } from '../admin-annual-report-modal/admin-annual-report-modal.component';
import { HeaderComponent } from '../header/header.component';
import { Subscription } from 'rxjs';

@Component({
  selector: 'app-annual-reports-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    AdminAnnualReportModalComponent,
    HeaderComponent
  ],
  templateUrl: './annual-reports-list.component.html',
  styleUrls: ['./annual-reports-list.component.css']
})
export class AnnualReportsListComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly annualReportService = inject(StockAnnualReportService);
  readonly authService = inject(AdminAuthService);
  readonly modalService = inject(AdminModalService);

  searchQuery = signal<string>('');
  activeFilter = signal<'All' | 'Completed' | 'Requested'>('All');
  allReports = signal<AnnualReportDirectoryItem[]>([]);
  isLoading = signal<boolean>(false);
  requestedFeedback = signal<{ [symbol: string]: string }>({});
  failedLogos = signal<{ [symbol: string]: boolean }>({});

  onLogoError(symbol: string): void {
    this.failedLogos.update(map => ({ ...map, [symbol]: true }));
  }

  // Computed counts from master list - these NEVER reset to 0 when switching tabs!
  totalAllCount = computed(() => this.allReports().length);
  totalCompletedCount = computed(() => this.allReports().filter(i => (i.extractionStatus || '').trim().toLowerCase() === 'completed').length);
  totalRequestedCount = computed(() => this.allReports().filter(i => (i.extractionStatus || '').trim().toLowerCase() === 'requested').length);

  // Computed filtered list for active view
  filteredReports = computed(() => {
    const list = this.allReports();
    const q = this.searchQuery().trim().toLowerCase();
    const filter = this.activeFilter();

    return list.filter(item => {
      const status = (item.extractionStatus || '').trim().toLowerCase();

      // 1. Filter by status
      if (filter === 'Completed' && status !== 'completed') {
        return false;
      }
      if (filter === 'Requested' && status !== 'requested') {
        return false;
      }

      // 2. Filter by search query
      if (q) {
        const sym = (item.symbol || '').toLowerCase();
        const name = (item.companyName || '').toLowerCase();
        const sector = (item.sector || item.industry || '').toLowerCase();
        if (!sym.includes(q) && !name.includes(q) && !sector.includes(q)) {
          return false;
        }
      }

      return true;
    });
  });

  private extractSub?: Subscription;

  ngOnInit(): void {
    this.loadDirectory();

    // Re-fetch when admin extracts a report
    this.extractSub = this.modalService.reportExtracted$.subscribe(() => {
      this.loadDirectory();
    });
  }

  ngOnDestroy(): void {
    this.extractSub?.unsubscribe();
  }

  loadDirectory(): void {
    this.isLoading.set(true);
    // Fetch all records so counts are always accurate
    this.annualReportService.getAnnualReportsDirectory().subscribe({
      next: (list) => {
        this.allReports.set(list || []);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  setFilter(filter: 'All' | 'Completed' | 'Requested'): void {
    this.activeFilter.set(filter);
  }

  viewInsights(symbol: string): void {
    this.router.navigate(['/stock', symbol]);
  }

  requestReport(symbol: string): void {
    this.annualReportService.requestAnnualReport(symbol).subscribe({
      next: () => {
        this.requestedFeedback.update(map => ({ ...map, [symbol]: 'Requested!' }));
        this.loadDirectory();
      },
      error: () => {
        this.requestedFeedback.update(map => ({ ...map, [symbol]: 'Failed' }));
      }
    });
  }

  openAdminExtract(item: AnnualReportDirectoryItem): void {
    this.modalService.open(item.symbol, item.year || '2026', item.title || '', item.pdfUrl || '');
  }

  openAdminNewReport(): void {
    this.modalService.open();
  }

  openPdf(url?: string): void {
    if (url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    }
  }

  navigateToHome(): void {
    this.router.navigate(['/']);
  }

  navigateToNifty50(): void {
    this.router.navigate(['/nifty50']);
  }
}
