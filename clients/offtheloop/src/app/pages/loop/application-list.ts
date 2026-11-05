import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { glanceLine, stageName, stageSegments } from '../../loop/application-glance';
import { shortDate } from '../../loop/days';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { Icon } from '../../shared/icon';

type Show = 'open' | 'closed' | 'all';

/** A company's applications once there are too many for cards: open ones first, closed below. */
@Component({
  selector: 'app-application-list',
  imports: [FormsModule, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-6' },
  template: `
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="seg" role="group" aria-label="Show">
        @for (tab of tabs(); track tab.id) {
          <button type="button" [attr.aria-pressed]="tab.id === show()" (click)="show.set(tab.id)">
            {{ tab.label }}<span class="seg-count">{{ tab.count }}</span>
          </button>
        }
      </div>
      <label class="search-box w-full max-w-[320px]">
        <app-icon name="search" [size]="15" />
        <span class="sr-only">Find a role</span>
        <input
          type="search"
          name="query"
          autocomplete="off"
          placeholder="Find a role"
          [(ngModel)]="query"
        />
      </label>
    </div>

    @for (group of groups(); track group.id) {
      <section class="flex flex-col gap-2.5" [attr.aria-label]="group.label">
        <h2 class="field-label text-[13px] font-medium">
          {{ group.label }} · {{ group.rows.length }}
        </h2>
        <div
          class="app-rows"
          [class.is-open]="group.id === 'open'"
          [class.is-closed]="group.id === 'closed'"
        >
          @for (row of group.rows; track row.id) {
            <a
              class="app-row"
              routerLink="/app/loop"
              [queryParams]="{ application: row.id }"
              queryParamsHandling="merge"
            >
              <span class="app-row-role">{{ row.role }}</span>
              <span class="app-row-stage">
                <span class="mini-stages is-small" aria-hidden="true">
                  @for (seg of row.segments; track $index) {
                    <span [style.background]="seg"></span>
                  }
                </span>
                {{ row.stage }}
              </span>
              <span class="app-row-when" [style.color]="row.hot ? 'var(--color-amber)' : null">{{
                row.line
              }}</span>
              <span class="app-row-day">{{ row.applied }}</span>
              <app-icon name="next" [size]="15" class="text-[var(--color-dim)]" />
            </a>
          }
        </div>
      </section>
    } @empty {
      <p class="quiet py-6 text-center">No role matches.</p>
    }
  `,
})
export class ApplicationList {
  private readonly store = inject(LoopStore);

  readonly applications = input.required<readonly Application[]>();

  protected readonly show = signal<Show>('all');
  protected readonly query = signal('');

  private readonly rows = computed(() => {
    const now = this.store.today();
    return this.applications().map((app) => ({
      id: app.id,
      closed: app.closed,
      role: app.role || 'Role not set',
      stage: stageName(app),
      segments: stageSegments(app),
      line: glanceLine(app, now),
      hot: !app.closed && !!app.nextOn,
      applied: app.appliedOn ? shortDate(app.appliedOn) : '',
    }));
  });

  protected readonly tabs = computed(() => {
    const closed = this.rows().filter((row) => row.closed).length;
    return [
      { id: 'open' as const, label: 'Open', count: this.rows().length - closed },
      { id: 'closed' as const, label: 'Closed', count: closed },
      { id: 'all' as const, label: 'All', count: this.rows().length },
    ];
  });

  protected readonly groups = computed(() => {
    const query = this.query().trim().toLowerCase();
    const show = this.show();
    const found = this.rows().filter((row) => !query || row.role.toLowerCase().includes(query));
    return [
      { id: 'open', label: 'Open', rows: found.filter((row) => !row.closed) },
      { id: 'closed', label: 'Closed', rows: found.filter((row) => row.closed) },
    ].filter((group) => group.rows.length && (show === 'all' || show === group.id));
  });
}
