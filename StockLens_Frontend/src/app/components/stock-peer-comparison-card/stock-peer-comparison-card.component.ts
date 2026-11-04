import { Component, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CompanyOverview } from '../../models/company-overview.model';
import { formatNumber } from '../../utils/formatters.util';

@Component({
  selector: 'app-stock-peer-comparison-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-peer-comparison-card.component.html',
  styleUrls: ['./stock-peer-comparison-card.component.css']
})
export class StockPeerComparisonCardComponent {
  private readonly router = inject(Router);

  @Input() symbol: string = '';
  @Input() companyName: string = '';
  @Input() exchange: string = 'NSE';
  @Input() peers: import('../../models/peer.model').Peer[] = [];
  @Input() isLoading: boolean = false;
  @Input() hasError: boolean = false;
  @Input() industry?: string;
  @Input() isLightTheme: boolean = false;

  formatNumber(val: number | undefined | null, decimals = 2): string {
    return formatNumber(val, decimals, '-');
  }

  formatInCrores(val: number | undefined | null): string {
    if (val == null) return '-';
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
    
    if (pName === cName || pName.includes(sym)) return true;
    
    const firstWord = cName.split(' ')[0];
    if (firstWord && firstWord.length > 3 && pName.startsWith(firstWord) && cName.includes(pName.split(' ')[1] || '')) {
      return true;
    }
    
    return false;
  }

  compareWithPeer(peerName: string): void {
    if (!peerName) return;
    const cleanPeer = peerName.trim().split(' ')[0].toUpperCase();
    this.router.navigate(['/compare'], {
      queryParams: {
        stock1: this.symbol,
        stock2: cleanPeer,
        exchange: this.exchange
      }
    });
  }
}
