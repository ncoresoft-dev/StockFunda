import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StockCashflowResponse } from '../models/stock-cashflow.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockCashflowService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetch annual cash flow metrics and statements by stock symbol and exchange.
   */
  getCashflowBySymbol(symbol: string, exchange = 'NSE', refresh = false): Observable<StockCashflowResponse> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('refresh', refresh.toString());

    return this.http.get<StockCashflowResponse>(`${this.baseUrl}/api/stocks/cashflow`, { params });
  }
}
