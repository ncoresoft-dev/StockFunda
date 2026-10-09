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
  logoUrl?: string;
  websiteUrl?: string;
  weightagePercentage?: number;
}

