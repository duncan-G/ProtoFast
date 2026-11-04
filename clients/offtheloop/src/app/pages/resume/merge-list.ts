import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { MergeRow } from '../../loop/resume-merge';

/** What would go into the general resume, by section, each ticked to keep. */
@Component({
  selector: 'app-merge-list',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ul class="findings">
      @for (group of groups(); track group.section) {
        <li class="findings-group">
          <h4 class="caption mb-1">{{ group.section }}</h4>
          @for (row of group.rows; track row.key) {
            <label class="finding merge-row">
              <input
                type="checkbox"
                class="finding-check"
                [checked]="kept().has(row.key)"
                [disabled]="row.problems.length > 0"
                (change)="toggle.emit(row.key)"
              />
              <span class="finding-value">
                <span class="font-medium">{{ row.title }}</span>
                @if (row.detail) {
                  <span class="merge-detail">{{
                    row.adds ? 'adds ' + row.detail : row.detail
                  }}</span>
                }
                @if (row.replaces) {
                  <span class="finding-was">{{ row.replaces }}</span>
                }
                @for (problem of row.problems; track $index) {
                  <span class="merge-problem">{{ problem }}</span>
                }
              </span>
            </label>
          }
        </li>
      }
    </ul>
  `,
})
export class MergeList {
  readonly rows = input.required<readonly MergeRow[]>();
  readonly kept = input.required<ReadonlySet<string>>();
  readonly toggle = output<string>();

  protected readonly groups = computed(() => {
    const groups: { section: string; rows: MergeRow[] }[] = [];
    for (const row of this.rows()) {
      const group = groups.find((g) => g.section === row.section);
      if (group) {
        group.rows.push(row);
      } else {
        groups.push({ section: row.section, rows: [row] });
      }
    }
    return groups;
  });
}

/** The keys of the rows that start ticked. */
export function tickedKeys(rows: readonly MergeRow[]): Set<string> {
  return new Set(rows.filter((r) => r.ticked && !r.problems.length).map((r) => r.key));
}

export function toggled(keys: ReadonlySet<string>, key: string): Set<string> {
  const next = new Set(keys);
  if (!next.delete(key)) {
    next.add(key);
  }
  return next;
}
