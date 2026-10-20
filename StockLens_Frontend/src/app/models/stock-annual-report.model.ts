export interface AnnualReportItem {
  title: string;
  year?: string;
  pdfUrl: string;
  source?: string;
}

export interface ManagementVision {
  ceoMessage?: string;
  futureGrowthGuidance?: string;
  strategicPriorities: string[];
}

export interface SegmentHighlight {
  segment: string;
  growth?: string;
  summary?: string;
}

export interface AnnualReportInsightItem {
  title: string;
  summary: string;
  category: string;
  displayGroup?: string;
  reportingPeriod?: string;
  status?: 'Planned' | 'Approved' | 'Ongoing' | 'Completed' | string;
  financialValue?: number;
  financialValueUnit?: string;
  expectedBenefit?: string;
  expectedDate?: string;
  sourcePage?: number;
  sourceText?: string;
  priorityScore?: number;
}

export interface AnnualReportInsights {
  executiveSummary?: string;

  // Top High-Impact Growth Insights (Ranked 5-6 initial cards)
  topHighImpactInsights?: AnnualReportInsightItem[];

  // 12 Core Extracted Business Dimensions
  companyExpansion?: AnnualReportInsightItem[];
  capacityExpansion?: AnnualReportInsightItem[];
  majorOrdersAndContracts?: AnnualReportInsightItem[];
  governmentBenefits?: AnnualReportInsightItem[];
  strategicPartnerships?: AnnualReportInsightItem[];
  newBusinessAndProducts?: AnnualReportInsightItem[];
  acquisitionsAndInvestments?: AnnualReportInsightItem[];
  marketExpansion?: AnnualReportInsightItem[];
  managementAndWorkforce?: AnnualReportInsightItem[];
  futureGrowthPlans?: AnnualReportInsightItem[];
  governmentProjectsAndPolicies?: AnnualReportInsightItem[];
  majorBusinessRisks?: AnnualReportInsightItem[];

  // Backward compatibility
  managementVision?: ManagementVision;
  keyHighlights?: string[];
  keyRisks?: string[];
  segmentPerformance?: SegmentHighlight[];
  auditorRemarks?: string;
}

export interface StockAnnualReportsResponse {
  stockId?: number;
  symbol: string;
  companyName?: string;
  totalCount: number;
  reports: AnnualReportItem[];
  extractionStatus: 'Completed' | 'Processing' | 'Pending' | 'NotAvailable' | 'Failed';
  insights?: AnnualReportInsights;
  processedAt?: string;
  message?: string;
}
