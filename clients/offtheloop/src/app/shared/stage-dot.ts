import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Status } from '../loop/model/status';

@Component({
  selector: 'app-stage-dot',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex w-[14px] flex-none justify-center', 'aria-hidden': 'true' },
  template: `<span class="dot" [class]="'dot-' + status()"></span>`,
})
export class StageDot {
  readonly status = input.required<Status>();
}
