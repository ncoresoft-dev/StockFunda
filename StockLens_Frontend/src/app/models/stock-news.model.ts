export interface Company {
  id: number;
  symbol: string;
  companyName: string;
  industry?: string;
  logoUrl?: string;
}

export interface Stock {
  id: number;
  symbol: string;
  companyName: string;
  exchange: string;
  industry?: string;
}

export interface StockNewsItem {
  id: number;
  title: string;
  description?: string;
  content?: string;
  sourceName: string;
  sourceUrl: string;
  articleUrl?: string;
  imageUrl?: string;
  publishedAt: string;
  category?: string;
  externalNewsId?: string;
}

export interface StockNewsResponse {
  stockId: number;
  symbol: string;
  exchange: string;
  companyName: string;
  news: StockNewsItem[];
  page: number;
  limit: number;
  lastFetchedAt?: string;
}

export interface MarketNewsItem {
  id: string | number;
  title: string;
  description?: string;
  content?: string;
  sourceName: string;
  sourceUrl: string;
  articleUrl: string;
  imageUrl?: string;
  publishedAt: string;
  category: string;
  timeAgo?: string;
  isHot?: boolean;
}

export interface MarketNewsResponse {
  news: MarketNewsItem[];
  totalCount: number;
  lastFetchedAt: string;
  isCached: boolean;
  status: string;
  message?: string;
  query: string;
  nextRetryInMinutes?: number;
  availableCategories?: string[];
}

export type LoadingState = 'idle' | 'loading' | 'success' | 'empty' | 'error';
