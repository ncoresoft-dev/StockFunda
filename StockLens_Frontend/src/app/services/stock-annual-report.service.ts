import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, Subject, tap } from 'rxjs';
import { StockAnnualReportsResponse, AdminSaveAndExtractReportRequest, AnnualReportDirectoryItem } from '../models/stock-annual-report.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockAnnualReportService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Shared reactive signal holding the current stock's annual report data
   * Accessible by top header, sticky nav bar, and bottom about card.
   */
  readonly activeReport = signal<StockAnnualReportsResponse | null>(null);
  readonly isActiveReportLoading = signal<boolean>(false);

  /**
   * Emits whenever an annual report is requested or extracted.
   */
  readonly reportUpdated$ = new Subject<StockAnnualReportsResponse>();

  /**
   * Retrieves the latest Annual Report with extracted AI insights for a stock.
   */
  getAnnualReports(symbol: string, refresh = false): Observable<StockAnnualReportsResponse> {
    const params = new HttpParams().set('refresh', refresh.toString());
    this.isActiveReportLoading.set(true);
    return this.http.get<StockAnnualReportsResponse>(
      `${this.baseUrl}/api/stocks/${encodeURIComponent(symbol)}/annual-reports`,
      { params }
    ).pipe(
      tap({
        next: (data) => {
          this.activeReport.set(data);
          this.isActiveReportLoading.set(false);
        },
        error: () => {
          this.isActiveReportLoading.set(false);
        }
      })
    );
  }

  /**
   * Allows a user to request an Annual Report for a company.
   */
  requestAnnualReport(symbol: string): Observable<StockAnnualReportsResponse> {
    return this.http.post<StockAnnualReportsResponse>(
      `${this.baseUrl}/api/stocks/${encodeURIComponent(symbol)}/annual-reports/request`,
      {}
    ).pipe(
      tap((res) => {
        this.activeReport.set(res);
        this.reportUpdated$.next(res);
      })
    );
  }

  /**
   * Admin triggers saving metadata and generating AI insights from PDF.
   */
  adminSaveAndExtractReport(request: AdminSaveAndExtractReportRequest): Observable<StockAnnualReportsResponse> {
    return this.http.post<StockAnnualReportsResponse>(
      `${this.baseUrl}/api/stocks/annual-reports/admin/extract`,
      request
    ).pipe(
      tap((res) => {
        this.activeReport.set(res);
        this.reportUpdated$.next(res);
      })
    );
  }

  /**
   * Fetches directory of all companies with annual report statuses.
   */
  getAnnualReportsDirectory(search?: string, status?: string): Observable<AnnualReportDirectoryItem[]> {
    let params = new HttpParams();
    if (search && search.trim()) {
      params = params.set('search', search.trim());
    }
    if (status && status !== 'All') {
      params = params.set('status', status);
    }
    return this.http.get<AnnualReportDirectoryItem[]>(
      `${this.baseUrl}/api/stocks/annual-reports/directory`,
      { params }
    );
  }

  /**
   * Fetches all annual reports currently in 'Requested' status (for admin notification badge).
   */
  getRequestedReports(): Observable<AnnualReportDirectoryItem[]> {
    return this.http.get<AnnualReportDirectoryItem[]>(
      `${this.baseUrl}/api/stocks/annual-reports/requested`
    );
  }

  /**
   * Suggests the official PDF link from Screener for autofill in Admin form.
   */
  suggestScreenerPdf(symbol: string): Observable<{ symbol: string; pdfUrl: string | null }> {
    return this.http.get<{ symbol: string; pdfUrl: string | null }>(
      `${this.baseUrl}/api/stocks/${encodeURIComponent(symbol)}/annual-reports/suggest-pdf`
    );
  }
}
