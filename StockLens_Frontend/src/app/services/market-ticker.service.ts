import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { MarketTickerQuote } from '../models/market-ticker.model';

@Injectable({
  providedIn: 'root'
})
export class MarketTickerService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/api/market/ticker`;

  private cachedQuotes: MarketTickerQuote[] | null = null;

  // Fallback defaults in case API is offline
  private readonly defaultFallbackQuotes: MarketTickerQuote[] = [
    {
      symbol: '^NSEI',
      displayName: 'NIFTY 50',
      shortName: 'NIFTY',
      currentPrice: 24350,
      previousClose: 24228.85,
      change: 121.15,
      changePercent: 0.5,
      instrumentType: 'INDEX',
      displayOrder: 1,
      lastUpdated: new Date().toISOString(),
      formattedPrice: '24,350',
      formattedChange: '+0.5%',
      isPositive: true
    },
    {
      symbol: '^BSESN',
      displayName: 'S&P BSE SENSEX',
      shortName: 'SENSEX',
      currentPrice: 80120,
      previousClose: 79800.8,
      change: 319.2,
      changePercent: 0.4,
      instrumentType: 'INDEX',
      displayOrder: 2,
      lastUpdated: new Date().toISOString(),
      formattedPrice: '80,120',
      formattedChange: '+0.4%',
      isPositive: true
    },
    {
      symbol: '^NSEBANK',
      displayName: 'NIFTY BANK',
      shortName: 'BANK NIFTY',
      currentPrice: 52410,
      previousClose: 52462.4,
      change: -52.4,
      changePercent: -0.1,
      instrumentType: 'INDEX',
      displayOrder: 3,
      lastUpdated: new Date().toISOString(),
      formattedPrice: '52,410',
      formattedChange: '-0.1%',
      isPositive: false
    },
    {
      symbol: '^CNXIT',
      displayName: 'NIFTY IT',
      shortName: 'NIFTY IT',
      currentPrice: 38900,
      previousClose: 39174.2,
      change: -274.2,
      changePercent: -0.7,
      instrumentType: 'INDEX',
      displayOrder: 4,
      lastUpdated: new Date().toISOString(),
      formattedPrice: '38,900',
      formattedChange: '-0.7%',
      isPositive: false
    },
    {
      symbol: 'INR=X',
      displayName: 'USD/INR',
      shortName: 'USD/INR',
      currentPrice: 83.9,
      previousClose: 83.82,
      change: 0.08,
      changePercent: 0.1,
      instrumentType: 'CURRENCY',
      displayOrder: 5,
      lastUpdated: new Date().toISOString(),
      formattedPrice: '83.9',
      formattedChange: '+0.1%',
      isPositive: true
    },
    {
      symbol: '^INDIAVIX',
      displayName: 'INDIA VIX',
      shortName: 'INDIA VIX',
      currentPrice: 15.8,
      previousClose: 15.2,
      change: 0.6,
      changePercent: 3.9,
      instrumentType: 'INDEX',
      displayOrder: 6,
      lastUpdated: new Date().toISOString(),
      formattedPrice: '15.8',
      formattedChange: '+3.9%',
      isPositive: true
    }
  ];

  getTickerQuotes(forceRefresh = false): Observable<MarketTickerQuote[]> {
    if (!forceRefresh && this.cachedQuotes && this.cachedQuotes.length > 0) {
      return of(this.cachedQuotes);
    }

    const url = forceRefresh ? `${this.apiUrl}?forceRefresh=true` : this.apiUrl;
    return this.http.get<MarketTickerQuote[]>(url).pipe(
      map(quotes => {
        if (!quotes || quotes.length === 0) {
          return this.defaultFallbackQuotes;
        }
        return quotes.map(q => ({
          ...q,
          isPositive: (q.change ?? 0) >= 0
        }));
      }),
      tap(quotes => this.cachedQuotes = quotes),
      catchError(err => {
        console.warn('Could not fetch market ticker quotes from API, using fallback data:', err);
        return of(this.cachedQuotes || this.defaultFallbackQuotes);
      })
    );
  }

  getExploreLensData(lens: string = 'Growth', forceRefresh = false): Observable<import('../models/market-ticker.model').ExploreLensResponse> {
    const url = `${environment.apiUrl}/api/market/explore-lens?lens=${encodeURIComponent(lens)}${forceRefresh ? '&forceRefresh=true' : ''}`;
    return this.http.get<import('../models/market-ticker.model').ExploreLensResponse>(url);
  }
}
