import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { shortDate, toDay } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopApi } from '../../loop/loop-api';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { isActive } from '../../loop/model/loop-run';
import { isEmptyResume, Resume } from '../../loop/model/resume';
import { resumeText } from '../../loop/resume-text';
import { Icon } from '../../shared/icon';
import { RunProgress } from '../shell/run-progress';
import { ResumeEditor } from './resume-editor';
import { ResumePaper } from './resume-paper';

/**
 * The resume that goes to one employer: tailored by the AI from the general resume and the stories,
 * reviewed, kept, then edited, printed or copied. Only the paper prints.
 */
@Component({
  selector: 'app-tailored-resume-page',
  imports: [
    FormsModule,
    Icon,
    NgTemplateOutlet,
    ResumeEditor,
    ResumePaper,
    RouterLink,
    RunProgress,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'mx-auto flex w-full max-w-[1120px] flex-col gap-7' },
  template: `
    @let app = application();
    <div class="no-print dim-scope flex flex-col gap-7">
      <a
        [routerLink]="app ? '/app/loop' : '/app/resume'"
        [queryParams]="app ? { id: app.companyId, application: app.id } : {}"
        class="-mb-3 inline-flex min-h-[40px] items-center gap-1.5 self-start text-[14px] text-[var(--color-muted)] no-underline"
        ><app-icon name="back" [size]="14" />{{ app?.company || 'Resume' }}</a
      >

      @if (!app) {
        @if (store.loaded()) {
          <p class="m-0 text-[var(--color-dim)]">That application isn’t in your loop any more.</p>
        }
      } @else {
        <header class="flex flex-wrap items-end justify-between gap-4">
          <div class="min-w-0">
            <p class="label m-0 text-[12px]">resume for</p>
            <h1
              class="mt-1.5 mb-0 text-[clamp(26px,3vw,36px)] leading-[1.08] font-semibold tracking-[-0.04em]"
            >
              {{ app.role || 'The role' }} · {{ app.company }}
            </h1>
            @if (saved(); as resume) {
              <p class="meta mt-1.5 mb-0">kept · updated {{ updated(resume) }}</p>
            }
          </div>
          @if (saved() && !draft()) {
            <div class="flex flex-wrap items-center gap-2">
              <div role="group" aria-label="Show" class="mr-2 flex gap-x-[18px]">
                <button
                  type="button"
                  class="chip"
                  [attr.aria-pressed]="mode() === 'paper'"
                  (click)="mode.set('paper')"
                >
                  paper
                </button>
                <button
                  type="button"
                  class="chip"
                  [attr.aria-pressed]="mode() === 'edit'"
                  (click)="mode.set('edit')"
                >
                  edit
                </button>
              </div>
              <button type="button" class="btn btn-sm" (click)="print()">
                <app-icon name="print" [size]="14" />Print or PDF
              </button>
              <button type="button" class="btn btn-sm" (click)="copy()">
                <app-icon name="copy" [size]="14" />{{ copied() ? 'Copied' : 'Copy text' }}
              </button>
            </div>
          }
        </header>

        @if (run(); as r) {
          @if (active(r)) {
            <div class="resume-run" role="status">
              <app-icon name="resume" [size]="16" />
              <div class="flex min-w-0 flex-1 flex-col gap-1.5">
                <span class="font-medium">Tailoring your resume to this job</span>
                <app-run-progress [run]="r" />
              </div>
              <button type="button" class="btn-link" (click)="runs.cancel(r.id)">stop</button>
            </div>
          } @else if (r.state === 'succeeded') {
            @if (draft(); as d) {
              <section class="resume-review" aria-labelledby="tailored-review-title">
                <div class="flex flex-wrap items-start justify-between gap-4">
                  <div class="flex flex-col gap-1">
                    <h2 id="tailored-review-title" class="caption m-0">tailored just now</h2>
                    <p class="m-0 text-[14.5px] text-[var(--color-muted)]">
                      Read it through before you keep it.
                      @if (saved()) {
                        Keeping it replaces the one you have.
                      }
                    </p>
                  </div>
                  <div class="flex gap-2">
                    <button
                      type="button"
                      class="btn btn-sm btn-primary"
                      [disabled]="busy()"
                      (click)="keep(r.id, d)"
                    >
                      @if (busy()) {
                        <span class="spinner" aria-hidden="true"></span>
                      }
                      Keep it
                    </button>
                    <button
                      type="button"
                      class="btn btn-sm"
                      [disabled]="busy()"
                      (click)="runs.dismiss([r.id])"
                    >
                      Discard
                    </button>
                  </div>
                </div>
                @if (d.notes.length) {
                  <ul class="resume-notes">
                    @for (note of d.notes; track $index) {
                      <li>{{ note }}</li>
                    }
                  </ul>
                }
              </section>
            } @else {
              <p class="m-0 text-[var(--color-dim)]">Opening what it wrote…</p>
            }
          } @else {
            <div class="resume-run" role="alert">
              <app-icon name="resume" [size]="16" />
              <span class="flex-1 text-[14px] text-[var(--color-amber)]">{{
                r.error || 'The tailoring stopped before it finished.'
              }}</span>
              <button type="button" class="btn-link" (click)="runs.dismiss([r.id])">dismiss</button>
            </div>
          }
        }

        @if (!run() || !active(run()!)) {
          @if (!saved() && !draft()) {
            <section class="flex max-w-[640px] flex-col items-start gap-4" aria-label="Tailor">
              <p class="m-0 text-[16px] text-[var(--color-text-2)]">
                The AI writes the resume for this job from {{ sources() }}, leading with what
                {{ app.company }} asks for. It’s told to add nothing they don’t say.
              </p>
              @if (generalEmpty()) {
                <p class="m-0 text-[15px] text-[var(--color-muted)]">
                  Your <a routerLink="/app/resume">resume</a> is empty. Build it from git, read in a
                  resume you have, or fill it in first.
                </p>
              } @else {
                <ng-container [ngTemplateOutlet]="ask" />
              }
            </section>
          } @else if (!draft()) {
            @if (asking()) {
              <section
                class="flex max-w-[640px] flex-col items-start gap-3"
                aria-label="Tailor again"
              >
                <ng-container [ngTemplateOutlet]="ask" />
              </section>
            } @else {
              <button type="button" class="btn-link self-start" (click)="asking.set(true)">
                tailor it again
              </button>
            }
          }
        }

        @if (saved()?.notes?.length && !draft()) {
          <section class="resume-review" aria-label="What the AI leaned on">
            <h2 class="caption m-0">what it leaned on</h2>
            <ul class="resume-notes">
              @for (note of saved()!.notes; track $index) {
                <li>{{ note }}</li>
              }
            </ul>
          </section>
        }

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
      }
    </div>

    @if (draft(); as d) {
      <app-resume-paper class="print-sheet" [resume]="d" />
    } @else if (saved(); as resume) {
      @if (mode() === 'edit') {
        <app-resume-editor class="no-print" [resume]="resume" [save]="saveTailored" />
      }
      <app-resume-paper
        class="print-sheet"
        [class.screen-hidden]="mode() === 'edit'"
        [resume]="resume"
      />
      @if (confirmingDelete()) {
        <div class="no-print flex flex-wrap items-center gap-2.5">
          <span class="text-[13px] text-[var(--color-muted)]"
            >Delete this tailored resume for good?</span
          >
          <button
            type="button"
            class="btn btn-sm btn-danger"
            [disabled]="busy()"
            (click)="remove(resume)"
          >
            Delete
          </button>
          <button type="button" class="btn-link" (click)="confirmingDelete.set(false)">keep</button>
        </div>
      } @else {
        <button
          type="button"
          class="btn-link no-print self-start"
          (click)="confirmingDelete.set(true)"
        >
          delete tailored resume
        </button>
      }
    }

    <ng-template #ask>
      <label class="field w-full">
        <span>anything to lean on or leave out · optional</span>
        <textarea
          class="input"
          rows="2"
          maxlength="2000"
          placeholder="Lead with the payments work; leave out the agency years."
          [(ngModel)]="instructions"
        ></textarea>
      </label>
      <button type="button" class="btn btn-primary" [disabled]="starting()" (click)="tailor()">
        @if (starting()) {
          <span class="spinner" aria-hidden="true"></span>
        } @else {
          <app-icon name="resume" [size]="15" />
        }
        {{ saved() ? 'Tailor it again' : 'Tailor my resume' }}
      </button>
    </ng-template>
  `,
})
export class TailoredResumePage {
  protected readonly store = inject(LoopStore);
  protected readonly runs = inject(LoopRuns);
  private readonly api = inject(LoopApi);

  readonly applicationId = input.required<string>();

  protected readonly active = isActive;
  protected readonly mode = signal<'paper' | 'edit'>('paper');
  protected readonly asking = signal(false);
  protected readonly starting = signal(false);
  protected readonly busy = signal(false);
  protected readonly copied = signal(false);
  protected readonly confirmingDelete = signal(false);
  protected readonly error = signal('');
  protected instructions = '';
  /** A finished run's resume, waiting to be kept or thrown away. */
  private readonly loaded = signal<{ runId: string; resume: Resume } | null>(null);

  protected readonly application = computed(
    () => this.store.applicationsById().get(this.applicationId()) ?? null,
  );
  protected readonly saved = computed(
    () => this.store.tailoredResumes().get(this.applicationId()) ?? null,
  );
  protected readonly generalEmpty = computed(() => isEmptyResume(this.store.generalResume()));
  protected readonly sources = computed(() => {
    const stories = this.store.stories().length;
    const posting = this.application()?.posting ? ' and the posting' : '';
    return stories
      ? `your resume, your ${stories === 1 ? 'story' : `${stories} stories`}${posting}`
      : `your resume${posting}`;
  });
  protected readonly run = computed(() => {
    const app = this.application();
    return app
      ? this.runs.find('tailorResume', { companyId: app.companyId, applicationId: app.id })
      : null;
  });
  protected readonly draft = computed(() => {
    const run = this.run();
    const loaded = this.loaded();
    return run?.state === 'succeeded' && loaded?.runId === run.id ? loaded.resume : null;
  });

  constructor() {
    effect(() => {
      const run = this.run();
      if (run?.state === 'succeeded' && untracked(this.loaded)?.runId !== run.id) {
        void this.load(run.id);
      }
    });
  }

  protected updated(resume: Resume): string {
    return shortDate(toDay(new Date(resume.lastModified * 1000)));
  }

  protected readonly saveTailored = (resume: Resume) =>
    this.store.saveResume({ ...resume, applicationId: this.applicationId() });

  protected async tailor(): Promise<void> {
    const app = this.application();
    if (!app || this.starting()) {
      return;
    }
    this.starting.set(true);
    this.error.set('');
    try {
      await this.runs.start(`Tailor · ${app.company}`, {
        kind: 'tailorResume',
        applicationId: app.id,
        instructions: this.instructions.trim(),
      });
      this.asking.set(false);
    } catch (err) {
      this.error.set(messageOf(err, 'The tailoring couldn’t start.'));
    } finally {
      this.starting.set(false);
    }
  }

  protected async keep(runId: string, resume: Resume): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveResume({ ...resume, id: '', applicationId: this.applicationId() });
      await this.runs.dismiss([runId]).catch(() => undefined);
      this.mode.set('paper');
    } catch (err) {
      this.error.set(messageOf(err, 'The resume couldn’t be kept.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected async remove(resume: Resume): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.deleteResume(resume.id);
      this.confirmingDelete.set(false);
    } catch (err) {
      this.error.set(messageOf(err, 'The resume couldn’t be deleted.'));
    } finally {
      this.busy.set(false);
    }
  }

  protected print(): void {
    window.print();
  }

  protected async copy(): Promise<void> {
    const resume = this.saved();
    if (!resume) {
      return;
    }
    try {
      await navigator.clipboard.writeText(resumeText(resume));
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      this.error.set('Your browser wouldn’t copy it. Use Print or PDF instead.');
    }
  }

  private async load(runId: string): Promise<void> {
    try {
      const details = await this.api.getRun(runId);
      if (details.result?.kind === 'resume') {
        this.loaded.set({ runId, resume: details.result.resume });
      }
    } catch (err) {
      this.error.set(messageOf(err, 'What it wrote couldn’t be opened.'));
    }
  }
}
