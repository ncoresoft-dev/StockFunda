export interface ComparisonPreset {
  stock1: string;
  stock2: string;
  title: string;
  category: string;
}

export interface StockComparisonPricePoint {
  date: string;
  stockAPercentReturn?: number | null;
  stockBPercentReturn?: number | null;
  stockAPrice?: number | null;
  stockBPrice?: number | null;
}

export interface StockComparisonProfile {
  symbol: string;
  companyName: string;
  industry?: string;
  sector?: string;
  logoUrl?: string;
  websiteUrl?: string;

  // Valuation
  currentPrice?: number | null;
  marketCap?: number | null;
  peRatio?: number | null;
  pbRatio?: number | null;
  roe?: number | null;
  roce?: number | null;
  dividendYield?: number | null;

  // Health Score
  healthScore: number;
  signal: string;
  signalClass: string;

  // Growth & Quarterly
  salesQuarterlyGrowthYoY?: number | null;
  salesQuarterlyGrowthQoQ?: number | null;
  profitQuarterlyGrowthYoY?: number | null;
  profitQuarterlyGrowthQoQ?: number | null;
  operatingMargin?: number | null;

  // Shareholding
  promoterHolding?: number | null;
  fiiHolding?: number | null;
  diiHolding?: number | null;
  publicHolding?: number | null;

  // Qualitative
  pros: string[];
  cons: string[];
  keyPoints: string[];
}

export interface StockComparisonHeadToHead {
  overallScoreWinner: 'StockA' | 'StockB' | 'Tie';
  valuationAdvantage: 'StockA' | 'StockB' | 'Tie';
  profitabilityAdvantage: 'StockA' | 'StockB' | 'Tie';
  growthAdvantage: 'StockA' | 'StockB' | 'Tie';
  summaryVerdict: string;
}

export interface StockComparisonResponse {
  stockA: StockComparisonProfile;
  stockB: StockComparisonProfile;
  headToHead: StockComparisonHeadToHead;
  normalizedHistory: StockComparisonPricePoint[];
  period: string;
  generatedAt: string;
}

