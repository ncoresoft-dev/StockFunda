import { Component, Input, OnInit, OnChanges, SimpleChanges, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BreakoutEvaluationDto, StockEvaluationService } from '../../services/stock-evaluation.service';

@Component({
  selector: 'app-stock-technical-analysis',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-technical-analysis.html',
  styleUrl: './stock-technical-analysis.css',
})
export class StockTechnicalAnalysis implements OnInit, OnChanges {
  @Input() symbol!: string;
  @Input() exchange: string = 'NSE';
  @Input() refreshTrigger: number = 0;

  private evalService = inject(StockEvaluationService);
  private cd = inject(ChangeDetectorRef);

  loading = true;
  error = false;
  breakoutData: BreakoutEvaluationDto | null = null;
  activeTab: 'BREAKOUTS' | 'EMA' = 'BREAKOUTS';

  ngOnInit() {
    this.fetchData();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['symbol'] && !changes['symbol'].firstChange) {
      this.fetchData();
    }
    if (changes['refreshTrigger'] && !changes['refreshTrigger'].firstChange) {
      this.fetchData(true);
    }
  }

  fetchData(refresh = false) {
    if (!this.symbol) return;
    
    this.loading = true;
    this.error = false;
    this.cd.detectChanges();

    this.evalService.getBreakoutAnalysis(this.symbol, this.exchange, refresh).subscribe({
      next: (data) => {
        if (data) {
          this.breakoutData = data;
        } else {
          this.error = true;
        }
        this.loading = false;
        this.cd.detectChanges();
      },
      error: () => {
        this.error = true;
        this.loading = false;
        this.cd.detectChanges();
      }
    });
  }
}
