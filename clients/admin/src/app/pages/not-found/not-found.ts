import { Component, inject, PLATFORM_ID, RESPONSE_INIT } from '@angular/core';
import { isPlatformServer } from '@angular/common';
import { RouterLink } from '@angular/router';

/**
 * Catch-all 404 page. Reached via the `**` route so unmatched paths render app chrome
 * instead of Express's bare "Cannot GET …". During SSR it also stamps the outgoing response
 * with a real 404 status via `RESPONSE_INIT` so crawlers and the edge see the correct code.
 */
@Component({
  selector: 'app-not-found',
  imports: [RouterLink],
  template: `
    <main class="min-h-screen bg-gray-50 flex items-center justify-center p-4">
      <div class="w-full max-w-md bg-white rounded-2xl shadow-lg p-8 space-y-4 text-center">
        <h1 class="text-2xl font-bold text-gray-900">Page not found</h1>
        <p class="text-gray-600">The page you are looking for doesn’t exist or may have moved.</p>
        <a routerLink="/" class="inline-block text-indigo-600 hover:underline">Back to home</a>
      </div>
    </main>
  `,
})
export class NotFound {
  private readonly platformId = inject(PLATFORM_ID);
  private readonly responseInit = inject(RESPONSE_INIT, { optional: true });

  constructor() {
    if (isPlatformServer(this.platformId) && this.responseInit) {
      this.responseInit.status = 404;
    }
  }
}
