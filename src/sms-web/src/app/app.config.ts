import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
  isDevMode,
} from '@angular/core';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideNativeDateAdapter, MAT_DATE_LOCALE } from '@angular/material/core';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { provideTranslateHttpLoader } from '@ngx-translate/http-loader';

import { routes } from './app.routes';
import { apiInterceptor, AuthService } from './core/api';
import { appI18n, SiteService } from './core/site';
import { initTelemetry } from './core/telemetry';
import { provideServiceWorker } from '@angular/service-worker';

initTelemetry();

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
    ),
    provideHttpClient(withFetch(), withInterceptors([apiInterceptor])),
    provideAnimationsAsync(),
    provideNativeDateAdapter(),
    { provide: MAT_DATE_LOCALE, useValue: 'de-DE' },
    provideTranslateService({
      loader: provideTranslateHttpLoader({ prefix: '/i18n/', suffix: '.json' }),
      fallbackLang: 'de',
      lang: 'de',
    }),
    provideAppInitializer(async () => {
      const auth = inject(AuthService);
      const translate = inject(TranslateService);
      await inject(SiteService).load();
      const me = await auth.load();
      const lang = me?.language ?? localStorage.getItem('lang') ?? 'de';
      translate.use(lang);
      Object.assign(appI18n, { t: (k: string) => translate.instant(k) as string, lang: () => translate.getCurrentLang() ?? 'de' });
    }),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
