import { Component, Input, OnInit, OnChanges, SimpleChanges, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockDealsService } from '../../services/stock-deals.service';
import { StockDealsSummary } from '../../models/stock-deals.model';
import { formatNumber } from '../../utils/formatters.util';

@Component({
  selector: 'app-stock-deals-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-deals-card.component.html',
  styleUrls: ['./stock-deals-card.component.css']
})
export class StockDealsCardComponent implements OnInit, OnChanges {
  @Input() symbol!: string;
  @Input() isLightTheme: boolean = false;

  private dealsService = inject(StockDealsService);
  private cdr = inject(ChangeDetectorRef); // Added ChangeDetectorRef
  
  dealsData: StockDealsSummary | null = null;
  loading = false;
  error = '';
  activeTab: 'bulk' | 'block' | 'insider' = 'block';

  get bulkCount(): number {
    return this.dealsData?.bulkDeals?.length || 0;
  }

  get blockCount(): number {
    return this.dealsData?.blockDeals?.length || 0;
  }

  get insiderCount(): number {
    return this.dealsData?.insiderTrades?.length || 0;
  }

  get totalDealsCount(): number {
    return this.bulkCount + this.blockCount + this.insiderCount;
  }

  get hasAnyDeals(): boolean {
    return this.totalDealsCount > 0;
  }

  ngOnInit(): void {
    if (this.symbol) {
      this.fetchDeals();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['symbol'] && !changes['symbol'].firstChange) {
      this.fetchDeals();
    }
  }

  setTab(tab: 'bulk' | 'block' | 'insider') {
    this.activeTab = tab;
    this.cdr.detectChanges(); // Force UI update on tab change
  }

  private fetchDeals() {
    this.loading = true;
    this.error = '';
    this.cdr.detectChanges(); // Force UI update to show loading
    
    const request$ = this.dealsService.getDealsBySymbol(this.symbol);

    request$.subscribe({
      next: (data) => {
        this.dealsData = data;
        this.loading = false;
        
        // Auto select tab that has data
        if (data.blockDeals && data.blockDeals.length > 0) {
          this.activeTab = 'block';
        } else if (data.bulkDeals && data.bulkDeals.length > 0) {
          this.activeTab = 'bulk';
        } else if (data.insiderTrades && data.insiderTrades.length > 0) {
          this.activeTab = 'insider';
        } else {
          this.activeTab = 'block';
        }
        
        this.cdr.detectChanges(); // CRITICAL: Force Angular to update the UI
      },
      error: (err) => {
        this.error = 'Failed to load deals data.';
        this.loading = false;
        this.cdr.detectChanges(); // CRITICAL: Force Angular to update the UI
        console.error(err);
      }
    });
  }

  formatCurrency(value: number): string {
    if (!value) return '-';
    // Format to Indian Rupees
    return new Intl.NumberFormat('en-IN', {
      style: 'currency',
      currency: 'INR',
      maximumFractionDigits: 2
    }).format(value);
  }

  formatNumber(value: number): string {
    return formatNumber(value, 0, '-');
  }
}
