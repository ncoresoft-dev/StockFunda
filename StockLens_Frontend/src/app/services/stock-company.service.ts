import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CompanyOverview } from '../models/company-overview.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockCompanyService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetch company Screener-style overview (About narrative, key points, website, employees)
   */
  getCompanyOverview(symbol: string, exchange = 'NSE', forceRefresh = false): Observable<CompanyOverview> {
    const params = new HttpParams()
      .set('exchange', exchange)
      .set('forceRefresh', forceRefresh.toString());

    return this.http.get<CompanyOverview>(`${this.baseUrl}/api/companies/${encodeURIComponent(symbol)}/overview`, { params });
  }
}
