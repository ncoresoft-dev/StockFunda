export interface DealItem {
  dealDate: string;
  clientName: string;
  action: string;
  quantity: number;
  averagePrice: number;
}

export interface InsiderTradeItem {
  intimationDate: string;
  acquirerName: string;
  personCategory: string;
  isPromoter: boolean;
  transactionType: string;
  quantity: number;
  totalValue: number;
  sharesAfterPct: number;
  mode: string;
}

export interface StockDealsSummary {
  symbol: string;
  bulkDeals: DealItem[];
  blockDeals: DealItem[];
  insiderTrades: InsiderTradeItem[];
  errorMessage?: string;
}
