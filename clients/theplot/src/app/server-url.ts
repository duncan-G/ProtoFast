import { InjectionToken, makeStateKey } from '@angular/core';

export const SERVER_URL_KEY = makeStateKey<string>('serverUrl');
export const SERVER_URL = new InjectionToken<string>('SERVER_URL');
