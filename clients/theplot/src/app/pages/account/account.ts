import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { AppShell } from '../../shared/app-shell';
import { AuthIdentityService } from '../../auth/auth-identity';

/**
 * Account management is the auth BFF's, not ThePlot's — the session cookie is the credential and
 * only auth-svc can read it. This page shows who is signed in and links out to the endpoints that
 * own the rest, rather than reimplementing them against an API this client cannot call.
 */
@Component({
  selector: 'app-account',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell],
  template: `
    <app-shell>
      <h1 class="text-2xl font-semibold tracking-tight">Account</h1>

      <dl class="mt-8 max-w-lg space-y-4 text-sm">
        <div class="flex justify-between gap-4 border-b border-[var(--color-divider)] pb-3">
          <dt class="text-[var(--color-neutral-400)]">Signed in as</dt>
          <dd class="font-mono text-xs break-all">{{ auth.identity.userId }}</dd>
        </div>
        <div class="flex justify-between gap-4 border-b border-[var(--color-divider)] pb-3">
          <dt class="text-[var(--color-neutral-400)]">Tenant</dt>
          <dd>{{ auth.identity.tenant ?? '—' }}</dd>
        </div>
        <div class="flex justify-between gap-4 border-b border-[var(--color-divider)] pb-3">
          <dt class="text-[var(--color-neutral-400)]">Roles</dt>
          <dd>{{ auth.identity.roles.length > 0 ? auth.identity.roles.join(', ') : 'none' }}</dd>
        </div>
      </dl>

      <p class="mt-8 max-w-lg text-sm text-[var(--color-neutral-400)]">
        Your email address, passkeys and account deletion are managed by the sign-in service,
        which is the only thing that can read your session.
      </p>

      <!-- Both are BFF endpoints, not Angular routes — full-page navigation. -->
      <div class="mt-4 flex gap-2">
        <a href="/account/me" rel="external" class="btn btn-secondary">Manage account</a>
        <a href="/signout" rel="external" class="btn btn-secondary">Sign out</a>
      </div>
    </app-shell>
  `,
})
export class Account {
  protected readonly auth = inject(AuthIdentityService);
}
