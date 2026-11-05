import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { shortDate } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LearningEntry, learningGroups } from '../../loop/learnings';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { Icon } from '../../shared/icon';
import { LearningAdd } from './learning-add';

const NO_ROUND = 'Not from a round';
const PREVIEW = 3;

/** Everything learned on one application, by round, to search and tidy. */
@Component({
  selector: 'app-learned-log',
  imports: [FormsModule, Icon, LearningAdd, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'mx-auto flex w-full max-w-[820px] flex-col gap-6' },
  template: `
    @let app = application();
    <a
      routerLink="/app/loop"
      [queryParams]="{ view: null }"
      queryParamsHandling="merge"
      class="inline-flex min-h-[40px] items-center gap-1.5 self-start text-[14px] text-[var(--color-muted)] no-underline"
      ><app-icon name="back" [size]="14" />{{ app.company }} · {{ app.role || 'Role not set' }}</a
    >
    <header class="-mt-2">
      <h1 class="company-name flex items-center gap-3 text-[36px]">
        What you learned
        @if (app.learnings.length) {
          <span class="badge text-[13px]">{{ app.learnings.length }}</span>
        }
      </h1>
      <p class="quiet mt-2">Grouped by round, newest first.</p>
    </header>

    <app-learning-add [application]="app" />

    @if (app.learnings.length) {
      <div class="flex flex-col gap-3">
        <label class="search-box">
          <app-icon name="search" [size]="15" />
          <span class="sr-only">Search what you learned</span>
          <input
            type="search"
            name="query"
            autocomplete="off"
            [placeholder]="'Search ' + app.learnings.length + ' notes'"
            [(ngModel)]="query"
          />
        </label>
        @if (groups().length > 1) {
          <div class="chips-row" role="group" aria-label="Round">
            <button
              type="button"
              class="round-chip"
              [attr.aria-pressed]="picked() === null"
              (click)="picked.set(null)"
            >
              All<span class="seg-count">{{ app.learnings.length }}</span>
            </button>
            @for (group of groups(); track group.round) {
              <button
                type="button"
                class="round-chip"
                [attr.aria-pressed]="picked() === group.round"
                (click)="picked.set(group.round)"
              >
                {{ group.round || noRound
                }}<span class="seg-count">{{ group.entries.length }}</span>
              </button>
            }
          </div>
        }
      </div>
    }

    @for (group of shown(); track group.round) {
      <section class="card card-pad gap-2" [attr.aria-label]="group.round || noRound">
        <div class="card-head">
          <h2 class="card-title flex-wrap items-baseline">
            {{ group.round || noRound }}
            @if (group.latest) {
              <span class="meta text-[12.5px] font-normal">{{ shortDate(group.latest) }}</span>
            }
          </h2>
          @if (editing() === group.round) {
            <div class="card-actions">
              <button type="button" class="btn btn-xs" (click)="editing.set(null)">Cancel</button>
              <button
                type="button"
                class="btn btn-xs btn-primary"
                [disabled]="busy()"
                (click)="saveGroup(group.entries)"
              >
                Save
              </button>
            </div>
          } @else {
            <button type="button" class="btn btn-xs" (click)="edit(group.entries, group.round)">
              <app-icon name="edit" [size]="13" />Edit
            </button>
          }
        </div>
        @if (editing() === group.round) {
          <ul class="learned-list">
            @for (entry of group.entries; track entry.at) {
              <li class="item gap-2" [hidden]="dropped().has(entry.at)">
                <label class="sr-only" [for]="'learned-' + entry.at">Entry</label>
                <input
                  class="item-input"
                  autocomplete="off"
                  [id]="'learned-' + entry.at"
                  [value]="drafts[entry.at] ?? ''"
                  (input)="drafts[entry.at] = $any($event.target).value"
                />
                <button
                  type="button"
                  class="btn btn-icon btn-xs h-8 min-h-8 w-8"
                  [attr.aria-label]="'Remove ' + entry.text"
                  (click)="drop(entry.at)"
                >
                  <app-icon name="close" [size]="12" />
                </button>
              </li>
            }
          </ul>
          <p class="hint">An emptied entry is removed when you save.</p>
        } @else {
          <ul class="learned-list">
            @for (entry of visible(group.round, group.entries); track entry.at) {
              <li>
                <p class="learned-text">{{ entry.text }}</p>
              </li>
            }
          </ul>
          @if (canExpand(group.entries)) {
            <button
              type="button"
              class="btn btn-xs mt-1 self-start"
              [attr.aria-expanded]="open().has(group.round)"
              (click)="toggle(group.round)"
            >
              {{ open().has(group.round) ? 'Show fewer' : 'Show all ' + group.entries.length }}
              <app-icon name="down" [size]="12" [class.rotate-180]="open().has(group.round)" />
            </button>
          }
        }
      </section>
    } @empty {
      @if (app.learnings.length) {
        <p class="quiet py-8 text-center">Nothing matches.</p>
      }
    }
    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }
  `,
})
export class LearnedLog {
  private readonly store = inject(LoopStore);

  readonly application = input.required<Application>();

  protected readonly noRound = NO_ROUND;
  protected readonly shortDate = shortDate;
  protected readonly query = signal('');
  protected readonly picked = signal<string | null>(null);
  protected readonly open = signal(new Set<string>());
  protected readonly editing = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly dropped = signal(new Set<number>());
  protected drafts: Record<number, string> = {};

  protected readonly groups = computed(() => learningGroups(this.application().learnings));

  protected readonly shown = computed(() => {
    const query = this.query().trim().toLowerCase();
    const picked = this.picked();
    return this.groups()
      .filter((group) => picked === null || group.round === picked)
      .map((group) => ({
        ...group,
        entries: query
          ? group.entries.filter((entry) => entry.text.toLowerCase().includes(query))
          : group.entries,
      }))
      .filter((group) => group.entries.length);
  });

  /** One round picked, or a search under way, shows every entry it finds. */
  protected canExpand(entries: LearningEntry[]): boolean {
    return this.picked() === null && !this.query().trim() && entries.length > PREVIEW;
  }

  protected visible(round: string, entries: LearningEntry[]): LearningEntry[] {
    return this.canExpand(entries) && !this.open().has(round) ? entries.slice(0, PREVIEW) : entries;
  }

  protected toggle(round: string): void {
    this.open.update((open) => {
      const next = new Set(open);
      if (!next.delete(round)) {
        next.add(round);
      }
      return next;
    });
  }

  protected edit(entries: LearningEntry[], round: string): void {
    this.drafts = Object.fromEntries(entries.map((entry) => [entry.at, entry.text]));
    this.dropped.set(new Set());
    this.error.set('');
    this.editing.set(round);
  }

  protected drop(at: number): void {
    this.drafts[at] = '';
    this.dropped.update((dropped) => new Set(dropped).add(at));
  }

  protected async saveGroup(entries: LearningEntry[]): Promise<void> {
    const app = this.application();
    const changed = new Map(
      entries.map((entry) => [entry.at, (this.drafts[entry.at] ?? '').trim()]),
    );
    const learnings = app.learnings
      .map((learning, at) =>
        changed.has(at) ? { ...learning, text: changed.get(at) ?? '' } : learning,
      )
      .filter((learning) => learning.text);
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveApplication({ ...app, learnings });
      this.editing.set(null);
    } catch (err) {
      this.error.set(messageOf(err, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}
