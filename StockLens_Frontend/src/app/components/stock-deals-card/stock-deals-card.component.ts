import { Component, Input, OnInit, OnChanges, SimpleChanges, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StockDealsService } from '../../services/stock-deals.service';
import { StockDealsSummary } from '../../models/stock-deals.model';

@Component({
  selector: 'app-stock-deals-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-deals-card.component.html',
  styleUrls: ['./stock-deals-card.component.css']
})
export class StockDealsCardComponent implements OnInit, OnChanges {
  @Input() symbol!: string;
  @Input() stockId?: number;

  private dealsService = inject(StockDealsService);
  private cdr = inject(ChangeDetectorRef); // Added ChangeDetectorRef
  
  dealsData: StockDealsSummary | null = null;
  loading = false;
  error = '';
  activeTab: 'bulk' | 'block' | 'insider' = 'bulk';

  ngOnInit(): void {
    if (this.symbol || this.stockId) {
      this.fetchDeals();
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['symbol'] && !changes['symbol'].firstChange) {
      this.fetchDeals();
    } else if (changes['stockId'] && !changes['stockId'].firstChange) {
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
    
    const request$ = this.stockId 
      ? this.dealsService.getDealsByStockId(this.stockId)
      : this.dealsService.getDealsBySymbol(this.symbol);

    request$.subscribe({
      next: (data) => {
        this.dealsData = data;
        this.loading = false;
        
        // Auto select tab that has data
        if (data.bulkDeals && data.bulkDeals.length > 0) {
          this.activeTab = 'bulk';
        } else if (data.blockDeals && data.blockDeals.length > 0) {
          this.activeTab = 'block';
        } else if (data.insiderTrades && data.insiderTrades.length > 0) {
          this.activeTab = 'insider';
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
    if (!value) return '-';
    return new Intl.NumberFormat('en-IN').format(value);
  }
}
