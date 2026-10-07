import { Component, inject, PLATFORM_ID, RESPONSE_INIT } from '@angular/core';
import { isPlatformServer } from '@angular/common';
import { RouterLink } from '@angular/router';

/** For a console's `**` route; SSR answers it with a real 404. */
@Component({
  selector: 'kit-not-found',
  imports: [RouterLink],
  template: `
    <main class="min-h-screen bg-gray-50 flex items-center justify-center p-4">
      <div class="w-full max-w-md bg-white rounded-2xl shadow-lg p-8 space-y-4 text-center">
        <h1 class="text-2xl font-bold text-gray-900">Page not found</h1>
        <p class="text-gray-600">The page you are looking for doesn’t exist or may have moved.</p>
        <a routerLink="/" class="inline-block text-indigo-600 hover:underline"
          >Back to the console</a
        >
      </div>
    </main>
  `,
})
export class NotFound {
  constructor() {
    const responseInit = inject(RESPONSE_INIT, { optional: true });
    if (isPlatformServer(inject(PLATFORM_ID)) && responseInit) {
      responseInit.status = 404;
    }
  }
}
