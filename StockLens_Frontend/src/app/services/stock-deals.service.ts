import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StockDealsSummary } from '../models/stock-deals.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockDealsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetch deals (bulk, block, insider) by stock database ID.
   */
  getDealsByStockId(stockId: number, refresh = false): Observable<StockDealsSummary> {
    const params = new HttpParams().set('refresh', refresh.toString());
    return this.http.get<StockDealsSummary>(`${this.baseUrl}/api/stocks/${stockId}/deals`, { params });
  }

  /**
   * Fetch deals (bulk, block, insider) by stock symbol and exchange.
   */
  getDealsBySymbol(symbol: string, exchange = 'NSE', refresh = false): Observable<StockDealsSummary> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('refresh', refresh.toString());

    return this.http.get<StockDealsSummary>(`${this.baseUrl}/api/stocks/deals`, { params });
  }
}
