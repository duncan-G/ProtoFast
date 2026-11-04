import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { messageOf } from '../../loop/errors';
import { LoopStore } from '../../loop/loop-store';
import { readResumeFile, RESUME_FILE, resumePrompt, ResumeRead } from '../../loop/resume-import';
import { applyMerge, mergeRows } from '../../loop/resume-merge';
import { AgentPrompt } from '../../shared/agent-prompt';
import { AgentFile, AgentUpload } from '../../shared/agent-upload';
import { Icon } from '../../shared/icon';
import { MergeList, tickedKeys, toggled } from './merge-list';

/** Everything the user's own agent found in their git history, ticked into the general resume. */
@Component({
  selector: 'app-resume-import',
  imports: [AgentPrompt, AgentUpload, Icon, MergeList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #sheet
      class="sheet"
      aria-labelledby="resume-import-title"
      (close)="closed.emit()"
      (click)="closeOnBackdrop($event)"
    >
      <div class="sheet-body">
        <div class="flex items-start justify-between gap-4">
          <div class="flex flex-col gap-2">
            <h2 id="resume-import-title" class="m-0 text-[24px] font-semibold tracking-[-0.04em]">
              Build it from git
            </h2>
            <p class="m-0 text-[14.5px] text-[var(--color-muted)]">
              Your own AI agent reads your git history on your computer and lists everything that
              could go on your resume: jobs, projects, highlights and skills, backed by your
              commits. Only the file it writes comes here.
            </p>
          </div>
          <button
            type="button"
            class="btn btn-icon -mt-1 -mr-3 border-0"
            aria-label="Close"
            (click)="close()"
          >
            <app-icon name="close" />
          </button>
        </div>

        <section class="sheet-step" aria-labelledby="resume-step-prompt">
          <h3 id="resume-step-prompt" class="caption">1 · give your agent the prompt</h3>
          <p>
            Start Claude Code, or another coding agent, in the folder that holds your repositories
            and paste the prompt in. It will ask you for what git can’t show, like your employers.
          </p>
          <app-agent-prompt [prompt]="prompt" />
        </section>

        <section class="sheet-step" aria-labelledby="resume-step-file">
          <h3 id="resume-step-file" class="caption">2 · upload the file it writes</h3>
          <app-agent-upload
            [fileName]="fileName"
            example='{ "format": "offtheloop-resume/1", "resume": { … } }'
            [summary]="summary()"
            (read)="take($event)"
          />
          @if (fileError(); as error) {
            <p class="alert" role="alert">{{ error }}</p>
          }
        </section>

        @if (rows().length) {
          <section class="sheet-step" aria-labelledby="resume-step-pick">
            <h3 id="resume-step-pick" class="caption">3 · tick what to add</h3>
            <app-merge-list
              [rows]="rows()"
              [kept]="kept()"
              (toggle)="kept.set(toggled(kept(), $event))"
            />
          </section>
        } @else if (nothingNew()) {
          <p class="m-0 text-[14.5px] text-[var(--color-muted)]">
            Everything in it is on your resume already.
          </p>
        }

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
      </div>

      <div class="sheet-foot">
        <button
          type="button"
          class="btn btn-primary"
          [disabled]="!kept().size || busy()"
          (click)="save()"
        >
          @if (busy()) {
            <span class="spinner" aria-hidden="true"></span>
          }
          Add {{ kept().size || '' }} to your resume
        </button>
        <button type="button" class="btn" (click)="close()">Cancel</button>
        @if (rows().length) {
          <span class="flex-1"></span>
          <span class="mono text-[11.5px] text-[var(--color-dim)]"
            >{{ kept().size }} of {{ rows().length }} ticked</span
          >
        }
      </div>
    </dialog>
  `,
})
export class ResumeImportSheet {
  private readonly store = inject(LoopStore);
  private readonly sheet = viewChild.required<ElementRef<HTMLDialogElement>>('sheet');

  readonly closed = output<void>();

  protected readonly prompt = resumePrompt();
  protected readonly fileName = RESUME_FILE;
  protected readonly toggled = toggled;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private readonly read = signal<ResumeRead | null>(null);
  protected readonly kept = signal<ReadonlySet<string>>(new Set());

  protected readonly fileError = computed(() => {
    const read = this.read();
    return read && 'error' in read ? read.error : '';
  });

  protected readonly rows = computed(() => {
    const read = this.read();
    return read && 'resume' in read
      ? mergeRows(this.store.generalResume(), read.resume, read.rejected)
      : [];
  });

  protected readonly nothingNew = computed(() => {
    const read = this.read();
    return !!read && 'resume' in read && !this.rows().length;
  });

  protected readonly summary = computed(() => {
    const read = this.read();
    if (!read || !('resume' in read)) {
      return '';
    }
    const { experience, projects } = read.resume;
    return [
      `${experience.length} ${experience.length === 1 ? 'job' : 'jobs'}`,
      `${projects.length} ${projects.length === 1 ? 'project' : 'projects'}`,
    ].join(' · ');
  });

  constructor() {
    afterNextRender(() => this.sheet().nativeElement.showModal());
  }

  protected take(file: AgentFile): void {
    this.read.set(
      !file ? null : 'error' in file ? { error: file.error } : readResumeFile(file.text),
    );
    this.error.set('');
    this.kept.set(tickedKeys(this.rows()));
  }

  protected close(): void {
    this.sheet().nativeElement.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.sheet().nativeElement) {
      this.close();
    }
  }

  protected async save(): Promise<void> {
    const read = this.read();
    if (!read || !('resume' in read) || !this.kept().size || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveResume(applyMerge(this.store.generalResume(), read.resume, this.kept()));
      this.close();
    } catch (err) {
      this.error.set(messageOf(err, 'Your resume could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}
