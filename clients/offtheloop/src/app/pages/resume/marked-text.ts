import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { markParts } from '../../loop/resume-reading';

/** Text with each match of a find marked. */
@Component({
  selector: 'app-marked',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (part of parts(); track $index) {
      @if (part.hit) {
        <mark class="find-hit">{{ part.text }}</mark>
      } @else {
        <span>{{ part.text }}</span>
      }
    }
  `,
})
export class MarkedText {
  readonly text = input.required<string>();
  /** A find key, as findKey makes it. */
  readonly find = input('');

  protected readonly parts = computed(() => markParts(this.text(), this.find()));
}
