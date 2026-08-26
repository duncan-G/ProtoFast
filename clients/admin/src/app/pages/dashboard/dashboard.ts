import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthIdentityService } from '../../auth/auth-identity';
import { AccountMenu } from '../../shared/account-menu';

@Component({
  selector: 'app-dashboard',
  imports: [AccountMenu, RouterLink],
  template: `
    <div class="min-h-screen bg-gray-50">
      <header class="flex items-center justify-between px-4 py-3 bg-white border-b border-gray-200">
        <a routerLink="/" class="font-semibold text-gray-900">ProtoFast Admin</a>
        <!-- Account and sign-out live behind the avatar; see shared/account-menu.ts. -->
        <app-account-menu />
      </header>

      <main class="mx-auto max-w-3xl px-4 py-16 space-y-2">
        <h1 class="text-2xl font-bold text-gray-900">Admin console</h1>
        <p class="text-gray-600">
          Signed in as {{ auth.identity.userId }} ({{ auth.identity.tenant }}).
        </p>
      </main>
    </div>
  `,
})
export class Dashboard {
  protected readonly auth = inject(AuthIdentityService);
}
