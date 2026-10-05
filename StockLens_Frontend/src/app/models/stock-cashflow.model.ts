export type LoadingState = 'idle' | 'loading' | 'success' | 'empty' | 'error';

export interface CashflowYoYChange {
  freeCashFlowChange?: number | null;
  freeCashFlowGrowth?: number | null; // YoY %
  operatingCashFlowChange?: number | null;
  operatingCashFlowGrowth?: number | null; // YoY %
  capexChange?: number | null;
  capexGrowth?: number | null; // YoY %
  netProfitChange?: number | null;
  netProfitGrowth?: number | null; // YoY %
  epsChange?: number | null;
  epsGrowth?: number | null; // YoY %
  revenueChange?: number | null;
  revenueGrowth?: number | null; // YoY %
  netCashFlowChange?: number | null;
  netCashFlowGrowth?: number | null; // YoY %
}

export interface StockRatios {
  roe?: number | null;
  roeYoY?: number | null;
  roeChange?: number | null;
  roePrevYear?: number | null;
  roce?: number | null;
  roceYoY?: number | null;
  roceChange?: number | null;
  rocePrevYear?: number | null;
  peRatio?: number | null;
  ttmEps?: number | null;
  pbRatio?: number | null;
  dividendYield?: number | null;
  week52High?: number | null;
  week52Low?: number | null;
  currentPrice?: number | null;
  asOfDate?: string | null;
  financialsPeriodType?: string | null;
  financialsFiscalYear?: string | null;
  faceValue?: number | null;
  equityCapital?: number | null;
  totalShares?: number | null;
  totalEquity?: number | null;
  totalEquityPeriod?: string | null;
  totalEquitySource?: string | null;
  bookValue?: number | null;
  marketCap?: number | null;
  marketCapSource?: string | null;
  sectorPe?: number | null;
  sectorPeSector?: string | null;
  sectorPeAsOfDate?: string | null;
  pegRatio?: number | null;
  debtorDays?: number | null;
  debtorDaysYoY?: number | null;
  inventoryDays?: number | null;
  inventoryDaysYoY?: number | null;
  payableDays?: number | null;
  payableDaysYoY?: number | null;
}


export interface CashflowSummary {
  fiscalYear: string;
  periodEndDate?: string | null;
  operatingCashFlow?: number | null;
  capex?: number | null;
  freeCashFlow?: number | null;
  netCashFlow?: number | null;
  revenue?: number | null;
  operatingProfit?: number | null;
  netProfit?: number | null;
  eps?: number | null;
  interest?: number | null;
  depreciation?: number | null;
  otherEquity?: number | null;
  totalEquity?: number | null;
  cfoToOperatingProfitRatio?: number | null;
  cfoToNetProfitRatio?: number | null;
  fcfMarginPercent?: number | null;
  capexToCfoPercent?: number | null;
  consolidationType?: string | null;
  ratios?: StockRatios | null;
  yoYChange: CashflowYoYChange;
}

export interface StockCashflowResponse {
  stockId: number;
  symbol: string;
  exchange: string;
  companyName: string;
  latestFiscalYear: string;
  dataAsOf?: string | null;
  source: string;
  lastSyncedAt: string;
  summary: CashflowSummary;
  ratios?: StockRatios | null;
}

