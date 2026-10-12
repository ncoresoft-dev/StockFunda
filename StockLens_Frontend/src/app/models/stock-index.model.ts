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
  peRatio?: number;
  marketCap?: number;
  dividendYield?: number;
  netProfitTtm?: number;
  profitGrowthYoy?: number;
  revenueTtm?: number;
  revenueGrowthQoq?: number;
  roce?: number;
}

