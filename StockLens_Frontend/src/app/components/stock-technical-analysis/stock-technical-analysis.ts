import { Component, Input, Output, EventEmitter, OnInit, OnChanges, SimpleChanges, inject, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { BreakoutEvaluationDto, BreakoutTimeframeDto, StockEvaluationService } from '../../services/stock-evaluation.service';

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
  @Output() breakoutStateChange = new EventEmitter<boolean>();

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
    if (changes['symbol'] && !changes['symbol'].isFirstChange() && changes['symbol'].currentValue !== changes['symbol'].previousValue) {
      if (changes['symbol'].currentValue) {
        this.fetchData();
      }
    }
    else if (changes['refreshTrigger'] && !changes['refreshTrigger'].isFirstChange() && changes['refreshTrigger'].currentValue !== changes['refreshTrigger'].previousValue) {
      this.fetchData(true);
    }
  }

  fetchData(refresh = false) {
    if (!this.symbol) return;

    this.loading = true;
    this.error = false;
    this.breakoutData = null;
    this.cd.detectChanges();

    this.evalService.getBreakoutAnalysis(this.symbol, this.exchange, refresh).subscribe({
      next: (data) => {
        if (data && (data.timeframes?.length > 0 || data.overallSignal)) {
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

  /**
   * Only returns timeframes where an actual breakout or breakdown has occurred.
   */
  getActiveBreakouts(): BreakoutTimeframeDto[] {
    if (!this.breakoutData?.timeframes) return [];
    return this.breakoutData.timeframes.filter(tf => tf.trend === 'BULLISH' || tf.trend === 'BEARISH');
  }
}