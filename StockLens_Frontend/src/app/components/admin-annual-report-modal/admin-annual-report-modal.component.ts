import { Component, inject, signal, effect, computed, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminModalService } from '../../services/admin-modal.service';
import { AdminAuthService } from '../../services/admin-auth.service';
import { StockAnnualReportService } from '../../services/stock-annual-report.service';

@Component({
  selector: 'app-admin-annual-report-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './admin-annual-report-modal.component.html',
  styleUrls: ['./admin-annual-report-modal.component.css']
})
export class AdminAnnualReportModalComponent {
  readonly modalService = inject(AdminModalService);
  readonly authService = inject(AdminAuthService);
  private readonly annualReportService = inject(StockAnnualReportService);

  // Form Fields
  symbol = signal<string>('');
  year = signal<string>('2026');
  title = signal<string>('');
  pdfUrl = signal<string>('');
  source = signal<string>('BSE');

  // Operation States
  isSuggesting = signal<boolean>(false);
  isExtracting = signal<boolean>(false);
  errorMessage = signal<string>('');
  successMessage = signal<string>('');

  // PIN Prompt State
  enteredPin = '';

  isSaveDisabled = computed(() => {
    return this.isExtracting() || !this.symbol()?.trim() || !this.pdfUrl()?.trim();
  });

  constructor() {
    effect(() => {
      const open = this.modalService.isOpen();
      if (open) {
        untracked(() => {
          const s = this.modalService.initialSymbol() || '';
          const initialPdf = this.modalService.initialPdfUrl() || '';
          this.symbol.set(s);
          this.year.set(this.modalService.initialYear() || '2026');
          this.title.set(this.modalService.initialTitle() || (s ? `${s} Annual Report 2026` : ''));
          this.pdfUrl.set(initialPdf);
          this.errorMessage.set('');
          this.successMessage.set('');

          // If symbol provided but no PDF URL, automatically try suggesting Screener link
          if (s && !initialPdf) {
            this.suggestPdfLink(s);
          }
        });
      }
    });
  }

  suggestPdfLink(targetSymbol?: string): void {
    const sym = (targetSymbol || this.symbol()).trim().toUpperCase();
    if (!sym) return;

    this.isSuggesting.set(true);
    this.annualReportService.suggestScreenerPdf(sym).subscribe({
      next: (res) => {
        this.isSuggesting.set(false);
        if (res?.pdfUrl) {
          this.pdfUrl.set(res.pdfUrl);
          if (!this.title()) {
            this.title.set(`${sym} Annual Report ${this.year()}`);
          }
        }
      },
      error: () => {
        this.isSuggesting.set(false);
      }
    });
  }

  saveAndExtract(): void {
    const sym = this.symbol().trim().toUpperCase();
    const url = this.pdfUrl().trim();
    if (!sym) {
      this.errorMessage.set('Please enter a Stock Symbol.');
      return;
    }
    if (!url) {
      this.errorMessage.set('Please provide the Annual Report PDF URL.');
      return;
    }

    this.errorMessage.set('');
    this.successMessage.set('');
    this.isExtracting.set(true);

    this.annualReportService.adminSaveAndExtractReport({
      symbol: sym,
      year: (this.year() || '2026').trim(),
      title: this.title().trim() || `${sym} Annual Report ${this.year() || '2026'}`,
      pdfUrl: url,
      source: this.source() || 'BSE'
    }).subscribe({
      next: (response) => {
        this.isExtracting.set(false);
        this.successMessage.set(`Insights successfully extracted and saved for ${sym}!`);
        this.modalService.notifyExtracted(response);
        setTimeout(() => {
          this.modalService.close();
        }, 1200);
      },
      error: (err) => {
        this.isExtracting.set(false);
        this.errorMessage.set(err?.error?.message || err?.message || 'Failed to extract AI insights from the PDF.');
      }
    });
  }

  onPinSubmit(): void {
    if (this.authService.submitPin(this.enteredPin)) {
      this.enteredPin = '';
    }
  }

  closeModal(): void {
    if (!this.isExtracting()) {
      this.modalService.close();
    }
  }
}
