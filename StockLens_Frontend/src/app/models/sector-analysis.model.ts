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

