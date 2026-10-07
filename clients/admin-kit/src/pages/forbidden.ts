import { Component } from '@angular/core';

/**
 * Where an operator lands without the role a page needs, or with no console role at all — auth
 * then issues no session, which is why this page must render without one. It answers 200: SSR
 * reaches it through a guard redirect, and Angular refuses a redirect carrying 403.
 */
@Component({
  selector: 'kit-forbidden',
  template: `
    <main class="min-h-screen bg-gray-50 flex items-center justify-center p-4">
      <div class="w-full max-w-md bg-white rounded-2xl shadow-lg p-8 space-y-4 text-center">
        <h1 class="text-2xl font-bold text-gray-900">No access</h1>
        <p class="text-gray-600">
          Your account doesn’t have access to this console. Ask a platform administrator to grant
          it, or sign in with a different account.
        </p>
        <div class="flex flex-col items-center gap-2 text-sm">
          <a href="/app" rel="external" class="text-indigo-600 hover:underline">Your consoles</a>
          <a href="/signout" rel="external" class="text-gray-600 hover:underline">
            Sign in with a different account
          </a>
        </div>
      </div>
    </main>
  `,
})
export class Forbidden {}
