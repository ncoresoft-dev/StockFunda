import { Injectable, signal, computed } from '@angular/core';

const WATCHLIST_STORAGE_KEY = 'stocklens_watchlist';
const WATCHLIST_META_STORAGE_KEY = 'stocklens_watchlist_meta';

export interface WatchlistStockMeta {
  symbol: string;
  companyName?: string;
  logoUrl?: string;
  price?: number;
  marketCap?: number;
  sector?: string;
}

@Injectable({
  providedIn: 'root'
})
export class WatchlistService {
  // Reactive signal containing the list of starred stock symbols
  readonly watchlistSymbols = signal<string[]>(this.loadFromStorage());

  // Cached metadata for stocks in watchlist
  readonly metaMap = signal<Record<string, WatchlistStockMeta>>(this.loadMetaFromStorage());

  // Set for O(1) membership lookup
  readonly watchlistSet = computed(() => new Set(this.watchlistSymbols().map(s => s.toUpperCase())));

  // Computed count of tracked stocks
  readonly watchlistCount = computed(() => this.watchlistSymbols().length);

  /**
   * Returns current watchlist symbols array
   */
  getWatchlist(): string[] {
    return this.watchlistSymbols();
  }

  /**
   * Returns cached metadata for a symbol if available
   */
  getMeta(symbol: string): WatchlistStockMeta | undefined {
    if (!symbol) return undefined;
    return this.metaMap()[symbol.toUpperCase()];
  }

  /**
   * Saves or merges metadata for a symbol
   */
  setMeta(symbol: string, meta: Partial<WatchlistStockMeta>): void {
    if (!symbol) return;
    const clean = symbol.trim().toUpperCase();
    const existing = this.metaMap()[clean] || { symbol: clean };
    const merged: WatchlistStockMeta = {
      ...existing,
      ...meta,
      symbol: clean
    };
    const updated = { ...this.metaMap(), [clean]: merged };
    this.metaMap.set(updated);
    this.saveMetaToStorage(updated);
  }

  /**
   * Checks if a symbol is in the user's personal watchlist
   */
  isInWatchlist(symbol: string): boolean {
    if (!symbol) return false;
    return this.watchlistSet().has(symbol.toUpperCase());
  }

  /**
   * Adds a symbol to the user's personal watchlist with optional metadata
   */
  addToWatchlist(symbol: string, meta?: Partial<WatchlistStockMeta>): void {
    if (!symbol) return;
    const clean = symbol.trim().toUpperCase();
    const current = this.watchlistSymbols();
    if (!current.includes(clean)) {
      const updated = [...current, clean];
      this.watchlistSymbols.set(updated);
      this.saveToStorage(updated);
    }
    if (meta) {
      this.setMeta(clean, meta);
    }
  }

  /**
   * Removes a symbol from the user's personal watchlist
   */
  removeFromWatchlist(symbol: string): void {
    if (!symbol) return;
    const clean = symbol.trim().toUpperCase();
    const current = this.watchlistSymbols();
    if (current.includes(clean)) {
      const updated = current.filter(s => s !== clean);
      this.watchlistSymbols.set(updated);
      this.saveToStorage(updated);
    }
  }

  /**
   * Toggles the presence of a symbol in the personal watchlist.
   * Returns true if newly added, false if removed.
   */
  toggleWatchlist(symbol: string, meta?: Partial<WatchlistStockMeta>): boolean {
    if (!symbol) return false;
    const clean = symbol.trim().toUpperCase();
    if (this.isInWatchlist(clean)) {
      this.removeFromWatchlist(clean);
      return false;
    } else {
      this.addToWatchlist(clean, meta);
      return true;
    }
  }

  private loadFromStorage(): string[] {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        const data = localStorage.getItem(WATCHLIST_STORAGE_KEY);
        if (data) {
          const parsed = JSON.parse(data);
          if (Array.isArray(parsed)) {
            return parsed.map((s: string) => s.toUpperCase());
          }
        }
      }
    } catch (e) {
      console.warn('Could not read watchlist from localStorage:', e);
    }
    // Default initial seed or empty list
    return ['RELIANCE', 'TCS', 'INFY', 'HDFCBANK'];
  }

  private saveToStorage(symbols: string[]): void {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        localStorage.setItem(WATCHLIST_STORAGE_KEY, JSON.stringify(symbols));
      }
    } catch (e) {
      console.warn('Could not write watchlist to localStorage:', e);
    }
  }

  private loadMetaFromStorage(): Record<string, WatchlistStockMeta> {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        const data = localStorage.getItem(WATCHLIST_META_STORAGE_KEY);
        if (data) {
          return JSON.parse(data);
        }
      }
    } catch (e) {
      console.warn('Could not read watchlist meta from localStorage:', e);
    }
    return {};
  }

  private saveMetaToStorage(meta: Record<string, WatchlistStockMeta>): void {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        localStorage.setItem(WATCHLIST_META_STORAGE_KEY, JSON.stringify(meta));
      }
    } catch (e) {
      console.warn('Could not write watchlist meta to localStorage:', e);
    }
  }
}
