import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StockQuarterlyResultsResponse } from '../models/stock-quarterly-results.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockQuarterlyResultsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetch quarterly results by stock symbol and exchange.
   */
  getQuarterlyResultsBySymbol(symbol: string, exchange = 'NSE', refresh = false): Observable<StockQuarterlyResultsResponse> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('refresh', refresh.toString());

    return this.http.get<StockQuarterlyResultsResponse>(`${this.baseUrl}/api/stocks/quarterly-results`, { params });
  }
}
