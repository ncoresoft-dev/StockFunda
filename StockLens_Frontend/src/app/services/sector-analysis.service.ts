import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { SectorAnalysisResponse, MoneyFlowResponse } from '../models/sector-analysis.model';

@Injectable({
  providedIn: 'root'
})
export class SectorAnalysisService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  /**
   * Get all available sectors in the platform
   */
  getAvailableSectors(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/api/sectors`);
  }

  /**
   * Get complete sector analysis, heatmap constituents, and peer rankings
   */
  getSectorAnalysis(sector?: string): Observable<SectorAnalysisResponse> {
    let params = new HttpParams();
    if (sector && sector.trim().length > 0) {
      params = params.set('sector', sector.trim());
    }
    return this.http.get<SectorAnalysisResponse>(`${this.baseUrl}/api/sectors/analysis`, { params });
  }

  /**
   * Get live institutional money flow, sector inflow/outflow, and top moving stocks (Choice A: Top 4-5 per sector)
   */
  getMoneyFlow(topPerSector: number = 5): Observable<MoneyFlowResponse> {
    const params = new HttpParams().set('topPerSector', topPerSector.toString());
    return this.http.get<MoneyFlowResponse>(`${this.baseUrl}/api/sectors/money-flow`, { params });
  }
}

