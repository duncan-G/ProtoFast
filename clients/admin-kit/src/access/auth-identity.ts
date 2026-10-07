import {
  inject,
  Injectable,
  makeStateKey,
  PLATFORM_ID,
  REQUEST,
  TransferState,
} from '@angular/core';
import { isPlatformServer } from '@angular/common';

export interface AuthIdentity {
  authenticated: boolean;
  userId: string | null;
  tenant: string | null;
  roles: string[];
}

const ANONYMOUS: AuthIdentity = {
  authenticated: false,
  userId: null,
  tenant: null,
  roles: [],
};
const AUTH_IDENTITY_KEY = makeStateKey<AuthIdentity>('adminKit.authIdentity');

/**
 * The identity Envoy's ext_authz injected as request headers, read once during SSR and
 * transferred to the browser. Display only: the API checks the internal JWT on every call.
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

  hasRole(role: string): boolean {
    return this.identity.roles.includes(role);
  }

  private resolve(): AuthIdentity {
    if (isPlatformServer(this.platformId)) {
      const headers = this.request?.headers;
      const userId = headers?.get('x-user-id') ?? null;
      const identity: AuthIdentity = {
        authenticated: !!userId,
        userId,
        tenant: headers?.get('x-tenant') ?? null,
        roles: parseRoles(headers?.get('x-roles')),
      };
      this.transferState.set(AUTH_IDENTITY_KEY, identity);
      return identity;
    }

    return this.transferState.get(AUTH_IDENTITY_KEY, ANONYMOUS);
  }
}

export function parseRoles(header: string | null | undefined): string[] {
  return (header ?? '')
    .split(',')
    .map((role) => role.trim())
    .filter((role) => role.length > 0);
}
