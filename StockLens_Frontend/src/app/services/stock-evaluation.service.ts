import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { StockHealthScoreResponse } from '../models/stock-evaluation.model';
import { environment } from '../../environments/environment';

export interface BreakoutTimeframeDto {
  period: string;
  trend: string;
  high: number;
  low: number;
  progressPercent: number;
}

export interface BreakoutEvaluationDto {
  summaryText: string;
  overallSignal: string;
  signalStrength: string;
  bullishCount: number;
  bearishCount: number;
  currentPrice: number;
  avgVolume1Month: number;
  todayVolume: number;
  volumeRatio: number;
  levels: {
    year1High?: number;
    year1Low?: number;
    year2High?: number;
    year2Low?: number;
    year3High?: number;
    year3Low?: number;
  };
  timeframes: BreakoutTimeframeDto[];
  prioritySignals: {
    rank: number;
    level: string;
    description: string;
    subDescription: string;
    type: string;
  }[];
}

@Injectable({
  providedIn: 'root'
})
export class StockEvaluationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetches stock health evaluation from the backend evaluation engine.
   */
  getEvaluation(symbol: string, exchange = 'NSE', refresh = false): Observable<StockHealthScoreResponse | null> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('refresh', refresh.toString());

    return this.http.get<StockHealthScoreResponse>(`${this.baseUrl}/api/stocks/evaluation`, { params }).pipe(
      catchError((err) => {
        console.warn(`[StockEvaluationService] Backend evaluation call failed for ${symbol}, fallback will be used:`, err);
        return of(null);
      })
    );
  }

  /**
   * Fetches the technical breakouts (1Y/2Y/3Y) and volume analysis from backend.
   */
  getBreakoutAnalysis(symbol: string, exchange = 'NSE', refresh = false): Observable<BreakoutEvaluationDto | null> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('refresh', refresh.toString());

    return this.http.get<BreakoutEvaluationDto>(`${this.baseUrl}/api/stocks/evaluation/breakouts`, { params }).pipe(
      catchError((err) => {
        console.warn(`[StockEvaluationService] Breakout analysis call failed for ${symbol}`, err);
        return of(null);
      })
    );
  }

}
