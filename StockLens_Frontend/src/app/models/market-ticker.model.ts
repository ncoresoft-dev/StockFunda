export interface MarketTickerQuote {
  symbol: string;
  displayName: string;
  shortName: string;
  currentPrice: number;
  previousClose?: number;
  change?: number;
  changePercent?: number;
  dayHigh?: number;
  dayLow?: number;
  instrumentType: string;
  displayOrder: number;
  lastUpdated: string;
  formattedPrice: string;
  formattedChange: string;
  isPositive: boolean;
}

export interface LensMetric {
  label: string;
  value: string;
  isPositive?: boolean;
}

export interface ExploreLensCompany {
  symbol: string;
  shortName: string;
  name: string;
  sector: string;
  logoUrl?: string;
  currentPrice: number;
  formattedPrice: string;
  change?: number;
  changePercent?: number;
  formattedChange: string;
  isPositive: boolean;
  score: number;
  orbitRadius: number;
  orbitAngle: number;
  colorBg: string;
  colorBorder: string;
  sevenDayPrices: number[];
  sevenDayChangePercent?: number;
  formattedSevenDayChange?: string;
  trendPath: string;
  trendPositive: boolean;
  metrics: LensMetric[];
  insight: string;
}

export interface ExploreLensResponse {
  activeLens: string;
  companies: ExploreLensCompany[];
  lastUpdated: string;
}

export interface MarketPulseIndex {
  symbol: string;
  name: string;
  currentPrice: number;
  formattedPrice: string;
  change: number;
  formattedPointsChange: string;
  changePercent: number;
  formattedChangePercent: string;
  isPositive: boolean;
  dayLow: number;
  formattedDayLow: string;
  dayHigh: number;
  formattedDayHigh: string;
  rangeProgressPct: number;
  sparklinePrices: number[];
  trendPath: string;
  takeawayNote: string;
}

export interface MarketPulseResponse {
  timeframe: string;
  marketSummary: string;
  sectorMapRoute: string;
  lastUpdated: string;
  indices: MarketPulseIndex[];
}
