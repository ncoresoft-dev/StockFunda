import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Stock, Company, StockNewsResponse, MarketNewsResponse } from '../models/stock-news.model';
import { StockAnnouncementResponse } from '../models/stock-announcement.model';
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

  /**
   * Fetch latest market-wide news (Sensex, Nifty, Stock Market).
   * Reads strictly from backend cache. Never triggers external RSS fetches.
   */
  getMarketNews(limit = 10, category?: string): Observable<MarketNewsResponse> {
    let params = new HttpParams().set('limit', limit.toString());
    if (category && category !== 'All') {
      params = params.set('category', category);
    }

    return this.http.get<MarketNewsResponse>(`${this.baseUrl}/api/market-news`, { params });
  }

  /**
   * Fetch corporate announcements from local DB.
   * Supports filtering by single symbol or multiple symbols (watchlist feed).
   * Never calls NSE directly from the client.
   */
  getCorporateAnnouncements(
    symbol?: string,
    search?: string,
    category?: string,
    limit = 30,
    page = 1,
    symbols?: string[] | string
  ): Observable<StockAnnouncementResponse> {
    let params = new HttpParams()
      .set('limit', limit.toString())
      .set('page', page.toString());

    if (symbol && symbol.trim()) {
      params = params.set('symbol', symbol.trim());
    }
    if (symbols) {
      const symList = Array.isArray(symbols) ? symbols.join(',') : symbols;
      if (symList && symList.trim()) {
        params = params.set('symbols', symList.trim());
      }
    }
    if (search && search.trim()) {
      params = params.set('search', search.trim());
    }
    if (category && category !== 'All') {
      params = params.set('category', category.trim());
    }

    return this.http.get<StockAnnouncementResponse>(`${this.baseUrl}/api/corporate-announcements`, { params });
  }

  /**
   * Fetch distinct announcement categories / subjects.
   */
  getAnnouncementCategories(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/api/corporate-announcements/categories`);
  }

  /**
   * Request backend to trigger a fresh sync from NSE feed.
   */
  triggerAnnouncementSync(): Observable<{ success: boolean; newRecordsAdded: number }> {
    return this.http.post<{ success: boolean; newRecordsAdded: number }>(
      `${this.baseUrl}/api/corporate-announcements/sync`,
      {}
    );
  }
}
