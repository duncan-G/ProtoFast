import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { DirectorJobs } from '../../direction/director-jobs';
import { DirectorJob, isActive } from '../../direction/model/director-job';
import { SceneEditorStore } from './scene-editor-store';

/** One row of the expanded tray, reduced to what it shows. */
interface TrayRow {
  job: DirectorJob;
  kind: 'active' | 'done' | 'failed';
  /** The scene or library it works on. */
  context: string;
  status: string;
  /** Width of the thin bar, or null for none. */
  percent: number | null;
  note: string | null;
  /** It works on a scene other than the open one. */
  openable: boolean;
}

/**
 * The director's work tray: a pill pinned bottom-right that sums up the model work queued in the
 * background, and opens into a list with one row each. Work keeps running while the author writes,
 * moves between scenes or reloads; what it finishes lands in the open scene as it is done.
 */
@Component({
  selector: 'app-director-job-tray',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rows().length > 0) {
      <div class="tray wt">
        @if (expanded()) {
          <div class="tray-panel wt-panel rise" role="region" aria-label="Director work">
            <div class="wt-head">
              <h3>Work</h3>
              <span class="wt-count">{{ countText() }}</span>
              <button
                type="button"
                class="wt-x"
                aria-label="Minimise work"
                (click)="expanded.set(false)"
              >
                ▾
              </button>
            </div>
            <div class="wt-rows">
              @for (row of rows(); track row.job.id) {
                <div class="wt-row">
                  <div class="flex min-w-0 flex-1 flex-col gap-[5px]">
                    <div class="wt-label">{{ row.job.label }}</div>
                    <div class="wt-context">{{ row.context }}</div>
                    <div class="wt-status" [class]="'is-' + row.kind">
                      @if (row.kind === 'active') {
                        <span class="spinner h-2.5 w-2.5" aria-hidden="true"></span>
                      } @else {
                        <span class="wt-dot"></span>
                      }
                      <span>{{ row.status }}</span>
                    </div>
                    @if (row.percent !== null) {
                      <div class="wt-bar">
                        <div [style.width.%]="row.percent"></div>
                      </div>
                    }
                    @if (row.note) {
                      <div class="wt-note">{{ row.note }}</div>
                    }
                    @if (row.openable) {
                      <div class="mt-0.5">
                        <button type="button" class="dr-btn" (click)="open(row.job)">
                          Open scene
                        </button>
                      </div>
                    }
                  </div>
                  @if (row.kind === 'active') {
                    <button
                      type="button"
                      class="wt-x"
                      aria-label="Cancel"
                      [disabled]="row.job.id.startsWith('starting-')"
                      (click)="cancel(row.job)"
                    >
                      ×
                    </button>
                  } @else {
                    <button
                      type="button"
                      class="wt-x"
                      aria-label="Dismiss"
                      (click)="dismiss(row.job)"
                    >
                      ×
                    </button>
                  }
                </div>
              }
            </div>
            @if (finished() > 0) {
              <div class="wt-foot">
                <button type="button" class="dr-btn" (click)="clearAll()">Clear finished</button>
              </div>
            }
          </div>
        }
        <button
          type="button"
          class="tray-pill wt-pill"
          [class.is-active]="active() > 0"
          [attr.aria-expanded]="expanded()"
          (click)="expanded.set(!expanded())"
        >
          @if (active() > 0) {
            <span class="wt-status is-active"
              ><span class="spinner" aria-hidden="true"></span
            ></span>
          } @else {
            <span class="wt-status" [class]="failed() > 0 ? 'is-failed' : 'is-done'"
              ><span class="wt-dot"></span
            ></span>
          }
          <span>{{ summary() }}</span>
          <span class="wt-caret">{{ expanded() ? '▾' : '▴' }}</span>
        </button>
      </div>
    }
  `,
})
export class DirectorJobTray {
  private readonly jobs = inject(DirectorJobs);
  private readonly store = inject(SceneEditorStore);
  private readonly router = inject(Router);

  protected readonly expanded = signal(false);

  private readonly all = computed(() => this.jobs.jobs());
  protected readonly active = computed(() => this.all().filter(isActive).length);
  protected readonly running = computed(
    () => this.all().filter((j) => j.state === 'running').length,
  );
  protected readonly done = computed(
    () => this.all().filter((j) => j.state === 'succeeded').length,
  );
  protected readonly failed = computed(() => this.all().filter((j) => j.state === 'failed').length);
  protected readonly finished = computed(() => this.all().length - this.active());

  protected readonly summary = computed(() => {
    const active = this.active();
    const failed = this.failed();
    if (active > 0) {
      const waiting = active - this.running();
      return waiting > 0 && this.running() > 0
        ? `Working on ${this.running()} · ${waiting} waiting`
        : this.running() > 0
          ? `Working on ${this.running()}`
          : `${waiting} waiting`;
    }
    if (failed > 0) {
      return `${failed} need${failed > 1 ? '' : 's'} attention`;
    }
    return this.done() > 0 ? `${this.done()} done` : 'Cancelled';
  });

  protected readonly countText = computed(() =>
    [
      this.active() && `${this.active()} to do`,
      this.done() && `${this.done()} done`,
      this.failed() && `${this.failed()} failed`,
    ]
      .filter(Boolean)
      .join(' · '),
  );

  /** Newest first. */
  protected readonly rows = computed<TrayRow[]>(() => {
    const all = this.all();
    return [...all].reverse().map((job): TrayRow => {
      const row = {
        job,
        context: this.contextOf(job),
        percent: null,
        note: null,
        openable: !!job.sceneId && job.sceneId !== this.store.scene()?.id,
      } as const;
      const steps = job.steps.length;
      switch (job.state) {
        case 'queued': {
          const ahead = all.find(
            (j) =>
              j !== job && j.lane && j.lane === job.lane && isActive(j) && j.created <= job.created,
          );
          return {
            ...row,
            kind: 'active',
            status: ahead ? `Waiting for “${ahead.label}”` : 'Waiting to start',
          };
        }
        case 'running':
          return {
            ...row,
            kind: 'active',
            status:
              steps > 1 ? `Step ${Math.min(job.stepsDone + 1, steps)} of ${steps}` : 'Working…',
            percent: steps > 1 ? (job.stepsDone / steps) * 100 : null,
          };
        case 'succeeded':
          return { ...row, kind: 'done', status: 'Done' };
        case 'failed':
          return {
            ...row,
            kind: 'failed',
            status: steps > 1 ? `Stopped at step ${job.stepsDone + 1} of ${steps}` : 'Failed',
            note: job.error || null,
          };
        case 'cancelled':
          return {
            ...row,
            kind: 'failed',
            status:
              job.stepsDone > 0 ? `Cancelled after ${job.stepsDone} of ${steps}` : 'Cancelled',
          };
      }
    });
  });

  protected cancel(job: DirectorJob): void {
    this.jobs.cancel(job.id).catch(() => undefined);
  }

  protected dismiss(job: DirectorJob): void {
    this.jobs.dismiss([job.id]).catch(() => undefined);
  }

  protected clearAll(): void {
    this.jobs.clearFinished().catch(() => undefined);
  }

  protected open(job: DirectorJob): void {
    if (!job.sceneId) {
      return;
    }
    if (job.storyId === this.store.story()?.id) {
      this.store.go(job.sceneId);
    } else {
      void this.router.navigate(['/app/stories', job.storyId, 'scenes', job.sceneId], {
        queryParams: { mode: 'director' },
      });
    }
  }

  private contextOf(job: DirectorJob): string {
    if (job.storyId !== this.store.story()?.id) {
      return 'Another story';
    }
    if (!job.sceneId) {
      return 'Library';
    }
    const place = this.store.places().find((p) => p.summary.id === job.sceneId);
    if (!place) {
      return 'A scene no longer in the story';
    }
    const title = place.summary.title || 'Untitled scene';
    return job.sceneId === this.store.scene()?.id ? `${title} · this scene` : title;
  }
}
