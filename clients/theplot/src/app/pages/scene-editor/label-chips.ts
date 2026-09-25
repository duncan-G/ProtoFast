import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { LABEL_MAX, labelProblem } from '../../stories/library-names';

@Component({
  selector: 'app-label-chips',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="se-chips">
      @for (label of labels(); track label) {
        <button
          type="button"
          class="se-chip"
          [class.se-chip-script]="script()"
          [attr.aria-pressed]="label === selected()"
          (click)="pick.emit(label)"
        >
          {{ label }}
        </button>
      }
      @if (adding()) {
        <input
          #field
          class="se-chip-input"
          [class.se-chip-script]="script()"
          [placeholder]="placeholder()"
          [attr.maxlength]="max"
          [attr.aria-label]="'New ' + noun()"
          [value]="draft()"
          (input)="draft.set($any($event.target).value); problem.set('')"
          (keydown.enter)="$event.preventDefault(); submit()"
          (keydown.escape)="$event.stopPropagation(); cancel()"
          (blur)="draft().trim() ? null : cancel()"
        />
        <button
          type="button"
          class="se-chip se-chip-add"
          (mousedown)="$event.preventDefault()"
          (click)="submit()"
        >
          Add
        </button>
      } @else {
        <button
          type="button"
          class="se-chip se-chip-add"
          [class.se-chip-script]="script()"
          (click)="open()"
        >
          + Add {{ noun() }}
        </button>
      }
    </div>
    @if (problem()) {
      <p class="se-error mt-1.5">{{ problem() }}</p>
    }
  `,
})
export class LabelChips {
  readonly labels = input.required<string[]>();
  readonly selected = input<string | null>(null);
  readonly noun = input.required<string>();
  readonly placeholder = input('');
  readonly script = input(false);
  /** Resolves with why the label was refused, or null once added. */
  readonly add = input.required<(label: string) => Promise<string | null>>();

  readonly pick = output<string>();

  protected readonly max = LABEL_MAX;
  protected readonly adding = signal(false);
  protected readonly draft = signal('');
  protected readonly problem = signal('');
  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');

  protected open(): void {
    this.adding.set(true);
    this.draft.set('');
    this.problem.set('');
    setTimeout(() => this.field()?.nativeElement.focus());
  }

  protected cancel(): void {
    this.adding.set(false);
    this.problem.set('');
  }

  protected async submit(): Promise<void> {
    const label = this.draft().trim().toUpperCase();
    const problem = labelProblem(this.labels(), label) ?? (await this.add()(label));
    if (problem) {
      this.problem.set(problem);
      this.field()?.nativeElement.focus();
      return;
    }
    this.adding.set(false);
    this.pick.emit(label);
  }
}
