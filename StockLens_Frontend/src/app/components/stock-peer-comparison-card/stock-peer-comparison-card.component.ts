import { Component, Input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Peer } from '../../models/peer.model';
import { formatNumber } from '../../utils/formatters.util';
import { StockIndexService } from '../../services/stock-index.service';

@Component({
  selector: 'app-stock-peer-comparison-card',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './stock-peer-comparison-card.component.html',
  styleUrls: ['./stock-peer-comparison-card.component.css']
})
export class StockPeerComparisonCardComponent {
  private readonly router = inject(Router);
  private readonly stockIndexService = inject(StockIndexService, { optional: true });

  @Input() symbol: string = '';
  @Input() companyName: string = '';
  @Input() exchange: string = 'NSE';
  @Input() peers: Peer[] = [];
  @Input() isLoading: boolean = false;
  @Input() hasError: boolean = false;
  @Input() industry?: string;
  @Input() isLightTheme: boolean = false;
  @Input() currentLogoUrl?: string | null;

  failedLogos = new Set<string>();

  onLogoError(peer: Peer): void {
    if (peer.companyName) {
      this.failedLogos.add(peer.companyName.trim().toUpperCase());
    }
    if (peer.symbol) {
      this.failedLogos.add(peer.symbol.trim().toUpperCase());
    }
  }

  isLogoFailed(peer: Peer): boolean {
    if (peer.companyName && this.failedLogos.has(peer.companyName.trim().toUpperCase())) return true;
    if (peer.symbol && this.failedLogos.has(peer.symbol.trim().toUpperCase())) return true;
    return false;
  }

  getPeerLogo(peer: Peer): string | null {
    if (this.isLogoFailed(peer)) return null;

    if (this.isCurrentCompany(peer.companyName, peer.symbol) && this.currentLogoUrl) {
      return this.currentLogoUrl;
    }

    if (peer.logoUrl) {
      return peer.logoUrl;
    }

    // Check cached stock lists from stockIndexService if symbol exists
    if (peer.symbol && this.stockIndexService) {
      const cached = this.stockIndexService.getCachedStockBySymbol(peer.symbol);
      if (cached?.logoUrl) {
        return cached.logoUrl;
      }
    }

    return null;
  }

  getInitials(peer: Peer): string {
    if (peer.symbol && peer.symbol.trim().length >= 2) {
      return peer.symbol.trim().substring(0, 2).toUpperCase();
    }
    const clean = (peer.companyName || '').trim();
    if (!clean) return 'CO';
    const words = clean.split(/\s+/).filter(w => w.length > 0 && !w.match(/^(ltd|limited|inc|corp|co|india|ind)$/i));
    if (words.length >= 2) {
      return (words[0][0] + words[1][0]).toUpperCase();
    }
    return clean.substring(0, Math.min(2, clean.length)).toUpperCase();
  }

  getAvatarBg(peer: Peer): string {
    const str = peer.symbol || peer.companyName || '';
    const gradients = [
      'linear-gradient(135deg, rgba(99, 102, 241, 0.35), rgba(129, 140, 248, 0.55))',
      'linear-gradient(135deg, rgba(168, 85, 247, 0.35), rgba(192, 132, 252, 0.55))',
      'linear-gradient(135deg, rgba(14, 165, 233, 0.35), rgba(56, 189, 248, 0.55))',
      'linear-gradient(135deg, rgba(16, 185, 129, 0.35), rgba(52, 211, 153, 0.55))',
      'linear-gradient(135deg, rgba(245, 158, 11, 0.35), rgba(251, 191, 36, 0.55))',
      'linear-gradient(135deg, rgba(236, 72, 153, 0.35), rgba(244, 114, 182, 0.55))'
    ];
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
      hash = str.charCodeAt(i) + ((hash << 5) - hash);
    }
    return gradients[Math.abs(hash) % gradients.length];
  }

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

  getMedian(key: keyof Peer): number | null {
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

  isCurrentCompany(peerName: string, peerSymbol?: string): boolean {
    if (peerSymbol && this.symbol && peerSymbol.toUpperCase() === this.symbol.toUpperCase()) {
      return true;
    }
    if (!peerName) return false;
    const pName = peerName.toLowerCase();
    const cName = (this.companyName || '').toLowerCase();
    const sym = (this.symbol || '').toLowerCase();
    
    if (pName === cName || (sym && (pName === sym || pName.startsWith(sym + ' ') || pName.includes(' ' + sym)))) return true;
    
    const firstWord = cName.split(' ')[0];
    if (firstWord && firstWord.length > 3 && pName.startsWith(firstWord) && cName.includes(pName.split(' ')[1] || '')) {
      return true;
    }
    
    return false;
  }

  navigateToStock(peer: Peer): void {
    if (this.isCurrentCompany(peer.companyName, peer.symbol)) return;
    const cleanPeer = peer.symbol || peer.companyName.trim().split(' ')[0].toUpperCase();
    if (cleanPeer) {
      this.router.navigate(['/stock', cleanPeer]);
    }
  }

  compareWithPeer(peer: Peer | string): void {
    const peerSymbol = typeof peer === 'object' && peer.symbol ? peer.symbol : '';
    const peerName = typeof peer === 'object' ? peer.companyName : peer;
    const cleanPeer = peerSymbol || (peerName ? peerName.trim().split(' ')[0].toUpperCase() : '');
    if (!cleanPeer) return;
    this.router.navigate(['/compare'], {
      queryParams: {
        stock1: this.symbol,
        stock2: cleanPeer,
        exchange: this.exchange
      }
    });
  }
}
