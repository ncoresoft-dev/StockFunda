import { Routes } from '@angular/router';
import { Nifty50LandingComponent } from './components/nifty50-landing/nifty50-landing.component';
import { StockDashboardComponent } from './components/stock-dashboard/stock-dashboard.component';

export const routes: Routes = [
  {
    path: '',
    component: Nifty50LandingComponent,
    title: 'StockLens — NIFTY 50'
  },
  {
    path: 'watchlist',
    redirectTo: '',
    pathMatch: 'full'
  },
  {
    path: 'stock/:symbol',
    component: StockDashboardComponent,
    title: 'Stock Details — StockLens'
  },
  {
    path: 'annual-reports',
    loadComponent: () => import('./components/annual-reports-list/annual-reports-list.component').then(m => m.AnnualReportsListComponent),
    title: 'Companies with Annual Reports — StockLens'
  },
  {
    path: 'admin',
    redirectTo: 'annual-reports',
    pathMatch: 'full'
  },
  {
    path: 'stock',
    redirectTo: 'stock/RELIANCE',
    pathMatch: 'full'
  },
  {
    path: '**',
    redirectTo: ''
  }
];
