export interface IndexConstituentCompanyDto {
  symbol: string;
  companyName: string;
  sector?: string;
  industry?: string;
  isin?: string;
  exchange: string;
  weightagePercentage?: number;
  logoUrl?: string;
  websiteUrl?: string;
}

export interface IndexConstituentsResponseDto {
  indexName: string;
  category?: string;
  totalConstituents: number;
  companies: IndexConstituentCompanyDto[];
}

export interface Nifty50StockItem {
  symbol: string;
  companyName: string;
  sector?: string;
  industry?: string;
  isin?: string;
  exchange: string;
  websiteUrl?: string;
  weightagePercentage?: number;
  logoUrl?: string;
  
  // API Fields
  price?: number;
  change?: number;
  changePercent?: number;
  isPositive?: boolean;
  trendPath?: string;
  sevenDayPrices?: number[];
  peRatio?: number;
  marketCap?: number;
  dividendYield?: number;
  netProfitTtm?: number;
  profitGrowthYoy?: number;
  revenueTtm?: number;
  revenueGrowthQoq?: number;
  roce?: number;
}

export interface IndexTugOfWar {
  liftingPoints: number;
  liftingCount: number;
  draggingPoints: number;
  draggingCount: number;
  netPoints: number;
  lifterRatioPct: number;
  biggestSingleMove: string;
  summaryText: string;
}

export interface IndexContributor {
  symbol: string;
  companyName: string;
  sector?: string;
  logoUrl?: string;
  price: number;
  change: number;
  changePercent: number;
  weightage: number;
  pointContribution: number;
  isLifter: boolean;
  formattedPoints: string;
  formattedChangePercent: string;
  pointBarPct: number;
}

export interface IndexChartSlice {
  id: string;
  label: string;
  points: number;
  absolutePoints: number;
  percentageOfCategory: number;
  isLifter: boolean;
  isOther: boolean;
  otherCount: number;
}

export interface IndexAttributionResponse {
  indexKey: string;
  indexSymbol: string;
  indexName: string;
  currentPrice: number;
  previousClose: number;
  change: number;
  changePercent: number;
  isPositive: boolean;
  formattedPrice: string;
  formattedChange: string;
  formattedChangePercent: string;
  lastUpdated: string;
  tugOfWar: IndexTugOfWar;
  topLifters: IndexContributor[];
  topDraggers: IndexContributor[];
  circleSlices: IndexChartSlice[];
  contributors: IndexContributor[];
}


