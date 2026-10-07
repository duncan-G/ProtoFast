import {
  EnvironmentProviders,
  inject,
  makeEnvironmentProviders,
  provideAppInitializer,
  TransferState,
} from '@angular/core';
import { CONSOLE_ROLES, CONSOLE_ROLES_KEY } from '../access/console-access';
import { provideSsrTraceparentMeta } from '../telemetry/ssr';
import { SERVER_URL, SERVER_URL_KEY } from '../transport/server-url';
import { consoleRolesFromEnv } from './console-roles';

/** For the console's server config: hands the deployment's settings to the browser. */
export function provideAdminConsoleServer(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideSsrTraceparentMeta(),
    { provide: SERVER_URL, useFactory: () => process.env['SERVER_URL'] ?? '' },
    { provide: CONSOLE_ROLES, useFactory: consoleRolesFromEnv },
    provideAppInitializer(() => {
      const state = inject(TransferState);
      state.set(SERVER_URL_KEY, process.env['SERVER_URL'] ?? '');
      state.set(CONSOLE_ROLES_KEY, consoleRolesFromEnv());
    }),
  ]);
}
