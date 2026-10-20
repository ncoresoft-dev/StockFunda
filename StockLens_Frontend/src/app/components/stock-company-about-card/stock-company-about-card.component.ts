import { Component, Input, OnInit, OnChanges, SimpleChanges, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockCompanyService } from '../../services/stock-company.service';
import { StockAnnualReportService } from '../../services/stock-annual-report.service';
import { CompanyOverview } from '../../models/company-overview.model';
import { StockAnnualReportsResponse, AnnualReportInsights, AnnualReportInsightItem } from '../../models/stock-annual-report.model';

export type GrowthDisplayGroupKey = 'top' | 'expansion' | 'deals' | 'govt' | 'newbiz' | 'growth' | 'risks' | 'all';

export interface GrowthDisplayGroupMeta {
  key: GrowthDisplayGroupKey;
  label: string;
  shortLabel: string;
  icon: string;
  count: number;
}

@Component({
  selector: 'app-stock-company-about-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-company-about-card.component.html',
  styleUrls: ['./stock-company-about-card.component.css']
})
export class StockCompanyAboutCardComponent implements OnInit, OnChanges {
  private readonly companyService = inject(StockCompanyService);
  private readonly annualReportService = inject(StockAnnualReportService);

  @Input() symbol: string = 'RELIANCE';
  @Input() exchange: string = 'NSE';
  @Input() companyName: string = '';
  @Input() isLightTheme: boolean = false;

  companyOverview = signal<CompanyOverview | null>(null);
  isLoading = signal<boolean>(false);
  hasError = signal<boolean>(false);
  errorMessage = signal<string>('');
  isExpanded = signal<boolean>(false);

  // Annual Report & AI Insights State
  annualReportResponse = signal<StockAnnualReportsResponse | null>(null);
  isReportLoading = signal<boolean>(false);

  // Growth Insights Display Group Filter
  activeDisplayGroup = signal<GrowthDisplayGroupKey>('top');

  // Modal / Drawer state for full PDF excerpt verification & View All
  activeSourceModalItem = signal<AnnualReportInsightItem | null>(null);
  isAllInsightsModalOpen = signal<boolean>(false);

  ngOnInit(): void {
    if (!this.annualReportResponse() && !this.isReportLoading()) {
      this.fetchAnnualReport();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['symbol'] || changes['exchange']) {
      // Instantly clear old company's data so previous company insights are not shown
      this.annualReportResponse.set(null);
      this.companyOverview.set(null);
      this.activeDisplayGroup.set('top');
      this.activeSourceModalItem.set(null);
      this.isAllInsightsModalOpen.set(false);
      this.fetchAnnualReport();
    }
  }

  fetchCompanyOverview(forceRefresh = false): void {
    if (!this.symbol) return;

    this.isLoading.set(true);
    this.hasError.set(false);
    this.errorMessage.set('');

    this.companyService.getCompanyOverview(this.symbol, this.exchange, forceRefresh).subscribe({
      next: (data) => {
        this.companyOverview.set(data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.hasError.set(true);
        this.errorMessage.set(err?.error?.message || 'Failed to load company profile.');
      }
    });
  }

  fetchAnnualReport(forceRefresh = false): void {
    if (!this.symbol) return;

    const requestedSymbol = this.symbol;
    if (forceRefresh) {
      this.annualReportResponse.set(null);
    }
    this.isReportLoading.set(true);

    this.annualReportService.getAnnualReports(requestedSymbol, forceRefresh).subscribe({
      next: (data) => {
        // Prevent race condition: ensure data matches the currently active symbol
        if (this.symbol && requestedSymbol.toUpperCase() === this.symbol.toUpperCase()) {
          this.annualReportResponse.set(data);
        }
        this.isReportLoading.set(false);
      },
      error: () => {
        if (this.symbol && requestedSymbol.toUpperCase() === this.symbol.toUpperCase()) {
          this.annualReportResponse.set({
            symbol: requestedSymbol,
            companyName: this.companyName || requestedSymbol,
            totalCount: 0,
            reports: [],
            extractionStatus: 'NotAvailable',
            message: `Annual report document is currently not filed or available for ${requestedSymbol}.`
          });
        }
        this.isReportLoading.set(false);
      }
    });
  }

  setDisplayGroup(groupKey: GrowthDisplayGroupKey): void {
    this.activeDisplayGroup.set(groupKey);
  }

  toggleExpand(): void {
    this.isExpanded.update(v => !v);
  }

  openPdf(url: string | undefined, page?: number): void {
    if (!url) return;
    let target = url;
    if (page && page > 0) {
      target = `${url}#page=${page}`;
    }
    window.open(target, '_blank', 'noopener,noreferrer');
  }

  openSourceSnippet(item: AnnualReportInsightItem): void {
    this.activeSourceModalItem.set(item);
  }

  closeSourceSnippet(): void {
    this.activeSourceModalItem.set(null);
  }

  openAllInsightsModal(): void {
    this.isAllInsightsModalOpen.set(true);
  }

  closeAllInsightsModal(): void {
    this.isAllInsightsModalOpen.set(false);
  }

  getFinancialYear(): string {
    const yr = this.annualReportResponse()?.reports?.[0]?.year;
    if (yr) return `FY ${yr}`;
    return 'Annual Report';
  }

  // --- Insight Computations & Grouping ---

  getDisplayGroupMetaList(): GrowthDisplayGroupMeta[] {
    const insights = this.annualReportResponse()?.insights;
    if (!insights) return [];

    const topCount = this.getTopHighImpactInsights().length;
    const expansionCount = this.getGroupItems('expansion').length;
    const dealsCount = this.getGroupItems('deals').length;
    const govtCount = this.getGroupItems('govt').length;
    const newbizCount = this.getGroupItems('newbiz').length;
    const growthCount = this.getGroupItems('growth').length;
    const risksCount = this.getGroupItems('risks').length;
    const allCount = this.getAllInsightsList().length;

    const list: GrowthDisplayGroupMeta[] = [
      { key: 'top', label: 'Top High-Impact Insights', shortLabel: 'Top Highlights', icon: '⚡', count: topCount },
      { key: 'expansion', label: 'Expansion & Capacity', shortLabel: 'Expansion', icon: '🏗️', count: expansionCount },
      { key: 'deals', label: 'Major Deals & Partnerships', shortLabel: 'Deals & Orders', icon: '📜', count: dealsCount },
      { key: 'govt', label: 'Government Benefits', shortLabel: 'Govt Benefits', icon: '🏛️', count: govtCount },
      { key: 'newbiz', label: 'New Business & Acquisitions', shortLabel: 'New Business', icon: '🚀', count: newbizCount },
      { key: 'growth', label: 'Future Growth', shortLabel: 'Future Growth', icon: '🎯', count: growthCount },
      { key: 'risks', label: 'Management & Risks', shortLabel: 'Risks & Team', icon: '⚠️', count: risksCount }
    ];

    // Filter out categories with 0 items (except top highlights if insights exist)
    return list.filter(g => g.key === 'top' || g.count > 0);
  }

  getTopHighImpactInsights(): AnnualReportInsightItem[] {
    const insights = this.annualReportResponse()?.insights;
    if (!insights) return [];

    if (insights.topHighImpactInsights && insights.topHighImpactInsights.length > 0) {
      return insights.topHighImpactInsights.slice(0, 6);
    }

    // Fallback: take top 6 from all items sorted by priority score
    return this.getAllInsightsList().slice(0, 6);
  }

  getAllInsightsList(): AnnualReportInsightItem[] {
    const insights = this.annualReportResponse()?.insights;
    if (!insights) return [];

    const list: AnnualReportInsightItem[] = [];
    const addSafe = (items?: AnnualReportInsightItem[]) => {
      if (items && Array.isArray(items)) {
        for (const it of items) {
          if (it && it.title) list.push(it);
        }
      }
    };

    addSafe(insights.companyExpansion);
    addSafe(insights.capacityExpansion);
    addSafe(insights.majorOrdersAndContracts);
    addSafe(insights.governmentBenefits);
    addSafe(insights.strategicPartnerships);
    addSafe(insights.newBusinessAndProducts);
    addSafe(insights.acquisitionsAndInvestments);
    addSafe(insights.marketExpansion);
    addSafe(insights.futureGrowthPlans);
    addSafe(insights.governmentProjectsAndPolicies);
    addSafe(insights.majorBusinessRisks);
    addSafe(insights.managementAndWorkforce);

    return list.sort((a, b) => (b.priorityScore || 0) - (a.priorityScore || 0));
  }

  getGroupItems(groupKey: GrowthDisplayGroupKey): AnnualReportInsightItem[] {
    const insights = this.annualReportResponse()?.insights;
    if (!insights) return [];

    if (groupKey === 'top') {
      return this.getTopHighImpactInsights();
    }
    if (groupKey === 'all') {
      return this.getAllInsightsList();
    }

    const items: AnnualReportInsightItem[] = [];
    const addSafe = (arr?: AnnualReportInsightItem[]) => {
      if (arr && Array.isArray(arr)) {
        for (const it of arr) {
          if (it && it.title) items.push(it);
        }
      }
    };

    switch (groupKey) {
      case 'expansion':
        addSafe(insights.companyExpansion);
        addSafe(insights.capacityExpansion);
        addSafe(insights.marketExpansion);
        break;
      case 'deals':
        addSafe(insights.majorOrdersAndContracts);
        addSafe(insights.strategicPartnerships);
        break;
      case 'govt':
        addSafe(insights.governmentBenefits);
        addSafe(insights.governmentProjectsAndPolicies);
        break;
      case 'newbiz':
        addSafe(insights.newBusinessAndProducts);
        addSafe(insights.acquisitionsAndInvestments);
        break;
      case 'growth':
        addSafe(insights.futureGrowthPlans);
        break;
      case 'risks':
        addSafe(insights.majorBusinessRisks);
        addSafe(insights.managementAndWorkforce);
        break;
    }

    return items.sort((a, b) => (b.priorityScore || 0) - (a.priorityScore || 0));
  }

  getCurrentDisplayItems(): AnnualReportInsightItem[] {
    return this.getGroupItems(this.activeDisplayGroup());
  }

  // --- Formatting Helpers ---

  getStatusClass(status?: string): string {
    if (!status) return 'status-badge neutral';
    const s = status.toLowerCase();
    if (s.includes('completed')) return 'status-badge completed';
    if (s.includes('ongoing') || s.includes('progress')) return 'status-badge ongoing';
    if (s.includes('approved') || s.includes('awarded')) return 'status-badge approved';
    if (s.includes('planned') || s.includes('target')) return 'status-badge planned';
    return 'status-badge neutral';
  }

  formatFinancialValue(item: AnnualReportInsightItem): string | null {
    if (item.financialValue === undefined || item.financialValue === null) return null;
    const unit = item.financialValueUnit || 'Cr';
    const val = item.financialValue.toLocaleString('en-IN');
    if (unit === '%') return `${val}%`;
    if (unit.startsWith('$')) return `$${val}M`;
    return `₹${val} ${unit}`;
  }
}
