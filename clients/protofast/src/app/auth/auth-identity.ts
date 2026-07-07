import { inject, Injectable, makeStateKey, PLATFORM_ID, REQUEST, TransferState } from '@angular/core';
import { isPlatformServer } from '@angular/common';

export interface AuthIdentity {
  authenticated: boolean;
  userId: string | null;
  tenant: string | null;
  roles: string[];
}

const ANONYMOUS: AuthIdentity = { authenticated: false, userId: null, tenant: null, roles: [] };
const AUTH_IDENTITY_KEY = makeStateKey<AuthIdentity>('pf.authIdentity');

/**
 * Exposes the identity that Envoy's ext_authz injected as request headers
 * (`x-user-id` / `x-tenant` / `x-roles`). Resolved once during SSR from the incoming request and
 * transferred to the browser, so the SPA knows whether the user is signed in without ever holding
 * a token (guide §7). Absence of `x-user-id` means anonymous.
 */
@Injectable({ providedIn: 'root' })
export class AuthIdentityService {
  private readonly transferState = inject(TransferState);
  private readonly request = inject(REQUEST, { optional: true });
  private readonly platformId = inject(PLATFORM_ID);

  readonly identity: AuthIdentity = this.resolve();

  get authenticated(): boolean {
    return this.identity.authenticated;
  }

  private resolve(): AuthIdentity {
    if (isPlatformServer(this.platformId)) {
      const headers = this.request?.headers;
      const userId = headers?.get('x-user-id') ?? null;
      const identity: AuthIdentity = {
        authenticated: !!userId,
        userId,
        tenant: headers?.get('x-tenant') ?? null,
        roles: (headers?.get('x-roles') ?? '')
          .split(',')
          .map((role) => role.trim())
          .filter((role) => role.length > 0),
      };
      this.transferState.set(AUTH_IDENTITY_KEY, identity);
      return identity;
    }

    return this.transferState.get(AUTH_IDENTITY_KEY, ANONYMOUS);
  }
}
