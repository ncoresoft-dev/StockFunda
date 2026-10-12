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
        peRatio: item.peRatio,
        marketCap: item.marketCap,
        dividendYield: item.dividendYield,
        netProfitTtm: item.netProfitTtm,
        profitGrowthYoy: item.profitGrowthYoy,
        revenueTtm: item.revenueTtm,
        revenueGrowthQoq: item.revenueGrowthQoq,
        roce: item.roce,
        exchange: 'NSE'
      } as Nifty50StockItem))),
      tap(mappedData => this.cachedNifty50List = mappedData),
      catchError(error => {
        console.error('Error fetching Nifty 50 companies:', error);
        return of([]); // Return empty list on failure instead of dummy data
      })
    );
  }

  /**
   * Finds stock items matching a list of symbols (for Watchlist tab)
   */
  getStocksBySymbols(symbols: string[]): Observable<Nifty50StockItem[]> {
    if (!symbols || symbols.length === 0) {
      return of([]);
    }

    if (!this.cachedNifty50List) {
      // If no cache, fetch first then filter
      return this.getNifty50List().pipe(
        map(stocks => stocks.filter(s => symbols.includes(s.symbol)))
      );
    }
    
    // Filter from cache
    return of(this.cachedNifty50List.filter(s => symbols.includes(s.symbol)));
  }
}
