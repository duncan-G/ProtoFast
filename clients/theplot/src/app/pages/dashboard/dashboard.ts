import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthIdentityService } from '../../auth/auth-identity';
import { AccountMenu } from '../../shared/account-menu';
import { TheplotLogo } from '../../shared/theplot-logo';

/**
 * The signed-in home. A placeholder until the library ships: it proves the
 * protected area end to end (SSR gate, ext_authz identity, account menu) and
 * gives the sign-in flow somewhere real to land.
 */
@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AccountMenu, RouterLink, TheplotLogo],
  template: `
    <div class="ember flex min-h-screen flex-col antialiased">
      <header class="border-b border-[var(--color-divider)]">
        <nav class="mx-auto flex w-full max-w-[1240px] items-center px-6 py-4 sm:px-8">
          <a routerLink="/" class="mr-auto text-[26px] no-underline"><app-theplot-logo /></a>
          <!-- Account and sign-out live behind the avatar; see shared/account-menu.ts. -->
          <app-account-menu />
        </nav>
      </header>

      <main class="mx-auto w-full max-w-[1240px] flex-1 px-6 pb-[100px] sm:px-8">
        <p
          class="m-0 mt-10 mb-3 font-[family-name:var(--font-mono)] text-[12px] tracking-[0.14em] text-[var(--color-accent-bright)] uppercase"
        >
          Your library
        </p>
        <h1 class="mb-2 text-[clamp(40px,5vw,64px)]">Nothing on the <span class="italic">marquee</span> yet.</h1>
        <p class="m-0 max-w-[460px] text-[16px] leading-[1.6] text-[var(--color-neutral-400)]">
          Signed in as {{ auth.identity.userId }}. Writing, reading and watching land here —
          your first chapter is the opening scene.
        </p>

        <div class="surface-card rise mt-9 flex max-w-[560px] flex-col items-start gap-3 px-[26px] py-[28px]">
          <h2 class="text-[24px]">Coming soon</h2>
          <p class="m-0 text-[14px] leading-[1.6] text-[var(--color-neutral-500)]">
            The chapter editor, the adaptation pipeline and the story shelf. Until then, your
            account is ready and waiting.
          </p>
          <a routerLink="/app/account" class="btn btn-secondary mt-1">Manage account</a>
        </div>
      </main>
    </div>
  `,
})
export class Dashboard {
  protected readonly auth = inject(AuthIdentityService);
}
