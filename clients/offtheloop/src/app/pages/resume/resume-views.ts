import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LoopStore } from '../../loop/loop-store';

/** The resume page's two halves: the general resume, and the stories it draws on. */
@Component({
  selector: 'app-resume-views',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav aria-label="Resume" class="flex flex-wrap gap-x-[18px] gap-y-1">
      <a
        class="chip"
        routerLink="/app/resume"
        [attr.aria-current]="current() === 'resume' ? 'page' : null"
        >resume</a
      >
      <a
        class="chip"
        routerLink="/app/resume"
        [queryParams]="{ view: 'stories' }"
        [attr.aria-current]="current() === 'stories' ? 'page' : null"
        >stories<span class="count">{{ store.stories().length }}</span></a
      >
    </nav>
  `,
})
export class ResumeViews {
  protected readonly store = inject(LoopStore);

  readonly current = input.required<'resume' | 'stories'>();
}
