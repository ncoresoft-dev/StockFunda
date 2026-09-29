import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { VolumeDeliveryAnalysisResponse } from '../models/stock-volume-delivery.model';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class StockVolumeDeliveryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = environment.apiUrl;

  /**
   * Fetch volume and delivery analysis by Stock Symbol and Exchange.
   */
  getDeliveryAnalysis(symbol: string, exchange = 'NSE'): Observable<VolumeDeliveryAnalysisResponse> {
    const params = new HttpParams()
      .set('symbol', symbol)
      .set('exchange', exchange);

    return this.http.get<VolumeDeliveryAnalysisResponse>(`${this.baseUrl}/api/stocks/delivery-analysis`, { params });
  }
}
