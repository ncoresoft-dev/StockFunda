import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
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

  private readonly pulseCache = new Map<string, MarketPulseResponse>();

  private getFallbackPulseData(timeframe: string): MarketPulseResponse {
    const cleanTf = (timeframe || '1D').toUpperCase();

    if (cleanTf === '1W') {
      return {
        timeframe: '1W',
        marketSummary: 'Market ek line mein: weekly trend bullish, steady institutional buying across large caps.',
        sectorMapRoute: '/sector-analysis',
        lastUpdated: new Date().toISOString(),
        indices: [
          {
            symbol: '^NSEI',
            name: 'Nifty 50',
            currentPrice: 25291.10,
            formattedPrice: '25,291.10',
            change: 362.40,
            formattedPointsChange: '+362.4 pts',
            changePercent: 1.45,
            formattedChangePercent: '+1.45%',
            isPositive: true,
            dayLow: 24980,
            formattedDayLow: '24,980',
            dayHigh: 25350,
            formattedDayHigh: '25,350',
            rangeProgressPct: 84.1,
            sparklinePrices: [24980, 25050, 25120, 25090, 25180, 25220, 25270, 25291],
            trendPath: 'M 4 44 L 35 38 L 70 30 L 105 34 L 140 22 L 175 16 L 210 10 L 236 8',
            takeawayNote: 'Weekly momentum positive; steady accumulation in large caps.'
          },
          {
            symbol: '^BSESN',
            name: 'Sensex',
            currentPrice: 82104.79,
            formattedPrice: '82,104.79',
            change: 1180.50,
            formattedPointsChange: '+1,180.5 pts',
            changePercent: 1.46,
            formattedChangePercent: '+1.46%',
            isPositive: true,
            dayLow: 80950,
            formattedDayLow: '80,950',
            dayHigh: 82380,
            formattedDayHigh: '82,380',
            rangeProgressPct: 80.7,
            sparklinePrices: [80950, 81150, 81380, 81520, 81700, 81890, 82020, 82105],
            trendPath: 'M 4 45 L 35 40 L 70 32 L 105 26 L 140 20 L 175 14 L 210 10 L 236 7',
            takeawayNote: 'Weekly gain +1.46%; BSE heavyweights extend upward march.'
          },
          {
            symbol: '^NSEBANK',
            name: 'Bank Nifty',
            currentPrice: 61575.90,
            formattedPrice: '61,575.90',
            change: 1210.80,
            formattedPointsChange: '+1,210.8 pts',
            changePercent: 2.01,
            formattedChangePercent: '+2.01%',
            isPositive: true,
            dayLow: 60320,
            formattedDayLow: '60,320',
            dayHigh: 61750,
            formattedDayHigh: '61,750',
            rangeProgressPct: 87.8,
            sparklinePrices: [60320, 60550, 60890, 61120, 61300, 61440, 61520, 61576],
            trendPath: 'M 4 46 L 35 42 L 70 30 L 105 22 L 140 16 L 175 12 L 210 8 L 236 6',
            takeawayNote: 'Weekly breakout +2.01%; private and PSU banks surge.'
          }
        ]
      };
    }

    if (cleanTf === '1M') {
      return {
        timeframe: '1M',
        marketSummary: 'Market ek line mein: monthly rally intact, major indices up 3%+ driven by banking and IT.',
        sectorMapRoute: '/sector-analysis',
        lastUpdated: new Date().toISOString(),
        indices: [
          {
            symbol: '^NSEI',
            name: 'Nifty 50',
            currentPrice: 25291.10,
            formattedPrice: '25,291.10',
            change: 794.60,
            formattedPointsChange: '+794.6 pts',
            changePercent: 3.24,
            formattedChangePercent: '+3.24%',
            isPositive: true,
            dayLow: 24450,
            formattedDayLow: '24,450',
            dayHigh: 25420,
            formattedDayHigh: '25,420',
            rangeProgressPct: 86.7,
            sparklinePrices: [24450, 24580, 24690, 24810, 24920, 25100, 25210, 25291],
            trendPath: 'M 4 46 L 35 40 L 70 34 L 105 28 L 140 22 L 175 14 L 210 9 L 236 6',
            takeawayNote: 'Monthly trend strongly bullish; benchmarks up over 3%.'
          },
          {
            symbol: '^BSESN',
            name: 'Sensex',
            currentPrice: 82104.79,
            formattedPrice: '82,104.79',
            change: 2540.20,
            formattedPointsChange: '+2,540.2 pts',
            changePercent: 3.19,
            formattedChangePercent: '+3.19%',
            isPositive: true,
            dayLow: 79450,
            formattedDayLow: '79,450',
            dayHigh: 82450,
            formattedDayHigh: '82,450',
            rangeProgressPct: 88.5,
            sparklinePrices: [79450, 79890, 80420, 80910, 81400, 81750, 81990, 82105],
            trendPath: 'M 4 46 L 35 41 L 70 33 L 105 25 L 140 18 L 175 12 L 210 8 L 236 6',
            takeawayNote: 'Monthly gain +3.19%; broader indices tracking all-time highs.'
          },
          {
            symbol: '^NSEBANK',
            name: 'Bank Nifty',
            currentPrice: 61575.90,
            formattedPrice: '61,575.90',
            change: 2460.50,
            formattedPointsChange: '+2,460.5 pts',
            changePercent: 4.16,
            formattedChangePercent: '+4.16%',
            isPositive: true,
            dayLow: 59100,
            formattedDayLow: '59,100',
            dayHigh: 61850,
            formattedDayHigh: '61,850',
            rangeProgressPct: 90.0,
            sparklinePrices: [59100, 59520, 60100, 60650, 61100, 61380, 61510, 61576],
            trendPath: 'M 4 47 L 35 41 L 70 30 L 105 20 L 140 13 L 175 9 L 210 6 L 236 5',
            takeawayNote: 'Monthly outperformance +4.16%; banking sector anchors rally.'
          }
        ]
      };
    }

    // Default 1D
    return {
      timeframe: '1D',
      marketSummary: 'Market ek line mein: broad market strong, all major benchmark indices trading in green.',
      sectorMapRoute: '/sector-analysis',
      lastUpdated: new Date().toISOString(),
      indices: [
        {
          symbol: '^NSEI',
          name: 'Nifty 50',
          currentPrice: 25291.10,
          formattedPrice: '25,291.10',
          change: 145.80,
          formattedPointsChange: '+145.8 pts',
          changePercent: 0.58,
          formattedChangePercent: '+0.58%',
          isPositive: true,
          dayLow: 25172,
          formattedDayLow: '25,172',
          dayHigh: 25338,
          formattedDayHigh: '25,338',
          rangeProgressPct: 71.7,
          sparklinePrices: [25180, 25210, 25230, 25200, 25260, 25280, 25270, 25291],
          trendPath: 'M 4 38 L 35 34 L 70 30 L 105 35 L 140 22 L 175 16 L 210 18 L 236 12',
          takeawayNote: 'IT and auto lead; 36 of 50 stocks up.'
        },
        {
          symbol: '^BSESN',
          name: 'Sensex',
          currentPrice: 82104.79,
          formattedPrice: '82,104.79',
          change: 514.00,
          formattedPointsChange: '+514.0 pts',
          changePercent: 0.63,
          formattedChangePercent: '+0.63%',
          isPositive: true,
          dayLow: 81763,
          formattedDayLow: '81,763',
          dayHigh: 82312,
          formattedDayHigh: '82,312',
          rangeProgressPct: 62.3,
          sparklinePrices: [81780, 81890, 81950, 81920, 82030, 82080, 82060, 82105],
          trendPath: 'M 4 39 L 35 32 L 70 27 L 105 30 L 140 20 L 175 15 L 210 17 L 236 12',
          takeawayNote: 'Large caps drive the move; BSE breadth positive.'
        },
        {
          symbol: '^NSEBANK',
          name: 'Bank Nifty',
          currentPrice: 61575.90,
          formattedPrice: '61,575.90',
          change: 543.20,
          formattedPointsChange: '+543.2 pts',
          changePercent: 0.89,
          formattedChangePercent: '+0.89%',
          isPositive: true,
          dayLow: 61213,
          formattedDayLow: '61,213',
          dayHigh: 61713,
          formattedDayHigh: '61,713',
          rangeProgressPct: 72.6,
          sparklinePrices: [61240, 61350, 61420, 61390, 61490, 61530, 61540, 61576],
          trendPath: 'M 4 39 L 35 30 L 70 23 L 105 27 L 140 18 L 175 14 L 210 13 L 236 10',
          takeawayNote: 'Banking leads rally; private banks showing strong inflows.'
        }
      ]
    };
  }

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
        console.warn('Could not fetch market pulse data from API, using fallback data:', err);
        const fallback = this.getFallbackPulseData(cleanTf);
        this.pulseCache.set(cleanTf, fallback);
        return of(fallback);
      })
    );
  }
}
