import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { StockShareholdingResponse } from '../models/stock-shareholding.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockShareholdingService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetch shareholding details by stock symbol and exchange.
   */
  getShareholdingBySymbol(symbol: string, exchange = 'NSE', refresh = false): Observable<StockShareholdingResponse> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('refresh', refresh.toString());

    return this.http.get<StockShareholdingResponse>(`${this.baseUrl}/api/stocks/shareholding`, { params });
  }
}
