import { Injectable, inject } from '@angular/core';
import { Observable, of } from 'rxjs';
import { Nifty50StockItem } from '../models/stock-index.model';

const NIFTY_50_COMPANIES: Nifty50StockItem[] = [
  { symbol: 'RELIANCE', companyName: 'Reliance Industries Ltd', sector: 'Energy', industry: 'Oil & Gas Refining & Marketing', isin: 'INE002A01018', exchange: 'NSE', weightagePercentage: 9.12, logoUrl: 'https://company-logo.shareperks.in/logo/INE002A01018/icon.svg' },
  { symbol: 'HDFCBANK', companyName: 'HDFC Bank Ltd', sector: 'Financial Services', industry: 'Private Sector Bank', isin: 'INE040A01034', exchange: 'NSE', weightagePercentage: 8.95, logoUrl: 'https://company-logo.shareperks.in/logo/INE040A01034/icon.svg' },
  { symbol: 'ICICIBANK', companyName: 'ICICI Bank Ltd', sector: 'Financial Services', industry: 'Private Sector Bank', isin: 'INE090A01021', exchange: 'NSE', weightagePercentage: 7.85, logoUrl: 'https://company-logo.shareperks.in/logo/INE090A01021/icon.svg' },
  { symbol: 'INFY', companyName: 'Infosys Ltd', sector: 'Information Technology', industry: 'Computers - Software & Consulting', isin: 'INE009A01021', exchange: 'NSE', weightagePercentage: 5.75, logoUrl: 'https://company-logo.shareperks.in/logo/INE009A01021/icon.svg' },
  { symbol: 'TCS', companyName: 'Tata Consultancy Services Ltd', sector: 'Information Technology', industry: 'Computers - Software & Consulting', isin: 'INE467B01029', exchange: 'NSE', weightagePercentage: 4.15, logoUrl: 'https://company-logo.shareperks.in/logo/INE467B01029/icon.svg' },
  { symbol: 'ITC', companyName: 'ITC Ltd', sector: 'Fast Moving Consumer Goods', industry: 'Cigarettes & Tobacco Products', isin: 'INE154A01025', exchange: 'NSE', weightagePercentage: 3.85, logoUrl: 'https://company-logo.shareperks.in/logo/INE154A01025/icon.svg' },
  { symbol: 'LT', companyName: 'Larsen & Toubro Ltd', sector: 'Construction', industry: 'Civil Construction', isin: 'INE018A01030', exchange: 'NSE', weightagePercentage: 3.65, logoUrl: 'https://company-logo.shareperks.in/logo/INE018A01030/icon.svg' },
  { symbol: 'BHARTIARTL', companyName: 'Bharti Airtel Ltd', sector: 'Telecommunication', industry: 'Telecom - Services', isin: 'INE397D01024', exchange: 'NSE', weightagePercentage: 3.55, logoUrl: 'https://company-logo.shareperks.in/logo/INE397D01024/icon.svg' },
  { symbol: 'SBIN', companyName: 'State Bank of India', sector: 'Financial Services', industry: 'Public Sector Bank', isin: 'INE062A01020', exchange: 'NSE', weightagePercentage: 3.15, logoUrl: 'https://company-logo.shareperks.in/logo/INE062A01020/icon.svg' },
  { symbol: 'AXISBANK', companyName: 'Axis Bank Ltd', sector: 'Financial Services', industry: 'Private Sector Bank', isin: 'INE238A01034', exchange: 'NSE', weightagePercentage: 3.05, logoUrl: 'https://company-logo.shareperks.in/logo/INE238A01034/icon.svg' },
  { symbol: 'KOTAKBANK', companyName: 'Kotak Mahindra Bank Ltd', sector: 'Financial Services', industry: 'Private Sector Bank', isin: 'INE237A01028', exchange: 'NSE', weightagePercentage: 2.85, logoUrl: 'https://company-logo.shareperks.in/logo/INE237A01028/icon.svg' },
  { symbol: 'HINDUNILVR', companyName: 'Hindustan Unilever Ltd', sector: 'Fast Moving Consumer Goods', industry: 'Diversified FMCG', isin: 'INE030A01027', exchange: 'NSE', weightagePercentage: 2.65, logoUrl: 'https://company-logo.shareperks.in/logo/INE030A01027/icon.svg' },
  { symbol: 'M&M', companyName: 'Mahindra & Mahindra Ltd', sector: 'Automobile and Auto Components', industry: 'Passenger Cars & Utility Vehicles', isin: 'INE101A01026', exchange: 'NSE', weightagePercentage: 2.45, logoUrl: 'https://company-logo.shareperks.in/logo/INE101A01026/icon.svg' },
  { symbol: 'TATAMOTORS', companyName: 'Tata Motors Ltd', sector: 'Automobile and Auto Components', industry: 'Commercial & Passenger Vehicles', isin: 'INE155A01022', exchange: 'NSE', weightagePercentage: 2.15, logoUrl: 'https://company-logo.shareperks.in/logo/INE155A01022/icon.svg' },
  { symbol: 'SUNPHARMA', companyName: 'Sun Pharmaceutical Industries Ltd', sector: 'Healthcare', industry: 'Pharmaceuticals', isin: 'INE044A01036', exchange: 'NSE', weightagePercentage: 2.05, logoUrl: 'https://company-logo.shareperks.in/logo/INE044A01036/icon.svg' },
  { symbol: 'MARUTI', companyName: 'Maruti Suzuki India Ltd', sector: 'Automobile and Auto Components', industry: 'Passenger Cars & Utility Vehicles', isin: 'INE585B01010', exchange: 'NSE', weightagePercentage: 1.95, logoUrl: 'https://company-logo.shareperks.in/logo/INE585B01010/icon.svg' },
  { symbol: 'BAJFINANCE', companyName: 'Bajaj Finance Ltd', sector: 'Financial Services', industry: 'Non Banking Financial Company (NBFC)', isin: 'INE296A01024', exchange: 'NSE', weightagePercentage: 1.85, logoUrl: 'https://company-logo.shareperks.in/logo/INE296A01024/icon.svg' },
  { symbol: 'NTPC', companyName: 'NTPC Ltd', sector: 'Power', industry: 'Power Generation', isin: 'INE733E01010', exchange: 'NSE', weightagePercentage: 1.80, logoUrl: 'https://company-logo.shareperks.in/logo/INE733E01010/icon.svg' },
  { symbol: 'HCLTECH', companyName: 'HCL Technologies Ltd', sector: 'Information Technology', industry: 'Computers - Software & Consulting', isin: 'INE860A01027', exchange: 'NSE', weightagePercentage: 1.65, logoUrl: 'https://company-logo.shareperks.in/logo/INE860A01027/icon.svg' },
  { symbol: 'TITAN', companyName: 'Titan Company Ltd', sector: 'Consumer Durables', industry: 'Gems Jewellery & Watches', isin: 'INE280A01028', exchange: 'NSE', weightagePercentage: 1.55, logoUrl: 'https://company-logo.shareperks.in/logo/INE280A01028/icon.svg' },
  { symbol: 'POWERGRID', companyName: 'Power Grid Corporation of India Ltd', sector: 'Power', industry: 'Power Transmission', isin: 'INE752E01010', exchange: 'NSE', weightagePercentage: 1.50, logoUrl: 'https://company-logo.shareperks.in/logo/INE752E01010/icon.svg' },
  { symbol: 'TATASTEEL', companyName: 'Tata Steel Ltd', sector: 'Metals & Mining', industry: 'Iron & Steel', isin: 'INE081A01020', exchange: 'NSE', weightagePercentage: 1.45, logoUrl: 'https://company-logo.shareperks.in/logo/INE081A01020/icon.svg' },
  { symbol: 'ONGC', companyName: 'Oil & Natural Gas Corporation Ltd', sector: 'Energy', industry: 'Oil & Gas Exploration & Production', isin: 'INE213A01029', exchange: 'NSE', weightagePercentage: 1.40, logoUrl: 'https://company-logo.shareperks.in/logo/INE213A01029/icon.svg' },
  { symbol: 'ADANIENT', companyName: 'Adani Enterprises Ltd', sector: 'Metals & Mining', industry: 'Trading - Minerals', isin: 'INE423A01024', exchange: 'NSE', weightagePercentage: 1.35, logoUrl: 'https://company-logo.shareperks.in/logo/INE423A01024/icon.svg' },
  { symbol: 'ADANIPORTS', companyName: 'Adani Ports and Special Economic Zone Ltd', sector: 'Services', industry: 'Port & Port services', isin: 'INE742F01042', exchange: 'NSE', weightagePercentage: 1.30, logoUrl: 'https://company-logo.shareperks.in/logo/INE742F01042/icon.svg' },
  { symbol: 'COALINDIA', companyName: 'Coal India Ltd', sector: 'Energy', industry: 'Coal', isin: 'INE522F01014', exchange: 'NSE', weightagePercentage: 1.25, logoUrl: 'https://company-logo.shareperks.in/logo/INE522F01014/icon.svg' },
  { symbol: 'BAJAJFINSV', companyName: 'Bajaj Finserv Ltd', sector: 'Financial Services', industry: 'Financial Holding Company', isin: 'INE918I01018', exchange: 'NSE', weightagePercentage: 1.20, logoUrl: 'https://company-logo.shareperks.in/logo/INE918I01018/icon.svg' },
  { symbol: 'ULTRACEMCO', companyName: 'UltraTech Cement Ltd', sector: 'Construction Materials', industry: 'Cement & Cement Products', isin: 'INE481G01011', exchange: 'NSE', weightagePercentage: 1.18, logoUrl: 'https://company-logo.shareperks.in/logo/INE481G01011/icon.svg' },
  { symbol: 'ASIANPAINT', companyName: 'Asian Paints Ltd', sector: 'Consumer Durables', industry: 'Paints & Varnishes', isin: 'INE021A01026', exchange: 'NSE', weightagePercentage: 1.15, logoUrl: 'https://company-logo.shareperks.in/logo/INE021A01026/icon.svg' },
  { symbol: 'JSWSTEEL', companyName: 'JSW Steel Ltd', sector: 'Metals & Mining', industry: 'Iron & Steel', isin: 'INE019A01038', exchange: 'NSE', weightagePercentage: 1.10, logoUrl: 'https://company-logo.shareperks.in/logo/INE019A01038/icon.svg' },
  { symbol: 'GRASIM', companyName: 'Grasim Industries Ltd', sector: 'Construction Materials', industry: 'Diversified', isin: 'INE047A01021', exchange: 'NSE', weightagePercentage: 1.05, logoUrl: 'https://company-logo.shareperks.in/logo/INE047A01021/icon.svg' },
  { symbol: 'TECHM', companyName: 'Tech Mahindra Ltd', sector: 'Information Technology', industry: 'Computers - Software & Consulting', isin: 'INE669C01036', exchange: 'NSE', weightagePercentage: 1.00, logoUrl: 'https://company-logo.shareperks.in/logo/INE669C01036/icon.svg' },
  { symbol: 'WIPRO', companyName: 'Wipro Ltd', sector: 'Information Technology', industry: 'Computers - Software & Consulting', isin: 'INE075A01022', exchange: 'NSE', weightagePercentage: 0.95, logoUrl: 'https://company-logo.shareperks.in/logo/INE075A01022/icon.svg' },
  { symbol: 'CIPLA', companyName: 'Cipla Ltd', sector: 'Healthcare', industry: 'Pharmaceuticals', isin: 'INE059A01026', exchange: 'NSE', weightagePercentage: 0.90, logoUrl: 'https://company-logo.shareperks.in/logo/INE059A01026/icon.svg' },
  { symbol: 'BPCL', companyName: 'Bharat Petroleum Corporation Ltd', sector: 'Energy', industry: 'Oil & Gas Refining & Marketing', isin: 'INE029A01011', exchange: 'NSE', weightagePercentage: 0.88, logoUrl: 'https://company-logo.shareperks.in/logo/INE029A01011/icon.svg' },
  { symbol: 'DRREDDY', companyName: 'Dr. Reddy\'s Laboratories Ltd', sector: 'Healthcare', industry: 'Pharmaceuticals', isin: 'INE089A01023', exchange: 'NSE', weightagePercentage: 0.85, logoUrl: 'https://company-logo.shareperks.in/logo/INE089A01023/icon.svg' },
  { symbol: 'NESTLEIND', companyName: 'Nestle India Ltd', sector: 'Fast Moving Consumer Goods', industry: 'Food & Dairy Products', isin: 'INE239A01016', exchange: 'NSE', weightagePercentage: 0.82, logoUrl: 'https://company-logo.shareperks.in/logo/INE239A01016/icon.svg' },
  { symbol: 'BRITANNIA', companyName: 'Britannia Industries Ltd', sector: 'Fast Moving Consumer Goods', industry: 'Packaged Foods', isin: 'INE216A01030', exchange: 'NSE', weightagePercentage: 0.80, logoUrl: 'https://company-logo.shareperks.in/logo/INE216A01030/icon.svg' },
  { symbol: 'EICHERMOT', companyName: 'Eicher Motors Ltd', sector: 'Automobile and Auto Components', industry: 'Automobiles - 2 & 3 Wheelers', isin: 'INE066A01021', exchange: 'NSE', weightagePercentage: 0.78, logoUrl: 'https://company-logo.shareperks.in/logo/INE066A01021/icon.svg' },
  { symbol: 'HINDALCO', companyName: 'Hindalco Industries Ltd', sector: 'Metals & Mining', industry: 'Aluminium', isin: 'INE038A01020', exchange: 'NSE', weightagePercentage: 0.75, logoUrl: 'https://company-logo.shareperks.in/logo/INE038A01020/icon.svg' },
  { symbol: 'TATACONSUM', companyName: 'Tata Consumer Products Ltd', sector: 'Fast Moving Consumer Goods', industry: 'Tea & Coffee', isin: 'INE192A01025', exchange: 'NSE', weightagePercentage: 0.72, logoUrl: 'https://company-logo.shareperks.in/logo/INE192A01025/icon.svg' },
  { symbol: 'SBILIFE', companyName: 'SBI Life Insurance Company Ltd', sector: 'Financial Services', industry: 'Life Insurance', isin: 'INE123W01016', exchange: 'NSE', weightagePercentage: 0.70, logoUrl: 'https://company-logo.shareperks.in/logo/INE123W01016/icon.svg' },
  { symbol: 'HDFCLIFE', companyName: 'HDFC Life Insurance Company Ltd', sector: 'Financial Services', industry: 'Life Insurance', isin: 'INE795G01014', exchange: 'NSE', weightagePercentage: 0.68, logoUrl: 'https://company-logo.shareperks.in/logo/INE795G01014/icon.svg' },
  { symbol: 'DIVISLAB', companyName: 'Divi\'s Laboratories Ltd', sector: 'Healthcare', industry: 'Pharmaceuticals', isin: 'INE361B01024', exchange: 'NSE', weightagePercentage: 0.65, logoUrl: 'https://company-logo.shareperks.in/logo/INE361B01024/icon.svg' },
  { symbol: 'APOLLOHOSP', companyName: 'Apollo Hospitals Enterprise Ltd', sector: 'Healthcare', industry: 'Hospital & Healthcare Services', isin: 'INE437A01024', exchange: 'NSE', weightagePercentage: 0.62, logoUrl: 'https://company-logo.shareperks.in/logo/INE437A01024/icon.svg' },
  { symbol: 'BAJAJ-AUTO', companyName: 'Bajaj Auto Ltd', sector: 'Automobile and Auto Components', industry: 'Automobiles - 2 & 3 Wheelers', isin: 'INE917I01010', exchange: 'NSE', weightagePercentage: 0.60, logoUrl: 'https://company-logo.shareperks.in/logo/INE917I01010/icon.svg' },
  { symbol: 'BEL', companyName: 'Bharat Electronics Ltd', sector: 'Capital Goods', industry: 'Aerospace & Defence', isin: 'INE263A01024', exchange: 'NSE', weightagePercentage: 0.58, logoUrl: 'https://company-logo.shareperks.in/logo/INE263A01024/icon.svg' },
  { symbol: 'SHREECEM', companyName: 'Shree Cement Ltd', sector: 'Construction Materials', industry: 'Cement & Cement Products', isin: 'INE070A01015', exchange: 'NSE', weightagePercentage: 0.55, logoUrl: 'https://company-logo.shareperks.in/logo/INE070A01015/icon.svg' },
  { symbol: 'HEROMOTOCO', companyName: 'Hero MotoCorp Ltd', sector: 'Automobile and Auto Components', industry: 'Automobiles - 2 & 3 Wheelers', isin: 'INE158A01026', exchange: 'NSE', weightagePercentage: 0.52, logoUrl: 'https://company-logo.shareperks.in/logo/INE158A01026/icon.svg' },
  { symbol: 'TRENT', companyName: 'Trent Ltd', sector: 'Consumer Services', industry: 'Retailing', isin: 'INE849A01020', exchange: 'NSE', weightagePercentage: 0.50, logoUrl: 'https://company-logo.shareperks.in/logo/INE849A01020/icon.svg' }
];

@Injectable({
  providedIn: 'root'
})
export class StockIndexService {
  /**
   * Returns NIFTY 50 constituent companies for the UI
   */
  getNifty50List(_forceRefresh = false): Observable<Nifty50StockItem[]> {
    return of(NIFTY_50_COMPANIES);
  }

  /**
   * Finds stock items matching a list of symbols (for Watchlist tab)
   */
  getStocksBySymbols(symbols: string[]): Observable<Nifty50StockItem[]> {
    if (!symbols || symbols.length === 0) {
      return of([]);
    }

    const symbolSet = new Set(symbols.map(s => s.toUpperCase()));
    const found = NIFTY_50_COMPANIES.filter(s => symbolSet.has(s.symbol.toUpperCase()));
    const foundSet = new Set(found.map(f => f.symbol.toUpperCase()));
    const missing = symbols.filter(s => !foundSet.has(s.toUpperCase())).map(sym => ({
      symbol: sym.toUpperCase(),
      companyName: `${sym.toUpperCase()} Ltd`,
      sector: 'Equities',
      industry: 'NSE Listed',
      exchange: 'NSE'
    } as Nifty50StockItem));

    return of([...found, ...missing]);
  }
}
