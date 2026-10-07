import { inject, InjectionToken, makeStateKey, TransferState } from '@angular/core';

export const SERVER_URL_KEY = makeStateKey<string>('serverUrl');

/** The Envoy origin pages and APIs share; empty in production, where it is the page's own. */
export const SERVER_URL = new InjectionToken<string>('SERVER_URL', {
  providedIn: 'root',
  factory: () => inject(TransferState).get(SERVER_URL_KEY, '') || globalThis.location?.origin || '',
});
