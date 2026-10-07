import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Peer } from '../models/peer.model';

@Injectable({
  providedIn: 'root'
})
export class StockPeerService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  /**
   * Fetch company peers by symbol
   */
  getCompanyPeers(symbol: string, exchange = 'NSE', forceRefresh = false): Observable<Peer[]> {
    const params = new HttpParams()
      .set('exchange', exchange)
      .set('forceRefresh', forceRefresh.toString());

    return this.http.get<Peer[]>(`${this.baseUrl}/api/peers/symbol/${encodeURIComponent(symbol)}`, { params });
  }

  /**
   * Fetch company peers by stock ID
   */
  getCompanyPeersByStockId(stockId: number, forceRefresh = false): Observable<Peer[]> {
    const params = new HttpParams()
      .set('forceRefresh', forceRefresh.toString());

    return this.http.get<Peer[]>(`${this.baseUrl}/api/peers/${stockId}`, { params });
  }
}
