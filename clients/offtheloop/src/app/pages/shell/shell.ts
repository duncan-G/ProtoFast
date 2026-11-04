import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { NoteEditor } from '../../editors/note-editor';
import { LoopEditors } from '../../loop/loop-editors';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { Icon } from '../../shared/icon';
import { LoopLogo } from '../../shared/loop-logo';
import { riseIn } from '../../shared/rise-in';
import { RunTray } from './run-tray';

/** The signed-in frame: a rail on wide screens, a tab bar with the jot button on phones. */
@Component({
  selector: 'app-shell',
  imports: [Icon, LoopLogo, NoteEditor, RouterLink, RouterLinkActive, RouterOutlet, RunTray],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <nav class="rail" aria-label="App">
        <a routerLink="/app" aria-label="OffTheLoop home" class="rail-link mb-3.5"
          ><app-loop-logo
        /></a>
        <a
          routerLink="/app"
          routerLinkActive="active"
          [routerLinkActiveOptions]="{ exact: true }"
          ariaCurrentWhenActive="page"
          aria-label="Today"
          class="rail-link"
          ><app-icon name="today"
        /></a>
        <a
          routerLink="/app/notes"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          [attr.aria-label]="notesLabel()"
          class="rail-link"
          ><app-icon name="notes"
        /></a>
        <a
          routerLink="/app/loop"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          aria-label="The loop"
          class="rail-link"
          ><app-icon name="loop"
        /></a>
        <a
          routerLink="/app/account"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          aria-label="Account"
          class="rail-link mt-auto"
          ><app-icon name="account"
        /></a>
      </nav>

      <main #main class="shell-main">
        @if (store.error()) {
          <p class="alert mx-auto mb-6 max-w-[1120px]" role="alert">
            {{ store.error() }}
            <button type="button" class="btn-link ml-3 text-[var(--color-text)]" (click)="retry()">
              Try again
            </button>
          </p>
        }
        <router-outlet />
      </main>

      <nav class="tabs" aria-label="App">
        <a
          routerLink="/app"
          routerLinkActive="active"
          [routerLinkActiveOptions]="{ exact: true }"
          ariaCurrentWhenActive="page"
          aria-label="Today"
          class="tab"
          ><app-icon name="today" [size]="22" [weight]="1.4"
        /></a>
        <a
          routerLink="/app/notes"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          aria-label="Notes"
          class="tab"
          ><app-icon name="notes" [size]="22" [weight]="1.4"
        /></a>
        <span class="flex flex-1 justify-center">
          <button type="button" class="jot" aria-label="Jot a note" (click)="editors.editNote()">
            <app-icon name="plus" [size]="20" [weight]="2" />
          </button>
        </span>
        <a
          routerLink="/app/loop"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          aria-label="The loop"
          class="tab"
          ><app-icon name="loop" [size]="22" [weight]="1.2"
        /></a>
        <a
          routerLink="/app/account"
          routerLinkActive="active"
          ariaCurrentWhenActive="page"
          aria-label="Account"
          class="tab"
          ><app-icon name="account" [size]="22" [weight]="1.4"
        /></a>
      </nav>
    </div>

    <app-run-tray />

    @if (editors.note(); as note) {
      <app-note-editor [draft]="note" (closed)="editors.note.set(null)" />
    }
  `,
})
export class Shell {
  protected readonly store = inject(LoopStore);
  protected readonly editors = inject(LoopEditors);
  private readonly runs = inject(LoopRuns);
  private readonly main = viewChild.required<ElementRef<HTMLElement>>('main');

  protected readonly notesLabel = computed(() =>
    this.store.loaded() ? `Notes, ${this.store.libraryNotes().length}` : 'Notes',
  );

  constructor() {
    // The session cookie never reaches the render, so the data loads in the browser.
    afterNextRender(() => {
      void this.store.load();
      void this.runs.load();
    });

    // Query params stay on the page; a different path is a different page.
    let page = '';
    inject(Router)
      .events.pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe((event) => {
        const next = event.urlAfterRedirects.split(/[?#]/, 1)[0];
        if (page && next !== page) {
          riseIn(this.main().nativeElement);
        }
        page = next;
      });
  }

  protected retry(): void {
    void this.store.refresh();
  }
}
