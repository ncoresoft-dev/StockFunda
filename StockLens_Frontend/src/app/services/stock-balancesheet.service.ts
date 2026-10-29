import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';



export interface BalanceSheetLineItemDto {
  name: string;
  isTotal: boolean;
  values: (number | null)[];
}

export interface BalanceSheetResponseDto {
  symbol: string;
  currency?: string;
  periods: string[];
  lineItems: BalanceSheetLineItemDto[];
  assetGrowthPercentage?: number;
  consolidationType?: string;
  latestPeriodEnd?: string;
  source?: string;
  lastSyncedAt?: string;
  errorMessage?: string;
}

@Injectable({
  providedIn: 'root'
})
export class StockBalanceSheetService {
  private apiUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getBalanceSheet(symbol: string, exchange = 'NSE', refresh = false): Observable<BalanceSheetResponseDto> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange)
      .set('refresh', refresh.toString());

    return this.http.get<BalanceSheetResponseDto>(`${this.apiUrl}/api/stocks/balancesheet`, { params });
  }
}
