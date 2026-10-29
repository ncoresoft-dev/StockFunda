import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Stock, Company, StockNewsResponse } from '../models/stock-news.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockNewsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetch all registered stocks for the selector/search dropdown.
   */
  getStocks(): Observable<Stock[]> {
    return this.http.get<Stock[]>(`${this.baseUrl}/api/stocks`);
  }

  /**
   * Search companies by query (symbol or company name).
   */
  searchCompanies(query: string): Observable<Company[]> {
    const params = new HttpParams().set('query', query).set('limit', '10');
    return this.http.get<Company[]>(`${this.baseUrl}/api/companies/search`, { params });
  }

  /**
   * Fetch latest news by Stock Ticker Symbol and Exchange.
   */
  getNewsBySymbol(symbol: string, exchange = 'NSE', limit = 5, page = 1, refresh = false): Observable<StockNewsResponse> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('limit', limit.toString())
      .set('page', page.toString())
      .set('refresh', refresh.toString());

    return this.http.get<StockNewsResponse>(`${this.baseUrl}/api/stocks/news`, { params });
  }
}
