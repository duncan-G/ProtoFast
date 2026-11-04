import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { FieldSpec, FieldsForm } from '../../editors/fields-form';
import { ListField } from '../../editors/list-field';
import { LoopError } from '../../loop/errors';
import { ResumeGroup } from '../../loop/model/resume-group';

/**
 * Named lists, such as skills by kind: each list's items edited in place, and the lists themselves
 * (names, order, which there are) as lines of text, "name: item, item".
 */
@Component({
  selector: 'app-resume-groups',
  imports: [FieldsForm, ListField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-5 dim-scope' },
  template: `
    @if (editing()) {
      <app-fields-form
        [heading]="label()"
        [fields]="lineFields()"
        [value]="{ lines: lines() }"
        [save]="saveLines"
        (closed)="editing.set(false)"
      />
    } @else {
      <div class="section-head">
        <h3 class="caption">{{ label() }}</h3>
        <button type="button" class="btn-link" (click)="editing.set(true)">
          {{ groups().length ? 'edit lists' : '+ ' + noun() }}
        </button>
      </div>
      @for (group of groups(); track $index) {
        <app-list-field
          [label]="group.name"
          [items]="group.items"
          [placeholder]="placeholder()"
          [look]="look()"
          [save]="saveItems($index)"
        />
      } @empty {
        <p class="m-0 text-[14px] text-[var(--color-dim)]">{{ empty() }}</p>
      }
    }
  `,
})
export class ResumeGroups {
  readonly label = input.required<string>();
  readonly noun = input.required<string>();
  readonly groups = input.required<readonly ResumeGroup[]>();
  readonly look = input<'rows' | 'tags'>('tags');
  readonly placeholder = input('');
  readonly example = input('');
  readonly empty = input('');
  /** What goes between items on a line. */
  readonly separator = input<',' | ';'>(',');
  readonly save = input.required<(groups: ResumeGroup[]) => Promise<unknown>>();

  protected readonly editing = signal(false);

  protected readonly lineFields = computed((): FieldSpec[] => [
    {
      key: 'lines',
      label: 'one list per line',
      hint: `name: item${this.separator()} item`,
      type: 'textarea',
      look: 'mono',
      placeholder: this.example(),
    },
  ]);

  protected readonly lines = computed(() =>
    this.groups()
      .map((g) => `${g.name}: ${g.items.join(`${this.separator()} `)}`)
      .join('\n'),
  );

  protected saveItems(index: number): (items: string[]) => Promise<unknown> {
    return (items) => this.save()(this.groups().map((g, i) => (i === index ? { ...g, items } : g)));
  }

  protected readonly saveLines = (changes: Record<string, unknown>) => {
    const groups: ResumeGroup[] = [];
    for (const line of String(changes['lines'] ?? '').split('\n')) {
      if (!line.trim()) {
        continue;
      }
      const colon = line.indexOf(':');
      if (colon <= 0) {
        throw new LoopError(
          `“${line.trim()}” needs a name before a colon, like “languages: Go, Rust”.`,
        );
      }
      const items = line
        .slice(colon + 1)
        .split(this.separator())
        .map((item) => item.trim())
        .filter(Boolean);
      groups.push({ name: line.slice(0, colon).trim(), items: [...new Set(items)] });
    }
    return this.save()(groups);
  };
}
