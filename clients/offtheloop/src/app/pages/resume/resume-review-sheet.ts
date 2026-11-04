import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { messageOf } from '../../loop/errors';
import { LoopRuns } from '../../loop/loop-runs';
import { LoopStore } from '../../loop/loop-store';
import { Resume } from '../../loop/model/resume';
import { applyMerge, mergeRows } from '../../loop/resume-merge';
import { Icon } from '../../shared/icon';
import { MergeList, tickedKeys, toggled } from './merge-list';

/** What the AI read in a resume file, ticked into the general resume; the run goes either way. */
@Component({
  selector: 'app-resume-review',
  imports: [Icon, MergeList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #sheet
      class="sheet"
      aria-labelledby="resume-review-title"
      (close)="closed.emit()"
      (click)="closeOnBackdrop($event)"
    >
      <div class="sheet-body">
        <div class="flex items-start justify-between gap-4">
          <div class="flex flex-col gap-2">
            <h2 id="resume-review-title" class="m-0 text-[24px] font-semibold tracking-[-0.04em]">
              {{ label() }}
            </h2>
            <p class="m-0 text-[14.5px] text-[var(--color-muted)]">
              What the AI read that isn’t on your resume yet. What’s filled in already starts
              unticked.
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

        @if (rows().length) {
          <app-merge-list
            [rows]="rows()"
            [kept]="kept()"
            (toggle)="kept.set(toggled(kept(), $event))"
          />
        } @else {
          <p class="m-0 text-[15px] text-[var(--color-text-2)]">
            Everything it read is on your resume already.
          </p>
        }

        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
      </div>

      <div class="sheet-foot">
        @if (rows().length) {
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
        }
        <button type="button" class="btn" [disabled]="busy()" (click)="discard()">
          {{ rows().length ? 'Discard' : 'Done' }}
        </button>
        <button type="button" class="btn-link" (click)="close()">later</button>
      </div>
    </dialog>
  `,
})
export class ResumeReviewSheet {
  private readonly store = inject(LoopStore);
  private readonly runs = inject(LoopRuns);
  private readonly sheet = viewChild.required<ElementRef<HTMLDialogElement>>('sheet');

  readonly runId = input.required<string>();
  readonly label = input.required<string>();
  readonly incoming = input.required<Resume>();
  readonly closed = output<void>();

  protected readonly toggled = toggled;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly rows = computed(() => mergeRows(this.store.generalResume(), this.incoming()));
  protected readonly kept = signal<ReadonlySet<string>>(new Set());

  constructor() {
    afterNextRender(() => {
      this.kept.set(tickedKeys(this.rows()));
      this.sheet().nativeElement.showModal();
    });
  }

  protected async save(): Promise<void> {
    if (!this.kept().size || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveResume(
        applyMerge(this.store.generalResume(), this.incoming(), this.kept()),
      );
      await this.runs.dismiss([this.runId()]).catch(() => undefined);
      this.close();
    } catch (err) {
      this.error.set(messageOf(err, 'Your resume could not be saved.'));
      this.busy.set(false);
    }
  }

  protected async discard(): Promise<void> {
    await this.runs.dismiss([this.runId()]).catch(() => undefined);
    this.close();
  }

  protected close(): void {
    this.sheet().nativeElement.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.sheet().nativeElement) {
      this.close();
    }
  }
}
