export interface CompanyOverview {
  id: number;
  symbol: string;
  companyName: string;
  industry?: string;
  sector?: string;
  logoUrl?: string;
  websiteUrl?: string;
  about?: string;
  keyPoints: string[];
  employeesCount?: number;
  bseCode?: string;
  nseCode?: string;
  keyExecutives: string[];
  source: string;
  updatedAt?: string;

  // Corporate Profile Metadata
  headquarters?: string;
  city?: string;
  country?: string;
  foundedYear?: string;
}
