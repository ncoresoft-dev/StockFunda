import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { StockHealthScoreResponse } from '../models/stock-evaluation.model';
import { environment } from '../../environments/environment';
import { StockCashflowResponse } from '../models/stock-cashflow.model';
import { StockQuarterlyResultsResponse } from '../models/stock-quarterly-results.model';
import { StockShareholdingResponse } from '../models/stock-shareholding.model';
import { BalanceSheetResponseDto } from './stock-balancesheet.service';

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
  timeframes: {
    period: string;
    trend: string;
    high: number;
    low: number;
    progressPercent: number;
  }[];
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

  /**
   * Client-side fallback evaluator calculating the 23-metric score in real-time from active dashboard state.
   */
  computeClientEvaluation(
    symbol: string,
    exchange: string,
    cashflow: StockCashflowResponse | null,
    quarters: StockQuarterlyResultsResponse | null,
    shareholding: StockShareholdingResponse | null,
    balanceSheet: BalanceSheetResponseDto | null
  ): StockHealthScoreResponse {
    const pros: string[] = [];
    const cons: string[] = [];
    const redFlags: string[] = [];

    const ratios = cashflow?.ratios;
    const summary = cashflow?.summary;

    // 1. Profitability (25 pts)
    let pPts = 0;
    const roe = ratios?.roe ?? 0;
    const roce = ratios?.roce ?? 0;
    if (roce >= 15 && roe >= 15) {
      pPts += 8;
      pros.push(`High Capital Efficiency: ROE (${roe.toFixed(1)}%) & ROCE (${roce.toFixed(1)}%) > 15%`);
    } else if (roce >= 10 && roe >= 10) {
      pPts += 4;
    } else {
      cons.push(`Sub-par return ratios: ROE (${roe.toFixed(1)}%) / ROCE (${roce.toFixed(1)}%)`);
    }

    const fcf = summary?.freeCashFlow ?? 0;
    if (fcf > 0) {
      pPts += 7;
      pros.push(`Positive Free Cash Flow (₹${fcf.toLocaleString('en-IN')} Cr)`);
    } else if (fcf < 0) {
      cons.push(`Negative Free Cash Flow (₹${fcf.toLocaleString('en-IN')} Cr)`);
    }

    const cfoRatio = summary?.cfoToOperatingProfitRatio;
    if (cfoRatio && cfoRatio >= 0.8) {
      pPts += 5;
      pros.push(`High Quality Earnings: CFO/OP conversion is ${(cfoRatio * 100).toFixed(0)}%`);
    }

    if ((summary?.netCashFlow ?? 0) > 0) {
      pPts += 5;
    }

    // 2. Valuation (20 pts)
    let vPts = 0;
    const peg = ratios?.pegRatio;
    if (peg && peg > 0 && peg < 1.0) {
      vPts += 10;
      pros.push(`Undervalued PEG Ratio (${peg.toFixed(2)}x < 1.0)`);
    } else if (peg && peg >= 1.0 && peg <= 1.5) {
      vPts += 6;
    } else if (peg && peg > 2.0) {
      cons.push(`Elevated PEG Multiple (${peg.toFixed(2)}x)`);
    }

    const pe = ratios?.peRatio;
    const sectorPe = ratios?.sectorPe;
    if (pe && sectorPe && sectorPe > 0) {
      if (pe > 0 && pe < sectorPe) {
        vPts += 6;
        pros.push(`Discount to Industry: P/E (${pe.toFixed(1)}x) < Sector P/E (${sectorPe.toFixed(1)}x)`);
      } else if (pe <= sectorPe * 1.2) {
        vPts += 3;
      } else if (pe > sectorPe * 1.5) {
        cons.push(`High Premium to Sector P/E (${sectorPe.toFixed(1)}x)`);
      }
    }

    const price = ratios?.currentPrice;
    const high = ratios?.week52High;
    if (price && high && high > 0) {
      const discount = ((high - price) / high) * 100;
      if (discount >= 10 && discount <= 35) {
        vPts += 4;
        pros.push(`Healthy Pullback: ${discount.toFixed(0)}% below 52W High`);
      } else if (discount < 10) {
        vPts += 2;
      }
    }

    // 3. Debt & Solvency (15 pts)
    let sPts = 0;
    const op = quarters?.summary?.operatingProfit ?? summary?.operatingProfit ?? 0;
    const interest = quarters?.summary?.interest ?? summary?.interest ?? 0;
    if (interest <= 0) {
      sPts += 8;
      pros.push('Virtually zero debt / interest expense');
    } else {
      const cov = op / interest;
      if (cov >= 4.0) {
        sPts += 8;
        pros.push(`Comfortable Debt Coverage: Interest coverage is ${cov.toFixed(1)}x`);
      } else if (cov >= 2.0) {
        sPts += 4;
      } else if (cov < 1.2) {
        cons.push(`Low Interest Coverage (${cov.toFixed(1)}x)`);
        redFlags.push('Interest coverage below 1.2x');
      }
    }

    sPts += 7; // Default safe balance sheet score

    // 4. Growth & Technicals (15 pts)
    let gPts = 0;
    const salesGrowth = quarters?.yoYGrowth?.salesGrowthPercent;
    const profitGrowth = quarters?.yoYGrowth?.netProfitGrowthPercent;
    if (salesGrowth && profitGrowth) {
      if (salesGrowth >= 10 && profitGrowth >= 12) {
        gPts += 8;
        pros.push(`Strong Qtr YoY Growth: Revenue +${salesGrowth.toFixed(1)}%, PAT +${profitGrowth.toFixed(1)}%`);
      } else if (salesGrowth >= 0 && profitGrowth >= 0) {
        gPts += 4;
      } else if (profitGrowth < -10) {
        cons.push(`Net profit contracted ${profitGrowth.toFixed(1)}% YoY in latest quarter`);
      }
    } else {
      gPts += 4;
    }
    gPts += 4; // Technical trend points

    // 5. Smart Money & Ownership (15 pts)
    let smPts = 0;
    const prom = shareholding?.currentPeriod?.promoter;
    const promChange = shareholding?.change?.promoter ?? 0;
    if (prom && prom >= 50 && promChange >= 0) {
      smPts += 6;
      pros.push(`High & stable Promoter Holding (${prom.toFixed(1)}%)`);
    } else if (prom && prom >= 40) {
      smPts += 4;
    }
    if (promChange < -4.0) {
      cons.push(`Promoters sold ${Math.abs(promChange).toFixed(1)}% in latest quarter`);
      redFlags.push('Promoter stake sale > 4%');
    }

    const instChange = (shareholding?.change?.fii ?? 0) + (shareholding?.change?.dii ?? 0);
    if (instChange > 0.5) {
      smPts += 6;
      pros.push(`Institutions (FII + DII) accumulated +${instChange.toFixed(2)}%`);
    } else if (instChange >= 0) {
      smPts += 3;
    }

    smPts += 3; // Capex points

    // 6. Efficiency (10 pts)
    let ePts = 0;
    const dd = ratios?.debtorDays;
    const ddChange = ratios?.debtorDaysYoY ?? 0;
    if (dd && (dd <= 60 || ddChange <= 0)) {
      ePts += 4;
      pros.push(`Controlled debtor cycle (${dd} days)`);
    } else {
      ePts += 2;
    }

    if (ratios?.inventoryDays) ePts += 3;
    if (ratios?.payableDays) ePts += 3;

    let total = pPts + vPts + sPts + gPts + smPts + ePts;
    if (redFlags.length > 0) {
      total = Math.min(total, 44);
    }
    total = Math.max(0, Math.min(100, total));

    let signal: StockHealthScoreResponse['signal'];
    let signalClass: StockHealthScoreResponse['signalClass'];
    let summaryText = '';

    if (total >= 75) {
      signal = 'Strong Buy';
      signalClass = 'badge-success';
      summaryText = 'Exceptional fundamentals with robust cash generation, attractive valuation, and institutional backing.';
    } else if (total >= 60) {
      signal = 'Good for Buy';
      signalClass = 'badge-good';
      summaryText = 'Solid financials, healthy operating performance, and favorable risk-reward profile.';
    } else if (total >= 45) {
      signal = 'Neutral / Hold';
      signalClass = 'badge-warning';
      summaryText = 'Mixed signals or fair valuation. Recommend monitoring upcoming quarters or waiting for dips.';
    } else {
      signal = 'Risky / Avoid';
      signalClass = 'badge-danger';
      summaryText = 'Elevated risk profile due to weak cash flow, excessive leverage, or negative earnings momentum.';
    }

    return {
      symbol: symbol.toUpperCase(),
      exchange: exchange.toUpperCase(),
      companyName: quarters?.companyName || cashflow?.companyName || symbol.toUpperCase(),
      totalScore: total,
      signal,
      signalClass,
      summaryText,
      profitabilityScore: {
        categoryName: 'Profitability & Cash Flow',
        earnedPoints: pPts,
        maxPoints: 25,
        percentage: Math.round((pPts / 25) * 100),
        status: pPts >= 18 ? 'Excellent' : pPts >= 12 ? 'Good' : 'Average',
        highlights: []
      },
      valuationScore: {
        categoryName: 'Valuation & Fair Value',
        earnedPoints: vPts,
        maxPoints: 20,
        percentage: Math.round((vPts / 20) * 100),
        status: vPts >= 15 ? 'Excellent' : vPts >= 10 ? 'Good' : 'Average',
        highlights: []
      },
      solvencyScore: {
        categoryName: 'Financial Safety & Debt',
        earnedPoints: sPts,
        maxPoints: 15,
        percentage: Math.round((sPts / 15) * 100),
        status: sPts >= 12 ? 'Excellent' : sPts >= 8 ? 'Good' : 'Average',
        highlights: []
      },
      growthScore: {
        categoryName: 'Growth & Technicals',
        earnedPoints: gPts,
        maxPoints: 15,
        percentage: Math.round((gPts / 15) * 100),
        status: gPts >= 12 ? 'Excellent' : gPts >= 8 ? 'Good' : 'Average',
        highlights: []
      },
      smartMoneyScore: {
        categoryName: 'Smart Money & Ownership',
        earnedPoints: smPts,
        maxPoints: 15,
        percentage: Math.round((smPts / 15) * 100),
        status: smPts >= 12 ? 'Excellent' : smPts >= 8 ? 'Good' : 'Average',
        highlights: []
      },
      efficiencyScore: {
        categoryName: 'Operating Efficiency',
        earnedPoints: ePts,
        maxPoints: 10,
        percentage: Math.round((ePts / 10) * 100),
        status: ePts >= 7 ? 'Excellent' : 'Good',
        highlights: []
      },
      pros: Array.from(new Set(pros)),
      cons: Array.from(new Set(cons)),
      redFlags: Array.from(new Set(redFlags)),
      evaluatedAt: new Date().toISOString()
    };
  }
}
