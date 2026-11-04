import {
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';

/** "$8.4B (FY2025)" is the figure $8.4B, with FY2025 as what it counts. */
export function figureParts(value: string): { figure: string; context: string } {
  const match = /^(.*?\S)\s*\(([^()]+)\)$/.exec(value.trim());
  return match ? { figure: match[1], context: match[2] } : { figure: value.trim(), context: '' };
}

/** A figure over its label; bracketed context moves down beside the label instead of crowding it. */
@Component({
  selector: 'app-figure',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-w-0 flex-col gap-1' },
  template: `
    @let parts = split();
    <span
      class="figure"
      [class.figure-sm]="small()"
      [style.color]="!parts.figure ? 'var(--color-edge-2)' : (color() ?? 'var(--color-text)')"
      >{{ parts.figure || '—' }}</span
    >
    <span class="figure-label"
      >{{ label() }}
      @if (parts.context) {
        <span class="figure-context">· {{ parts.context }}</span>
      }
    </span>
  `,
})
export class Figure {
  readonly value = input.required<string>();
  readonly label = input.required<string>();
  readonly small = input(false, { transform: booleanAttribute });
  readonly color = input<string>();

  protected readonly split = computed(() => figureParts(this.value()));
}
