import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AccountMenu } from './account-menu';

/** The frame every console page sits in. Project nav links with the `consoleNav` attribute. */
@Component({
  selector: 'kit-console-shell',
  imports: [AccountMenu, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-screen bg-gray-50">
      <header
        class="flex items-center justify-between gap-4 px-4 py-3 bg-white border-b border-gray-200"
      >
        <div class="flex items-center gap-6">
          <a routerLink="/" class="font-semibold text-gray-900">{{ title() }}</a>
          <nav class="flex items-center gap-4 text-sm text-gray-600">
            <ng-content select="[consoleNav]" />
          </nav>
        </div>
        <kit-account-menu />
      </header>

      <main class="mx-auto max-w-5xl px-4 py-10">
        <ng-content />
      </main>
    </div>
  `,
})
export class ConsoleShell {
  readonly title = input.required<string>();
}
