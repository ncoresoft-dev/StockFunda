import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StockAnnualReportsResponse } from '../models/stock-annual-report.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockAnnualReportService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Retrieves the latest Annual Report with extracted AI insights for a stock.
   */
  getAnnualReports(symbol: string, refresh = false): Observable<StockAnnualReportsResponse> {
    const params = new HttpParams().set('refresh', refresh.toString());
    return this.http.get<StockAnnualReportsResponse>(
      `${this.baseUrl}/api/stocks/${encodeURIComponent(symbol)}/annual-reports`,
      { params }
    );
  }
}
