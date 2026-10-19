export interface CategoryScore {
  categoryName: string;
  earnedPoints: number;
  maxPoints: number;
  percentage: number;
  status: 'Excellent' | 'Good' | 'Average' | 'Poor';
  highlights: string[];
}

export interface StockHealthScoreResponse {
  symbol: string;
  exchange: string;
  companyName: string;
  totalScore: number;
  signal: 'Strong Buy' | 'Good for Buy' | 'Neutral / Hold' | 'Risky / Avoid';
  signalClass: 'badge-success' | 'badge-good' | 'badge-warning' | 'badge-danger';
  summaryText: string;
  profitabilityScore: CategoryScore;
  valuationScore: CategoryScore;
  solvencyScore: CategoryScore;
  growthScore: CategoryScore;
  smartMoneyScore: CategoryScore;
  efficiencyScore: CategoryScore;
  pros: string[];
  cons: string[];
  redFlags: string[];
  totalApplicableMetrics?: number;
  availableMetricsCount?: number;
  dataCoveragePercent?: number;
  dataConfidence?: string;
  missingMetrics?: string[];
  inapplicableMetrics?: string[];
  rawTotalScore?: number;
  evaluatedAt: string;
}
