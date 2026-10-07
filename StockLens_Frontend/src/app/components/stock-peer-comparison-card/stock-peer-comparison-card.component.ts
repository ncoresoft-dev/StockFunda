import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CompanyOverview } from '../../models/company-overview.model';

@Component({
  selector: 'app-stock-peer-comparison-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-peer-comparison-card.component.html',
  styleUrls: ['./stock-peer-comparison-card.component.css']
})
export class StockPeerComparisonCardComponent {
  @Input() symbol: string = '';
  @Input() companyName: string = '';
  @Input() exchange: string = 'NSE';
  @Input() peers: import('../../models/peer.model').Peer[] = [];
  @Input() isLoading: boolean = false;
  @Input() hasError: boolean = false;
  @Input() industry?: string;
  @Input() isLightTheme: boolean = false;

  formatNumber(val: number | undefined | null, decimals = 2): string {
    if (val == null) return '-';
    return val.toLocaleString('en-IN', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
  }

  formatInCrores(val: number | undefined | null): string {
    if (val == null) return '-';
    // BharatStock returns absolute Rupees (e.g. 7602000000000 for TCS)
    // IndianApi returns already in Crores (e.g. 755746.12 for TCS)
    // We use a threshold of 10,000,000 (1 Crore). No listed company has an absolute market cap < 1 Crore.
    let inCrores = val;
    if (val >= 10000000) {
      inCrores = val / 10000000;
    }
    return this.formatNumber(inCrores);
  }

  getMedian(key: keyof import('../../models/peer.model').Peer): number | null {
    if (!this.peers || this.peers.length === 0) return null;
    const values = this.peers
      .map(p => p[key])
      .filter(v => v != null && typeof v === 'number') as number[];
    
    if (values.length === 0) return null;
    values.sort((a, b) => a - b);
    
    const mid = Math.floor(values.length / 2);
    if (values.length % 2 === 0) {
      return (values[mid - 1] + values[mid]) / 2;
    }
    return values[mid];
  }

  get hasPeRatio(): boolean {
    return this.peers.some(p => p.peRatio != null);
  }

  get hasPbRatio(): boolean {
    return this.peers.some(p => p.pbRatio != null);
  }

  get hasRoe(): boolean {
    return this.peers.some(p => p.roe != null);
  }

  get hasRoce(): boolean {
    return this.peers.some(p => p.roce != null);
  }

  get hasDividendYield(): boolean {
    return this.peers.some(p => p.dividendYield != null);
  }

  isCurrentCompany(peerName: string): boolean {
    if (!peerName) return false;
    const pName = peerName.toLowerCase();
    const cName = (this.companyName || '').toLowerCase();
    const sym = (this.symbol || '').toLowerCase();
    
    // Simple checks: exact match, or symbol is in the name, or the first word matches
    if (pName === cName || pName.includes(sym)) return true;
    
    // For cases like "Tata Consultancy Services Ltd" vs "Tata Consultancy Services Limited"
    const firstWord = cName.split(' ')[0];
    if (firstWord && firstWord.length > 3 && pName.startsWith(firstWord) && cName.includes(pName.split(' ')[1] || '')) {
      return true;
    }
    
    return false;
  }
}
