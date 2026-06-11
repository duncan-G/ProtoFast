import { mergeApplicationConfig, ApplicationConfig, inject, TransferState } from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { SERVER_URL, SERVER_URL_KEY } from './server-url';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    {
      provide: SERVER_URL,
      useFactory: () => {
        const transferState = inject(TransferState);
        const url = process.env['SERVER_URL'] ?? '';
        transferState.set(SERVER_URL_KEY, url);
        return url;
      },
    },
  ]
};

export const config = mergeApplicationConfig(appConfig, serverConfig);
