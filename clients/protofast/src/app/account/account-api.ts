import { Injectable } from '@angular/core';

/** One WebAuthn credential on the account, as `/account/me` reports it. */
export interface AccountPasskey {
  id: string;
  /** The name the user gave it during enrolment; empty when they gave none. */
  label: string;
  /** ISO-8601, or null for a credential Keycloak recorded no creation date for. */
  createdAt: string | null;
  /** A passkey, rather than a second-factor WebAuthn credential. */
  passwordless: boolean;
}

export interface AccountView {
  email: string;
  tenant: string;
  passkeys: AccountPasskey[];
  /** Keycloak could not be asked for the credential list; the rest of the view still holds. */
  passkeysUnavailable: boolean;
  /**
   * An address a code has been sent to and is waiting on, or null. It lives on the view rather
   * than in this page's memory so a reload — or the same account in a second tab — comes back to
   * the code box instead of pretending nothing was started.
   */
  pendingEmail: string | null;
  /** ISO-8601 deadline for the pending code. */
  pendingEmailExpiresAt: string | null;
}

/** What starting an email change answers with: where the code went, and how long it lasts. */
export interface PendingEmailChange {
  email: string;
  expiresAt: string;
}

/** Where the BFF sends the user back to after a round trip it owns. */
export const ACCOUNT_ROUTE = '/app/account';

/**
 * The account-management endpoints on auth-svc (`/account/*`). They are BFF routes, not gRPC:
 * the session cookie is the credential, and auth-svc is the only service that can read it.
 *
 * Nothing here runs during SSR — the cookie never reaches the render, so the page fetches after
 * hydration instead of rendering a signed-out shape of itself on the server.
 */
@Injectable({ providedIn: 'root' })
export class AccountApi {
  async load(): Promise<AccountView> {
    const response = await fetch('/account/me', { headers: { accept: 'application/json' } });
    if (!response.ok) {
      throw await toError(response, 'Your account details could not be loaded.');
    }
    return (await response.json()) as AccountView;
  }

  /**
   * Starts an email change: the BFF mails a 6-digit code to `newEmail` and remembers it for
   * fifteen minutes. Nothing about the account has changed yet — `confirmEmailChange` is what
   * moves it.
   */
  async requestEmailChange(newEmail: string): Promise<PendingEmailChange> {
    const response = await fetch('/account/email', {
      method: 'POST',
      headers: { accept: 'application/json', 'content-type': 'application/json' },
      body: JSON.stringify({ newEmail }),
    });
    if (!response.ok) {
      throw await toError(response, 'The code could not be sent.');
    }
    return (await response.json()) as PendingEmailChange;
  }

  /**
   * Sends the code back. The address that gets written is the one the BFF mailed, never one this
   * client restates — the code is proof of that mailbox and no other.
   */
  async confirmEmailChange(code: string): Promise<void> {
    const response = await fetch('/account/email/confirm', {
      method: 'POST',
      headers: { accept: 'application/json', 'content-type': 'application/json' },
      body: JSON.stringify({ code }),
    });
    if (!response.ok) {
      throw await toError(response, 'Your email address could not be changed.');
    }
  }

  /** Abandons a change that was started but never confirmed. */
  async cancelEmailChange(): Promise<void> {
    const response = await fetch('/account/email', {
      method: 'DELETE',
      headers: { accept: 'application/json' },
    });
    if (!response.ok) {
      throw await toError(response, 'The email change could not be cancelled.');
    }
  }

  async removePasskey(credentialId: string): Promise<void> {
    const response = await fetch(`/account/passkeys/${encodeURIComponent(credentialId)}`, {
      method: 'DELETE',
      headers: { accept: 'application/json' },
    });
    if (!response.ok) {
      throw await toError(response, 'The passkey could not be removed.');
    }
  }

  async deleteAccount(): Promise<void> {
    const response = await fetch('/account/delete', {
      method: 'POST',
      headers: { accept: 'application/json' },
    });
    if (!response.ok) {
      throw await toError(response, 'Your account could not be deleted.');
    }
  }
}

/**
 * The endpoint's own message when it sent one, the caller's fallback otherwise. A 401 is its own
 * case: the session lapsed while the page sat open, and the only useful answer is to sign in again.
 */
async function toError(response: Response, fallback: string): Promise<Error> {
  if (response.status === 401) {
    return new Error('Your session has expired. Sign in again to manage your account.');
  }

  try {
    const body = (await response.json()) as { message?: string };
    return new Error(body.message || fallback);
  } catch {
    return new Error(fallback);
  }
}
