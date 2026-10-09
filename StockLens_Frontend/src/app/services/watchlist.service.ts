import { Injectable, signal, computed } from '@angular/core';

const WATCHLIST_STORAGE_KEY = 'stocklens_watchlist';

@Injectable({
  providedIn: 'root'
})
export class WatchlistService {
  // Reactive signal containing the list of starred stock symbols
  readonly watchlistSymbols = signal<string[]>(this.loadFromStorage());

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
   * Checks if a symbol is in the user's personal watchlist
   */
  isInWatchlist(symbol: string): boolean {
    if (!symbol) return false;
    return this.watchlistSet().has(symbol.toUpperCase());
  }

  /**
   * Adds a symbol to the user's personal watchlist
   */
  addToWatchlist(symbol: string): void {
    if (!symbol) return;
    const clean = symbol.trim().toUpperCase();
    const current = this.watchlistSymbols();
    if (!current.includes(clean)) {
      const updated = [...current, clean];
      this.watchlistSymbols.set(updated);
      this.saveToStorage(updated);
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
  toggleWatchlist(symbol: string): boolean {
    if (!symbol) return false;
    const clean = symbol.trim().toUpperCase();
    if (this.isInWatchlist(clean)) {
      this.removeFromWatchlist(clean);
      return false;
    } else {
      this.addToWatchlist(clean);
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
}
