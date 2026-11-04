import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { shortDate, toDay } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { isActive } from '../../loop/model/loop-run';
import { isEmptyResume } from '../../loop/model/resume';
import { Icon } from '../../shared/icon';

/** The resume that goes to this employer: tailored by the AI, then kept on the resume page. */
@Component({
  selector: 'app-tailored-resume',
  imports: [Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'section-rule flex flex-col gap-3', role: 'region', 'aria-label': 'Resume' },
  template: `
    @let app = application();
    <div class="section-head">
      <h4 class="caption">resume</h4>
      @if (saved()) {
        <button type="button" class="btn-link" [disabled]="starting()" (click)="tailor()">
          tailor again
        </button>
      }
    </div>
    @switch (state()) {
      @case ('going') {
        <a class="tailored-row" routerLink="/app/resume" [queryParams]="{ for: app.id }">
          <span class="spinner h-3.5 w-3.5" aria-hidden="true"></span>
          <span class="flex-1">Tailoring your resume to this job</span>
          <span class="meta">see it</span>
        </a>
      }
      @case ('done') {
        <a
          class="btn btn-sm btn-primary self-start"
          routerLink="/app/resume"
          [queryParams]="{ for: app.id }"
        >
          <span class="research-ready" aria-hidden="true"></span>Review the tailored resume
        </a>
      }
      @case ('failed') {
        <p class="alert m-0" role="alert">
          {{ run()?.error || 'The tailoring stopped before it finished.' }}
        </p>
        <button
          type="button"
          class="btn btn-sm self-start"
          [disabled]="starting()"
          (click)="tailor()"
        >
          Try again
        </button>
      }
      @case ('saved') {
        <a class="tailored-row" routerLink="/app/resume" [queryParams]="{ for: app.id }">
          <app-icon name="resume" [size]="16" />
          <span class="flex-1 font-medium">Tailored resume</span>
          <span class="meta">updated {{ updated() }}</span>
        </a>
      }
      @default {
        @if (generalEmpty()) {
          <p class="m-0 text-[14px] text-[var(--color-dim)]">
            Fill in <a routerLink="/app/resume">your resume</a> first; then the AI can tailor it to
            this job.
          </p>
        } @else {
          <button
            type="button"
            class="btn btn-sm self-start"
            [disabled]="starting()"
            (click)="tailor()"
          >
            @if (starting()) {
              <span class="spinner h-3 w-3" aria-hidden="true"></span>
            } @else {
              <app-icon name="resume" [size]="14" />
            }
            Tailor my resume to this job
          </button>
          <p class="meta m-0">{{ uses() }}</p>
        }
      }
    }
    @if (error()) {
      <p class="alert m-0" role="alert">{{ error() }}</p>
    }
  `,
})
export class TailoredResume {
  private readonly store = inject(LoopStore);
  private readonly runs = inject(LoopRuns);
  private readonly router = inject(Router);

  readonly application = input.required<Application>();

  protected readonly starting = signal(false);
  protected readonly error = signal('');

  protected readonly saved = computed(
    () => this.store.tailoredResumes().get(this.application().id) ?? null,
  );
  protected readonly run = computed(() => {
    const app = this.application();
    return this.runs.find('tailorResume', { companyId: app.companyId, applicationId: app.id });
  });
  protected readonly generalEmpty = computed(() => isEmptyResume(this.store.generalResume()));

  protected readonly state = computed(() => {
    const run = this.run();
    if (run && isActive(run)) {
      return 'going';
    }
    if (run?.state === 'succeeded') {
      return 'done';
    }
    if (run) {
      return 'failed';
    }
    return this.saved() ? 'saved' : 'none';
  });

  protected readonly updated = computed(() => {
    const saved = this.saved();
    return saved ? shortDate(toDay(new Date(saved.lastModified * 1000))) : '';
  });

  protected readonly uses = computed(() => {
    const stories = this.store.stories().length;
    return [
      'from your resume',
      stories && `${stories} ${stories === 1 ? 'story' : 'stories'}`,
      this.application().posting && 'this posting',
    ]
      .filter(Boolean)
      .join(', ')
      .replace(/, ([^,]*)$/, ' and $1');
  });

  protected async tailor(): Promise<void> {
    const app = this.application();
    this.starting.set(true);
    this.error.set('');
    try {
      await this.runs.start(`Tailor · ${app.company}`, {
        kind: 'tailorResume',
        applicationId: app.id,
        instructions: '',
      });
      void this.router.navigate(['/app/resume'], { queryParams: { for: app.id } });
    } catch (err) {
      this.error.set(messageOf(err, 'The tailoring couldn’t start.'));
    } finally {
      this.starting.set(false);
    }
  }
}
