import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Nifty50StockItem } from '../models/stock-index.model';
@Injectable({
  providedIn: 'root'
})
export class StockIndexService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api/v1/indices`;
  
  // Cache to avoid hitting API constantly on tab switches
  private cachedNifty50List: Nifty50StockItem[] | null = null;

  /**
   * Returns NIFTY 50 constituent companies for the UI
   */
  getNifty50List(forceRefresh = false): Observable<Nifty50StockItem[]> {
    if (!forceRefresh && this.cachedNifty50List && this.cachedNifty50List.length > 0) {
      return of(this.cachedNifty50List);
    }

    return this.http.get<any[]>(`${this.apiUrl}/nifty-50`).pipe(
      map(data => data.map(item => ({
        symbol: item.symbol,
        companyName: item.companyName || item.company_name,
        sector: item.sector,
        logoUrl: item.logoUrl || item.logo_url,
        websiteUrl: item.websiteUrl || item.website_url,
        price: item.price,
        marketCap: item.marketCap,
        exchange: 'NSE'
      } as Nifty50StockItem))),
      tap(mappedData => this.cachedNifty50List = mappedData),
      catchError(error => {
        console.error('Error fetching Nifty 50 companies:', error);
        return of(this.cachedNifty50List || []); // Return cached list on failure or empty list
      })
    );
  }

  /**
   * Updates price and market cap for a symbol in local cache
   */
  updateStockInCache(symbol: string, price?: number | null, marketCap?: number | null): void {
    if (!this.cachedNifty50List || !symbol) return;
    const cleanSym = symbol.trim().toUpperCase();
    const item = this.cachedNifty50List.find(s => s.symbol?.toUpperCase() === cleanSym);
    if (item) {
      if (price !== undefined && price !== null && price > 0) {
        item.price = price;
      }
      if (marketCap !== undefined && marketCap !== null && marketCap > 0) {
        item.marketCap = marketCap;
      }
    }
  }

  /**
   * Clears in-memory cache so next call always hits API
   */
  clearCache(): void {
    this.cachedNifty50List = null;
  }

  /**
   * Finds stock items matching a list of symbols (for Watchlist tab or custom queries).
   * Queries the dedicated backend endpoint so non-Nifty stocks (e.g. BANDHANBNK) return real logo, CMP, and market cap.
   */
  getStocksBySymbols(symbols: string[]): Observable<Nifty50StockItem[]> {
    if (!symbols || symbols.length === 0) {
      return of([]);
    }

    const cleanSymbols = symbols.map(s => s.trim().toUpperCase()).filter(Boolean);
    const joined = cleanSymbols.join(',');

    return this.http.get<any[]>(`${this.apiUrl}/stocks-by-symbols?symbols=${encodeURIComponent(joined)}`).pipe(
      map(data => data.map(item => ({
        symbol: item.symbol,
        companyName: item.companyName || item.company_name,
        sector: item.sector || 'Equities',
        logoUrl: item.logoUrl || item.logo_url,
        websiteUrl: item.websiteUrl || item.website_url,
        price: item.price,
        marketCap: item.marketCap || item.market_cap,
        exchange: 'NSE'
      } as Nifty50StockItem))),
      catchError(error => {
        console.warn('Error fetching stocks by symbols from API, falling back to cache:', error);
        if (this.cachedNifty50List) {
          return of(this.cachedNifty50List.filter(s => cleanSymbols.includes(s.symbol?.toUpperCase())));
        }
        return of([]);
      })
    );
  }
}

