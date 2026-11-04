import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { Status, STATUSES } from '../loop/model/status';

export type StatusFilter = Status | 'all';

const FILTERS: readonly { filter: StatusFilter; label: string }[] = [
  { filter: 'all', label: 'all' },
  ...STATUSES.map((s) => ({ filter: s.status, label: s.label })),
];

/** No filter shows everything still open. */
export function passes(status: Status, filter: StatusFilter | null): boolean {
  switch (filter) {
    case 'all':
      return true;
    case null:
      return status !== 'closed';
    default:
      return status === filter;
  }
}

/** The stage legend as a filter: one at a time, pressed again to show the open ones. */
@Component({
  selector: 'app-stage-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'legend', role: 'group', 'aria-label': 'Filter by stage' },
  template: `
    @for (f of filters; track f.filter) {
      <button
        type="button"
        class="chip inline-flex items-center gap-1.5 text-[12px]"
        [attr.aria-pressed]="status() === f.filter"
        (click)="status.set(status() === f.filter ? null : f.filter)"
      >
        @if (f.filter !== 'all') {
          <span class="dot" [class]="'dot-' + f.filter"></span>
        }
        {{ f.label }}
      </button>
    }
  `,
})
export class StageFilter {
  readonly status = model<StatusFilter | null>(null);
  protected readonly filters = FILTERS;
}
