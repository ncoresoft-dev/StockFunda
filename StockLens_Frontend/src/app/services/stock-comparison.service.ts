import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ComparisonPreset, StockComparisonResponse } from '../models/stock-comparison.model';

@Injectable({
  providedIn: 'root'
})
export class StockComparisonService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Compare two stocks side-by-side with valuation, health scores, quarterly results, and price history
   */
  compareStocks(
    stock1: string,
    stock2: string,
    exchange = 'NSE',
    period = '1yr',
    refresh = false
  ): Observable<StockComparisonResponse> {
    const params = new HttpParams()
      .set('stock1', stock1.trim().toUpperCase())
      .set('stock2', stock2.trim().toUpperCase())
      .set('exchange', exchange)
      .set('period', period)
      .set('refresh', refresh.toString());

    return this.http.get<StockComparisonResponse>(`${this.baseUrl}/api/stocks/compare`, { params });
  }

  /**
   * Get popular stock comparison rivalry presets
   */
  getPresets(): Observable<ComparisonPreset[]> {
    return this.http.get<ComparisonPreset[]>(`${this.baseUrl}/api/stocks/compare/presets`);
  }
}

