import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthIdentityService } from '../../auth/auth-identity';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink],
  template: `
    <div class="min-h-screen bg-slate-950 text-slate-100 antialiased">
      <header
        class="sticky top-0 z-50 border-b border-slate-800/80 bg-slate-950/80 backdrop-blur"
      >
        <nav class="mx-auto flex max-w-7xl items-center justify-between px-6 py-4">
          <a routerLink="/" class="flex items-center gap-2">
            <span
              class="flex h-9 w-9 items-center justify-center rounded-lg bg-gradient-to-br from-amber-300 to-orange-500"
            >
              <svg
                class="h-5 w-5 text-slate-950"
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 24 24"
                fill="currentColor"
                aria-hidden="true"
              >
                <path
                  fill-rule="evenodd"
                  d="M14.615 1.595a.75.75 0 0 1 .359.852L12.982 9.75h7.268a.75.75 0 0 1 .548 1.262l-10.5 11.25a.75.75 0 0 1-1.272-.71l1.992-7.302H3.75a.75.75 0 0 1-.548-1.262l10.5-11.25a.75.75 0 0 1 .913-.143Z"
                  clip-rule="evenodd"
                />
              </svg>
            </span>
            <span class="text-lg font-bold tracking-tight">Protofast</span>
          </a>
          <!-- /signout is a BFF endpoint, not an Angular route — full-page navigation. -->
          <a
            href="/signout"
            rel="external"
            class="rounded-lg px-4 py-2 text-sm font-medium text-slate-300 transition hover:text-white"
          >
            Sign out
          </a>
        </nav>
      </header>

      <main class="mx-auto max-w-7xl px-6 py-16">
        <h1 class="text-3xl font-bold tracking-tight">Your app</h1>
        <p class="mt-3 text-slate-400">
          Signed in as {{ auth.identity.userId }} ({{ auth.identity.tenant }}).
        </p>
      </main>
    </div>
  `,
})
export class Dashboard {
  protected readonly auth = inject(AuthIdentityService);
}
