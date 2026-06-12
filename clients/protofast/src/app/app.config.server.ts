import { mergeApplicationConfig, ApplicationConfig, inject, provideAppInitializer, TransferState } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { SERVER_URL, SERVER_URL_KEY } from './server-url';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    // Unconditional: runs on every SSR bootstrap, so the browser can read
    // serverUrl from #ng-state even when no component injects SERVER_URL.
    provideAppInitializer(() => {
      inject(TransferState).set(SERVER_URL_KEY, process.env['SERVER_URL'] ?? '');
    }),
    {
      provide: SERVER_URL,
      useFactory: () => process.env['SERVER_URL'] ?? '',
    },
  ]
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
