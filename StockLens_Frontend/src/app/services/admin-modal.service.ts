import { Injectable, signal } from '@angular/core';
import { Subject } from 'rxjs';
import { StockAnnualReportsResponse } from '../models/stock-annual-report.model';

@Injectable({
  providedIn: 'root'
})
export class AdminModalService {
  isOpen = signal<boolean>(false);
  initialSymbol = signal<string>('');
  initialYear = signal<string>('');
  initialTitle = signal<string>('');
  initialPdfUrl = signal<string>('');

  // Notifies subscribers when a report has been successfully extracted & saved
  reportExtracted$ = new Subject<StockAnnualReportsResponse>();

  open(symbol = '', year = '', title = '', pdfUrl = ''): void {
    this.initialSymbol.set(symbol);
    this.initialYear.set(year);
    this.initialTitle.set(title);
    this.initialPdfUrl.set(pdfUrl);
    this.isOpen.set(true);
  }

  close(): void {
    this.isOpen.set(false);
  }

  notifyExtracted(response: StockAnnualReportsResponse): void {
    this.reportExtracted$.next(response);
  }
}
