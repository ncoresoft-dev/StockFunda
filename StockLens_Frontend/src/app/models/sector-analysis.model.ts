export interface SectorSummary {
  sectorName: string;
  description: string;
  totalCompanies: number;
  averageMarketCapCr: number;
  advanceCount: number;
  declineCount: number;
  neutralCount: number;
  advanceDeclineRatio: number;
  sectorChangePercent: number;
  averagePe: number;
  averagePb: number;
  averageRoe: number;
  averageRoce: number;
  sectorScore: number;
  sectorRank: number;
  totalSectors: number;
}

export interface SectorStock {
  symbol: string;
  companyName: string;
  price: number;
  change: number;
  changePercent: number;
  marketCapCr: number;
  peRatio?: number | null;
  pbRatio?: number | null;
  roe?: number | null;
  roce?: number | null;
  score: number;
  primaryBadge?: string | null;
  badges: string[];
  logoUrl?: string | null;
  // Treemap weight value
  value: number;
}

export interface SectorAnalysisResponse {
  summary: SectorSummary;
  stocks: SectorStock[];
  topGainers: SectorStock[];
  topLosers: SectorStock[];
  bestOverallStock?: SectorStock | null;
  availableSectors: string[];
}

export interface StockFlowItem {
  symbol: string;
  companyName: string;
  sector: string;
  price: number;
  changePercent: number;
  tradedValueCr: number;
  flowCr: number;
  weight: number;
  clv: number;
  relativeVolume: number;
  speed: number;
  isSurge: boolean;
  isFo: boolean;
  buildup?: string | null;
  logoUrl?: string | null;
}

export interface SectorFlowItem {
  sector: string;
  inflowCr: number;
  outflowCr: number;
  netFlowCr: number;
  tradedValueCr: number;
  stockCount: number;
  advances: number;
  declines: number;
  changePercent: number;
  speed: number;
  isSurge: boolean;
  stocks: StockFlowItem[];
}

export interface MoneyFlowResponse {
  timestamp: string;
  formattedTimestamp: string;
  totalInflowCr: number;
  totalOutflowCr: number;
  netFlowCr: number;
  totalStocks: number;
  totalSectors: number;
  advances: number;
  declines: number;
  unchanged: number;
  sectors: SectorFlowItem[];
  topInflowStocks: StockFlowItem[];
  topOutflowStocks: StockFlowItem[];
}


