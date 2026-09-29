import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface PriceHistoryResponseDto {
  symbol: string;
  errorMessage?: string;
  dates: string[];
  opens?: number[];
  highs?: number[];
  lows?: number[];
  closePrices: number[];
  volumes: number[];
  source?: string;
  lastSyncedAt?: string;
  dma50?: (number | null)[];
  dma200?: (number | null)[];
  detectedPatterns?: { date: string, patternName: string, signal: string, coordinates?: { date: string, price: number }[] }[];
  patternCounts?: { [key: string]: number };
}

@Injectable({
  providedIn: 'root'
})
export class StockPriceHistoryService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getPriceHistory(symbol: string, exchange = 'NSE', period = '5yr', refresh = false, filter = 'price'): Observable<PriceHistoryResponseDto> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('period', period)
      .set('filter', filter)
      .set('refresh', refresh.toString());

    return this.http.get<PriceHistoryResponseDto>(`${this.apiUrl}/api/stocks/prices`, { params });
  }

  getPriceHistoryByStockId(stockId: number, period = '5yr', refresh = false, filter = 'price'): Observable<PriceHistoryResponseDto> {
    const params = new HttpParams()
      .set('period', period)
      .set('filter', filter)
      .set('refresh', refresh.toString());
    return this.http.get<PriceHistoryResponseDto>(`${this.apiUrl}/api/stocks/${stockId}/prices`, { params });
  }
}
