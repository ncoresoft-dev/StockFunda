import { Injectable, signal } from '@angular/core';

export interface RecentStockItem {
  symbol: string;
  name: string;
  sector: string;
  price?: number;
  formattedPrice: string;
  marketCap?: number;
  formattedMarketCap: string;
  avatarLetter: string;
  avatarBg: string;
  avatarColor: string;
  logoUrl?: string;
  timestamp?: number;
}

const STORAGE_KEY = 'stocklens_recent_views';
const MAX_RECENT_STOCKS = 6;
const INITIAL_SEED_SYMBOLS = ['TCS', 'RELIANCE', 'HDFCBANK'];

const PALETTES = [
  { bg: '#cff4e6', color: '#0f766e' },
  { bg: '#fed7aa', color: '#9a3412' },
  { bg: '#fde2e4', color: '#9f1239' },
  { bg: '#e0e7ff', color: '#3730a3' },
  { bg: '#fef08a', color: '#854d0e' },
  { bg: '#bee6fd', color: '#0369a1' },
  { bg: '#fae8ff', color: '#86198f' },
  { bg: '#fee2e2', color: '#991b1b' }
];

@Injectable({
  providedIn: 'root'
})
export class RecentResearchService {
  // Reactive signal of recently searched/viewed symbols in browser history (capped at max 6)
  readonly recentSymbols = signal<string[]>(this.loadFromStorage());

  /**
   * Returns current recent symbols array
   */
  getRecentSymbols(): string[] {
    return this.recentSymbols();
  }

  /**
   * Adds or bumps a stock symbol in the user's browser history (localStorage).
   * Safely caps the history to a maximum of 6 stocks.
   */
  addRecentSymbol(symbol: string): void {
    if (!symbol) return;
    const cleanSym = symbol.trim().toUpperCase();
    if (!cleanSym) return;

    const current = this.recentSymbols();
    const filtered = current.filter(s => s !== cleanSym);
    const updated = [cleanSym, ...filtered].slice(0, MAX_RECENT_STOCKS);

    this.recentSymbols.set(updated);
    this.saveToStorage(updated);
  }

  /**
   * Backwards compatible helper
   */
  addRecentView(stock: { symbol: string }): void {
    if (stock && stock.symbol) {
      this.addRecentSymbol(stock.symbol);
    }
  }

  /**
   * Deterministically compute an avatar palette from a stock symbol
   */
  computePalette(symbol: string): { bg: string; color: string } {
    let hash = 0;
    for (let i = 0; i < symbol.length; i++) {
      hash = symbol.charCodeAt(i) + ((hash << 5) - hash);
    }
    const idx = Math.abs(hash) % PALETTES.length;
    return PALETTES[idx];
  }

  private loadFromStorage(): string[] {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw) {
          const parsed = JSON.parse(raw);
          if (Array.isArray(parsed) && parsed.length > 0) {
            const list = parsed
              .map(item => (typeof item === 'string' ? item : item?.symbol))
              .filter((sym): sym is string => typeof sym === 'string' && sym.trim().length > 0)
              .map(s => s.trim().toUpperCase());

            if (list.length > 0) {
              return list.slice(0, MAX_RECENT_STOCKS);
            }
          }
        }
      }
    } catch (e) {
      console.warn('Could not read recent stocks history from localStorage:', e);
    }

    return INITIAL_SEED_SYMBOLS.slice(0, MAX_RECENT_STOCKS);
  }

  private saveToStorage(symbols: string[]): void {
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(symbols.slice(0, MAX_RECENT_STOCKS)));
      }
    } catch (e) {
      console.warn('Could not save recent stocks history to localStorage:', e);
    }
  }
}
