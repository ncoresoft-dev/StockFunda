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
