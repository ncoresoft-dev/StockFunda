import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { StockShareholdingService } from './stock-shareholding.service';
import { StockShareholdingResponse } from '../models/stock-shareholding.model';
import { environment } from '../../environments/environment';

describe('StockShareholdingService', () => {
  let service: StockShareholdingService;
  let httpTesting: HttpTestingController;

  const mockResponse: StockShareholdingResponse = {
    stockId: 1,
    symbol: 'RELIANCE',
    exchange: 'NSE',
    companyName: 'Reliance Industries Limited',
    currentPeriod: {
      period: 'Q1 FY2026',
      periodKey: '2025-06-30',
      periodDate: '2025-06-30',
      periodType: 'Quarterly',
      promoter: 50.25,
      fii: 18.20,
      dii: 16.30,
      public: 15.25,
      total: 100.00,
      dataAsOf: '30 Jun 2025'
    },
    previousPeriod: {
      period: 'Q4 FY2025',
      periodKey: '2025-03-31',
      periodDate: '2025-03-31',
      periodType: 'Quarterly',
      promoter: 49.80,
      fii: 19.10,
      dii: 15.80,
      public: 15.30,
      total: 100.00,
      dataAsOf: '31 Mar 2025'
    },
    change: {
      promoter: 0.45,
      fii: -0.90,
      dii: 0.50,
      public: -0.05
    },
    relativeChange: {
      promoter: 0.90,
      fii: -4.71,
      dii: 3.16,
      public: -0.33
    },
    history: [],
    dataAsOf: '30 Jun 2025',
    source: 'BharatStock',
    lastSyncedAt: new Date().toISOString()
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        StockShareholdingService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    service = TestBed.inject(StockShareholdingService);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpTesting.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should fetch shareholding by stockId', () => {
    service.getShareholdingByStockId(1, false).subscribe(data => {
      expect(data).toEqual(mockResponse);
      expect(data.currentPeriod.promoter).toBe(50.25);
      expect(data.change.promoter).toBe(0.45);
    });

    const req = httpTesting.expectOne(`${environment.apiUrl}/api/stocks/1/shareholding?refresh=false`);
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);
  });

  it('should fetch shareholding by symbol and exchange', () => {
    service.getShareholdingBySymbol('RELIANCE', 'NSE', true).subscribe(data => {
      expect(data.symbol).toBe('RELIANCE');
      expect(data.change.fii).toBe(-0.90);
    });

    const req = httpTesting.expectOne(`${environment.apiUrl}/api/stocks/shareholding?symbol=RELIANCE&exchange=NSE&refresh=true`);
    expect(req.request.method).toBe('GET');
    req.flush(mockResponse);
  });
});
