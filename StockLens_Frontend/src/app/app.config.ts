import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideHighcharts } from 'highcharts-angular';
import { routes } from './app.routes';
import { ngrokInterceptor } from './interceptors/ngrok.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withFetch(), withInterceptors([ngrokInterceptor])),
    provideHighcharts({ instance: () => import('highcharts/highstock') })
  ]
};
