import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { MarketTickerQuote, MarketPulseResponse } from '../models/market-ticker.model';

@Injectable({
  providedIn: 'root'
})
export class MarketTickerService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/market/ticker`;

  private cachedQuotes: MarketTickerQuote[] | null = null;

  getTickerQuotes(forceRefresh = false): Observable<MarketTickerQuote[]> {
    if (!forceRefresh && this.cachedQuotes && this.cachedQuotes.length > 0) {
      return of(this.cachedQuotes);
    }

    const url = forceRefresh ? `${this.apiUrl}?forceRefresh=true` : this.apiUrl;
    return this.http.get<MarketTickerQuote[]>(url).pipe(
      map(quotes => {
        if (!quotes) {
          return [];
        }
        return quotes.map(q => ({
          ...q,
          isPositive: (q.change ?? 0) >= 0
        }));
      }),
      tap(quotes => {
        if (quotes && quotes.length > 0) {
          this.cachedQuotes = quotes;
        }
      }),
      catchError(err => {
        console.error('Could not fetch market ticker quotes from API:', err);
        if (this.cachedQuotes && this.cachedQuotes.length > 0) {
          return of(this.cachedQuotes);
        }
        return throwError(() => err);
      })
    );
  }

  getExploreLensData(lens: string = 'Growth', forceRefresh = false): Observable<import('../models/market-ticker.model').ExploreLensResponse> {
    const url = `${environment.apiUrl}/api/market/explore-lens?lens=${encodeURIComponent(lens)}${forceRefresh ? '&forceRefresh=true' : ''}`;
    return this.http.get<import('../models/market-ticker.model').ExploreLensResponse>(url);
  }

  private readonly pulseCache = new Map<string, MarketPulseResponse>();

  getMarketPulse(timeframe: string = '1D', forceRefresh = false): Observable<MarketPulseResponse> {
    const cleanTf = (timeframe || '1D').toUpperCase();

    if (!forceRefresh && this.pulseCache.has(cleanTf)) {
      return of(this.pulseCache.get(cleanTf)!);
    }

    const url = `${environment.apiUrl}/api/market/pulse?timeframe=${encodeURIComponent(cleanTf)}${forceRefresh ? '&forceRefresh=true' : ''}`;
    return this.http.get<MarketPulseResponse>(url).pipe(
      tap(data => {
        if (data && data.indices && data.indices.length > 0) {
          this.pulseCache.set(cleanTf, data);
        }
      }),
      catchError(err => {
        console.error('Could not fetch market pulse data from API:', err);
        const cached = this.pulseCache.get(cleanTf);
        if (cached && !forceRefresh) {
          return of(cached);
        }
        return throwError(() => err);
      })
    );
  }
}
