import {
  ApplicationConfig,
  inject,
  provideBrowserGlobalErrorListeners,
  TransferState,
} from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import {
  provideClientHydration,
  withEventReplay,
  withNoIncrementalHydration,
} from '@angular/platform-browser';
import { SERVER_URL, SERVER_URL_KEY } from './server-url';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideClientHydration(withEventReplay(), withNoIncrementalHydration()),
    {
      provide: SERVER_URL,
      useFactory: () => {
        const transferState = inject(TransferState);
        return transferState.get(SERVER_URL_KEY, window.location.origin);
      },
    },
  ],
};
