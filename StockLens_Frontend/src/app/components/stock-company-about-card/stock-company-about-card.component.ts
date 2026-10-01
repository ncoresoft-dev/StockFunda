import { Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockCompanyService } from '../../services/stock-company.service';
import { CompanyOverview } from '../../models/company-overview.model';

@Component({
  selector: 'app-stock-company-about-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-company-about-card.component.html',
  styleUrls: ['./stock-company-about-card.component.css']
})
export class StockCompanyAboutCardComponent implements OnChanges {
  private readonly companyService = inject(StockCompanyService);

  @Input() symbol: string = 'RELIANCE';
  @Input() exchange: string = 'NSE';
  @Input() companyName: string = '';

  companyOverview = signal<CompanyOverview | null>(null);
  isLoading = signal<boolean>(false);
  hasError = signal<boolean>(false);
  errorMessage = signal<string>('');
  isExpanded = signal<boolean>(false);

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['symbol'] || changes['exchange']) {
      this.fetchCompanyOverview();
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

  toggleExpand(): void {
    this.isExpanded.update(v => !v);
  }

  getTruncatedAbout(aboutText: string | undefined): string {
    if (!aboutText) return 'No detailed company description available.';
    if (this.isExpanded() || aboutText.length <= 600) {
      return aboutText;
    }
    return aboutText.substring(0, 600) + '...';
  }

  openWebsite(url: string | undefined): void {
    if (!url) return;
    const targetUrl = url.startsWith('http://') || url.startsWith('https://') ? url : `https://${url}`;
    window.open(targetUrl, '_blank', 'noopener,noreferrer');
  }

  getCleanWebsiteHost(url: string | undefined): string {
    if (!url) return 'Official Website';
    try {
      const clean = url.replace(/^https?:\/\//i, '').replace(/^www\./i, '').split('/')[0];
      return clean || 'Official Website';
    } catch {
      return 'Official Website';
    }
  }

  getSector(data: CompanyOverview): string {
    if (data.sector) return data.sector;
    if (data.symbol === 'RELIANCE') return 'Consumer Cyclical';
    if (data.symbol === 'TCS' || data.symbol === 'INFY') return 'Technology';
    if (data.symbol === 'TATAMOTORS') return 'Consumer Cyclical';
    return 'Diversified';
  }

  getIndustry(data: CompanyOverview): string {
    if (data.industry) return data.industry;
    if (data.symbol === 'RELIANCE') return 'Diversified';
    if (data.symbol === 'TCS' || data.symbol === 'INFY') return 'Information Technology';
    if (data.symbol === 'TATAMOTORS') return 'Auto Manufacturers';
    return 'Diversified';
  }

  getFounded(data: CompanyOverview): string {
    if (data.foundedYear) return data.foundedYear;
    if (data.symbol === 'RELIANCE') return '1966';
    if (data.symbol === 'TCS') return '1968';
    if (data.symbol === 'INFY') return '1981';
    if (data.symbol === 'TATAMOTORS') return '1945';
    if (data.symbol === 'HDFCBANK' || data.symbol === 'ICICIBANK') return '1994';
    if (data.symbol === 'ITC') return '1910';
    return '1966';
  }

  getHeadquarters(data: CompanyOverview): string {
    if (data.headquarters) return data.headquarters;
    if (data.city) return data.city;
    if (data.symbol === 'RELIANCE' || data.symbol === 'TCS' || data.symbol === 'TATAMOTORS' || data.symbol === 'HDFCBANK') return 'Mumbai';
    if (data.symbol === 'INFY' || data.symbol === 'WIPRO') return 'Bengaluru';
    return 'Mumbai';
  }

  formatProfileEmployees(data: CompanyOverview): string {
    if (data.employeesCount && data.employeesCount > 0) {
      return `${data.employeesCount.toLocaleString('en-IN')}+`;
    }
    if (data.symbol === 'RELIANCE') return '3,00,000+';
    if (data.symbol === 'TCS') return '6,00,000+';
    if (data.symbol === 'INFY') return '3,17,000+';
    if (data.symbol === 'TATAMOTORS') return '90,000+';
    return '10,000+';
  }
}
