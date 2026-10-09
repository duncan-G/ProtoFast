import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';

/** A short list of free-text states ("wet hair", "broken"), each removable, with an add field. */
@Component({
  selector: 'app-state-chips',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="se-chips se-state-chips">
      @for (entry of state(); track $index) {
        <button
          type="button"
          class="se-chip"
          [title]="'Remove “' + entry + '”'"
          (click)="remove($index)"
        >
          {{ entry }} <span aria-hidden="true">×</span>
        </button>
      }
      <input
        class="se-chip-input"
        [placeholder]="placeholder()"
        [attr.aria-label]="placeholder()"
        maxlength="255"
        [value]="draft()"
        (input)="draft.set($any($event.target).value)"
        (keydown.enter)="$event.preventDefault(); add()"
        (blur)="add()"
      />
    </div>
  `,
})
export class StateChips {
  readonly state = input.required<string[]>();
  readonly placeholder = input('add a state…');

  readonly change = output<string[]>();

  protected readonly draft = signal('');

  protected add(): void {
    const entry = this.draft().trim();
    if (!entry) {
      return;
    }
    this.draft.set('');
    if (!this.state().includes(entry)) {
      this.change.emit([...this.state(), entry]);
    }
  }

  protected remove(index: number): void {
    this.change.emit(this.state().filter((_, i) => i !== index));
  }
}
