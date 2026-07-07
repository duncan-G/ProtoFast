import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthIdentityService } from '../../auth/auth-identity';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  template: `
    <div class="min-h-screen bg-gray-50">
      <header class="flex items-center justify-between px-4 py-3 bg-white border-b border-gray-200">
        <a routerLink="/" class="font-semibold text-gray-900">ProtoFast Admin</a>
        <!-- BFF endpoint, not an Angular route — full-page navigation. -->
        <a href="/signout" rel="external" class="text-sm text-gray-600 hover:underline">Sign out</a>
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
