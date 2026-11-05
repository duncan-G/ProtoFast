import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { shortDate, toDay } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { LoopApi } from '../../loop/loop-api';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { isActive } from '../../loop/model/loop-run';
import { blankResume, Resume } from '../../loop/model/resume';
import { Icon } from '../../shared/icon';
import { Notes } from '../notes/notes';
import { RunProgress } from '../shell/run-progress';
import { ResumeImportSheet } from './resume-import-sheet';
import { ResumeReader } from './resume-reader';
import { ResumeReadSheet } from './resume-read-sheet';
import { ResumeReviewSheet } from './resume-review-sheet';
import { ResumeViews } from './resume-views';
import { TailoredResumePage } from './tailored-resume-page';

/**
 * The general resume (everything that could go on one, built from git or read from a resume you
 * have), the stories it draws on, and each resume tailored from it (?for=application).
 */
@Component({
  selector: 'app-resume',
  imports: [
    Icon,
    Notes,
    ResumeImportSheet,
    ResumeReader,
    ResumeReadSheet,
    ResumeReviewSheet,
    ResumeViews,
    RouterLink,
    RunProgress,
    TailoredResumePage,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (view() === 'stories') {
      <app-notes only="story"><app-resume-views current="stories" /></app-notes>
    } @else if (tailoredFor(); as applicationId) {
      <app-tailored-resume-page [applicationId]="applicationId" />
    } @else if (store.loaded()) {
      <app-resume-reader
        [resume]="general()"
        [save]="saveGeneral"
        [stories]="store.stories().length"
      >
        <app-resume-views readerTop current="resume" />
        @if (readRun(); as run) {
          <div readerStatus class="resume-run" role="status">
            <app-icon name="file" [size]="16" />
            <div class="flex min-w-0 flex-1 flex-col gap-1.5">
              <span class="font-medium">{{ run.label }}</span>
              @if (active(run)) {
                <app-run-progress [run]="run" />
              } @else if (run.state === 'succeeded') {
                <span class="meta">read · tick what goes into your resume</span>
              } @else {
                <span class="text-[13.5px] text-[var(--color-amber)]">{{
                  run.error || 'It stopped before it finished.'
                }}</span>
              }
            </div>
            @if (run.state === 'succeeded') {
              <button
                type="button"
                class="btn btn-sm btn-primary"
                [disabled]="opening()"
                (click)="openReview(run.id, run.label)"
              >
                @if (opening()) {
                  <span class="spinner" aria-hidden="true"></span>
                }
                Review
              </button>
            } @else if (active(run)) {
              <button type="button" class="btn-link" (click)="runs.cancel(run.id)">stop</button>
            } @else {
              <button type="button" class="btn-link" (click)="runs.dismiss([run.id])">
                dismiss
              </button>
            }
          </div>
        }
        @if (reviewError()) {
          <p readerStatus class="alert" role="alert">{{ reviewError() }}</p>
        }

        <section class="outline-part" aria-labelledby="resume-tailored">
          <h2 id="resume-tailored" class="caption">tailored from this</h2>
          @for (item of tailored(); track item.resume.id) {
            <a
              class="outline-tailored"
              routerLink="/app/resume"
              [queryParams]="{ for: item.applicationId }"
            >
              <app-icon name="resume" [size]="16" />
              <span class="flex min-w-0 flex-col gap-0.5">
                <span class="text-[14px] leading-[1.35] font-medium">{{ item.role }}</span>
                <span class="text-[13px] text-[var(--color-muted)]"
                  >{{ item.company }} · {{ item.updated }}</span
                >
              </span>
            </a>
          }
          <p>
            {{
              tailored().length
                ? 'Tailor another from a company’s page.'
                : 'Tailor one to a job from its company’s page.'
            }}
          </p>
        </section>

        <section class="outline-part" aria-labelledby="resume-add">
          <h2 id="resume-add" class="caption">add to it</h2>
          <div class="flex flex-wrap gap-2">
            <button type="button" class="btn" (click)="importing.set(true)">
              <app-icon name="upload" [size]="14" />Build from git
            </button>
            <button type="button" class="btn" (click)="reading.set(true)">
              <app-icon name="file" [size]="14" />Read a resume
            </button>
          </div>
          <p>
            Everything that could go on a resume, not the one you send. From a company’s page, the AI
            tailors a resume to each job out of this and your stories.
          </p>
        </section>
      </app-resume-reader>
    } @else {
      <p class="m-0 text-[var(--color-dim)]">Loading…</p>
    }
    @if (importing()) {
      <app-resume-import (closed)="importing.set(false)" />
    }
    @if (reading()) {
      <app-resume-read (closed)="reading.set(false)" />
    }
    @if (review(); as r) {
      <app-resume-review
        [runId]="r.runId"
        [label]="r.label"
        [incoming]="r.resume"
        (closed)="review.set(null)"
      />
    }
  `,
})
export class ResumePage {
  protected readonly store = inject(LoopStore);
  protected readonly runs = inject(LoopRuns);
  private readonly api = inject(LoopApi);
  private readonly params = toSignal(inject(ActivatedRoute).queryParamMap, { requireSync: true });

  protected readonly active = isActive;
  protected readonly importing = signal(false);
  protected readonly reading = signal(false);
  protected readonly opening = signal(false);
  protected readonly reviewError = signal('');
  protected readonly review = signal<{ runId: string; label: string; resume: Resume } | null>(null);

  protected readonly view = computed(() => this.params().get('view'));
  protected readonly tailoredFor = computed(() => this.params().get('for'));

  protected readonly general = computed(() => this.store.generalResume() ?? blankResume());

  protected readonly readRun = computed(() => this.runs.find('readResume', {}));

  protected readonly tailored = computed(() => {
    const applications = this.store.applicationsById();
    return [...this.store.tailoredResumes()].flatMap(([applicationId, resume]) => {
      const application = applications.get(applicationId);
      return application
        ? [
            {
              applicationId,
              resume,
              role: application.role || 'a role',
              company: application.company,
              updated: shortDate(toDay(new Date(resume.lastModified * 1000))),
            },
          ]
        : [];
    });
  });

  protected readonly saveGeneral = (resume: Resume) =>
    this.store.saveResume({ ...resume, applicationId: null });

  protected async openReview(runId: string, label: string): Promise<void> {
    this.opening.set(true);
    this.reviewError.set('');
    try {
      const details = await this.api.getRun(runId);
      if (details.result?.kind !== 'resume') {
        throw new Error('no resume');
      }
      this.review.set({ runId, label, resume: details.result.resume });
    } catch (err) {
      this.reviewError.set(messageOf(err, 'What it read couldn’t be opened.'));
    } finally {
      this.opening.set(false);
    }
  }
}
