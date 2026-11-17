import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError, map, tap, shareReplay } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { Nifty50StockItem, IndexAttributionResponse } from '../models/stock-index.model';

@Injectable({
  providedIn: 'root'
})
export class StockIndexService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/api/v1/indices`;
  
  // Cache to avoid hitting API constantly on tab switches
  private cachedNifty50List: Nifty50StockItem[] | null = null;
  private inFlightNifty50$: Observable<Nifty50StockItem[]> | null = null;
  private cachedSensexList: Nifty50StockItem[] | null = null;
  private inFlightSensex$: Observable<Nifty50StockItem[]> | null = null;
  private cachedBankNiftyList: Nifty50StockItem[] | null = null;
  private inFlightBankNifty$: Observable<Nifty50StockItem[]> | null = null;
  private inFlightSymbolsMap = new Map<string, Observable<Nifty50StockItem[]>>();

  /**
   * Returns NIFTY 50 constituent companies for the UI
   */
  getNifty50List(forceRefresh = false): Observable<Nifty50StockItem[]> {
    if (!forceRefresh && this.cachedNifty50List && this.cachedNifty50List.length > 0) {
      return of(this.cachedNifty50List);
    }

    if (!forceRefresh && this.inFlightNifty50$) {
      return this.inFlightNifty50$;
    }

    this.inFlightNifty50$ = this.http.get<any[]>(`${this.apiUrl}/nifty-50`).pipe(
      map(data => {
        const mapped = (data || []).map(item => ({
          symbol: item.symbol,
          companyName: item.companyName || item.company_name,
          sector: item.sector,
          logoUrl: item.logoUrl || item.logo_url,
          websiteUrl: item.websiteUrl || item.website_url,
          price: item.price,
          marketCap: item.marketCap ?? item.market_cap,
          profitGrowthYoy: item.profitGrowthYoy ?? item.profit_growth_yoy,
          weightagePercentage: item.weightagePercentage ?? item.weightage_percentage,
          exchange: 'NSE'
        } as Nifty50StockItem));

        const totalCap = mapped.reduce((sum, s) => sum + (s.marketCap || 0), 0);
        if (totalCap > 0) {
          mapped.forEach(s => {
            if (s.weightagePercentage === undefined && s.marketCap) {
              s.weightagePercentage = Math.round(((s.marketCap) / totalCap) * 10000) / 100;
            }
          });
        }
        return mapped;
      }),
      tap(mappedData => {
        this.cachedNifty50List = mappedData;
        this.inFlightNifty50$ = null;
      }),
      catchError(error => {
        console.error('Error fetching Nifty 50 companies:', error);
        this.inFlightNifty50$ = null;
        return of(this.cachedNifty50List || []);
      }),
      shareReplay(1)
    );

    return this.inFlightNifty50$;
  }

  /**
   * Returns BSE SENSEX constituent companies for the UI
   */
  getSensexList(forceRefresh = false): Observable<Nifty50StockItem[]> {
    if (!forceRefresh && this.cachedSensexList && this.cachedSensexList.length > 0) {
      return of(this.cachedSensexList);
    }

    if (!forceRefresh && this.inFlightSensex$) {
      return this.inFlightSensex$;
    }

    this.inFlightSensex$ = this.http.get<any[]>(`${this.apiUrl}/sensex`).pipe(
      map(data => {
        const mapped = (data || []).map(item => ({
          symbol: item.symbol,
          companyName: item.companyName || item.company_name,
          sector: item.sector || 'Equities',
          logoUrl: item.logoUrl || item.logo_url,
          websiteUrl: item.websiteUrl || item.website_url,
          price: item.price,
          marketCap: item.marketCap ?? item.market_cap,
          profitGrowthYoy: item.profitGrowthYoy ?? item.profit_growth_yoy,
          weightagePercentage: item.weightagePercentage ?? item.weightage_percentage,
          exchange: 'BSE'
        } as Nifty50StockItem));

        if (this.cachedNifty50List && this.cachedNifty50List.length > 0) {
          const niftyMap = new Map(this.cachedNifty50List.map(s => [s.symbol.toUpperCase(), s]));
          mapped.forEach(s => {
            const nStock = niftyMap.get(s.symbol.toUpperCase());
            if (nStock) {
              if (s.price == null && nStock.price != null) s.price = nStock.price;
              if (s.marketCap == null && nStock.marketCap != null) s.marketCap = nStock.marketCap;
              if (!s.logoUrl && nStock.logoUrl) s.logoUrl = nStock.logoUrl;
            }
          });
        }

        const totalCap = mapped.reduce((sum, s) => sum + (s.marketCap || 0), 0);
        if (totalCap > 0) {
          mapped.forEach(s => {
            if (s.weightagePercentage === undefined && s.marketCap) {
              s.weightagePercentage = Math.round(((s.marketCap) / totalCap) * 10000) / 100;
            }
          });
        }
        return mapped;
      }),
      tap(mappedData => {
        this.cachedSensexList = mappedData;
        this.inFlightSensex$ = null;
      }),
      catchError(error => {
        console.error('Error fetching BSE Sensex companies:', error);
        this.inFlightSensex$ = null;
        return of(this.cachedSensexList || []);
      }),
      shareReplay(1)
    );

    return this.inFlightSensex$;
  }

  /**
   * Returns NIFTY BANK constituent companies for the UI
   */
  getBankNiftyList(forceRefresh = false): Observable<Nifty50StockItem[]> {
    if (!forceRefresh && this.cachedBankNiftyList && this.cachedBankNiftyList.length > 0) {
      return of(this.cachedBankNiftyList);
    }

    if (!forceRefresh && this.inFlightBankNifty$) {
      return this.inFlightBankNifty$;
    }

    this.inFlightBankNifty$ = this.http.get<any[]>(`${this.apiUrl}/bank-nifty`).pipe(
      map(data => {
        const mapped = (data || []).map(item => ({
          symbol: item.symbol,
          companyName: item.companyName || item.company_name,
          sector: item.sector || 'Financial Services',
          logoUrl: item.logoUrl || item.logo_url,
          websiteUrl: item.websiteUrl || item.website_url,
          price: item.price,
          marketCap: item.marketCap ?? item.market_cap,
          profitGrowthYoy: item.profitGrowthYoy ?? item.profit_growth_yoy,
          weightagePercentage: item.weightagePercentage ?? item.weightage_percentage,
          exchange: 'NSE'
        } as Nifty50StockItem));

        if (this.cachedNifty50List && this.cachedNifty50List.length > 0) {
          const niftyMap = new Map(this.cachedNifty50List.map(s => [s.symbol.toUpperCase(), s]));
          mapped.forEach(s => {
            const nStock = niftyMap.get(s.symbol.toUpperCase());
            if (nStock) {
              if (s.price == null && nStock.price != null) s.price = nStock.price;
              if (s.marketCap == null && nStock.marketCap != null) s.marketCap = nStock.marketCap;
              if (!s.logoUrl && nStock.logoUrl) s.logoUrl = nStock.logoUrl;
            }
          });
        }

        const totalCap = mapped.reduce((sum, s) => sum + (s.marketCap || 0), 0);
        if (totalCap > 0) {
          mapped.forEach(s => {
            if (s.weightagePercentage === undefined && s.marketCap) {
              s.weightagePercentage = Math.round(((s.marketCap) / totalCap) * 10000) / 100;
            }
          });
        }
        return mapped;
      }),
      tap(mappedData => {
        this.cachedBankNiftyList = mappedData;
        this.inFlightBankNifty$ = null;
      }),
      catchError(error => {
        console.error('Error fetching Bank Nifty companies:', error);
        this.inFlightBankNifty$ = null;
        return of(this.cachedBankNiftyList || []);
      }),
      shareReplay(1)
    );

    return this.inFlightBankNifty$;
  }

  /**
   * Updates price and market cap for a symbol in local cache
   */
  updateStockInCache(symbol: string, price?: number | null, marketCap?: number | null): void {
    if (!symbol) return;
    const cleanSym = symbol.trim().toUpperCase();

    const updateItem = (item?: Nifty50StockItem) => {
      if (item) {
        if (price !== undefined && price !== null && price > 0) {
          item.price = price;
        }
        if (marketCap !== undefined && marketCap !== null && marketCap > 0) {
          item.marketCap = marketCap;
        }
      }
    };

    if (this.cachedNifty50List) {
      updateItem(this.cachedNifty50List.find(s => s.symbol?.toUpperCase() === cleanSym));
    }
    if (this.cachedSensexList) {
      updateItem(this.cachedSensexList.find(s => s.symbol?.toUpperCase() === cleanSym));
    }
    if (this.cachedBankNiftyList) {
      updateItem(this.cachedBankNiftyList.find(s => s.symbol?.toUpperCase() === cleanSym));
    }
  }

  /**
   * Clears in-memory cache so next call always hits API
   */
  clearCache(): void {
    this.cachedNifty50List = null;
    this.inFlightNifty50$ = null;
    this.cachedSensexList = null;
    this.inFlightSensex$ = null;
    this.cachedBankNiftyList = null;
    this.inFlightBankNifty$ = null;
    this.inFlightSymbolsMap.clear();
  }

  /**
   * Retrieves a cached stock item if present across Nifty 50, Sensex, or Bank Nifty
   */
  getCachedStockBySymbol(symbol: string): Nifty50StockItem | undefined {
    if (!symbol) return undefined;
    const clean = symbol.trim().toUpperCase();
    return (
      this.cachedNifty50List?.find(s => s.symbol.toUpperCase() === clean) ||
      this.cachedSensexList?.find(s => s.symbol.toUpperCase() === clean) ||
      this.cachedBankNiftyList?.find(s => s.symbol.toUpperCase() === clean)
    );
  }

  /**
   * Finds stock items matching a list of symbols (for Watchlist tab or custom queries).
   * Queries the dedicated backend endpoint so non-Nifty stocks (e.g. BANDHANBNK) return real logo, CMP, and market cap.
   */
  getStocksBySymbols(symbols: string[]): Observable<Nifty50StockItem[]> {
    if (!symbols || symbols.length === 0) {
      return of([]);
    }

    const cleanSymbols = Array.from(new Set(symbols.map(s => s.trim().toUpperCase()))).filter(Boolean).sort();
    const joined = cleanSymbols.join(',');

    if (this.inFlightSymbolsMap.has(joined)) {
      return this.inFlightSymbolsMap.get(joined)!;
    }

    const obs$ = this.http.get<any[]>(`${this.apiUrl}/stocks-by-symbols?symbols=${encodeURIComponent(joined)}`).pipe(
      map(data => data.map(item => ({
        symbol: item.symbol,
        companyName: item.companyName || item.company_name,
        sector: item.sector || 'Equities',
        logoUrl: item.logoUrl || item.logo_url,
        websiteUrl: item.websiteUrl || item.website_url,
        price: item.price,
        marketCap: item.marketCap ?? item.market_cap,
        change: item.change,
        changePercent: item.changePercent ?? item.change_percent,
        isPositive: item.isPositive ?? item.is_positive ?? (item.change_percent != null ? item.change_percent >= 0 : (item.change != null ? item.change >= 0 : true)),
        trendPath: item.trendPath || item.trend_path,
        sevenDayPrices: item.sevenDayPrices || item.seven_day_prices,
        exchange: 'NSE'
      } as Nifty50StockItem))),
      tap(() => {
        this.inFlightSymbolsMap.delete(joined);
      }),
      catchError(error => {
        console.warn('Error fetching stocks by symbols from API, falling back to cache:', error);
        this.inFlightSymbolsMap.delete(joined);
        if (this.cachedNifty50List) {
          return of(this.cachedNifty50List.filter(s => cleanSymbols.includes(s.symbol?.toUpperCase())));
        }
        return of([]);
      }),
      shareReplay(1)
    );

    this.inFlightSymbolsMap.set(joined, obs$);
    return obs$;
  }

  private attributionCache = new Map<string, IndexAttributionResponse>();

  /**
   * Retrieves index attribution analytics (Who Moved NIFTY, Tug of War, Donut Slices, Contributors)
   */
  getIndexAttribution(indexKey: string, forceRefresh = false): Observable<IndexAttributionResponse | null> {
    const cleanKey = (indexKey || 'nifty-50').trim().toLowerCase();

    if (!forceRefresh && this.attributionCache.has(cleanKey)) {
      return of(this.attributionCache.get(cleanKey)!);
    }

    const url = `${this.apiUrl}/attribution/${encodeURIComponent(cleanKey)}${forceRefresh ? '?forceRefresh=true' : ''}`;
    return this.http.get<IndexAttributionResponse>(url).pipe(
      tap(data => {
        if (data) {
          this.attributionCache.set(cleanKey, data);
        }
      }),
      catchError(err => {
        console.warn(`Could not fetch index attribution for ${cleanKey}:`, err);
        return of(this.attributionCache.get(cleanKey) || null);
      })
    );
  }
}

