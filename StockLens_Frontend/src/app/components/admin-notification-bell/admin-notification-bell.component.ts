import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AdminAuthService } from '../../services/admin-auth.service';
import { AdminModalService } from '../../services/admin-modal.service';
import { StockAnnualReportService } from '../../services/stock-annual-report.service';
import { AnnualReportDirectoryItem } from '../../models/stock-annual-report.model';
import { Subscription, interval } from 'rxjs';

@Component({
  selector: 'app-admin-notification-bell',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './admin-notification-bell.component.html',
  styleUrls: ['./admin-notification-bell.component.css']
})
export class AdminNotificationBellComponent implements OnInit, OnDestroy {
  readonly authService = inject(AdminAuthService);
  readonly modalService = inject(AdminModalService);
  private readonly annualReportService = inject(StockAnnualReportService);

  isOpen = signal<boolean>(false);
  requestedList = signal<AnnualReportDirectoryItem[]>([]);
  isLoading = signal<boolean>(false);

  private pollSub?: Subscription;
  private extractSub?: Subscription;

  ngOnInit(): void {
    this.loadRequestedReports();

    // Poll every 60 seconds for new requests if admin is active
    this.pollSub = interval(60000).subscribe(() => {
      if (this.authService.isAdmin()) {
        this.loadRequestedReports();
      }
    });

    // Refresh requested list when admin extracts a report
    this.extractSub = this.modalService.reportExtracted$.subscribe(() => {
      this.loadRequestedReports();
    });
  }

  ngOnDestroy(): void {
    this.pollSub?.unsubscribe();
    this.extractSub?.unsubscribe();
  }

  loadRequestedReports(): void {
    if (!this.authService.isAdmin()) return;

    this.isLoading.set(true);
    this.annualReportService.getRequestedReports().subscribe({
      next: (list) => {
        this.requestedList.set(list || []);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
      }
    });
  }

  toggleDropdown(): void {
    this.isOpen.update(v => !v);
    if (this.isOpen()) {
      this.loadRequestedReports();
    }
  }

  closeDropdown(): void {
    this.isOpen.set(false);
  }

  openExtractFor(item: AnnualReportDirectoryItem): void {
    this.isOpen.set(false);
    this.modalService.open(item.symbol, item.year || '', item.title || '', item.pdfUrl || '');
  }

  openNewReportModal(): void {
    this.isOpen.set(false);
    this.modalService.open();
  }

  logout(): void {
    this.authService.logoutAdmin();
    this.isOpen.set(false);
  }
}
