import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  model,
  output,
  signal,
} from '@angular/core';
import { messageOf } from '../../loop/errors';
import { changesFrom, Review } from '../../loop/findings';
import { LoopStore } from '../../loop/loop-store';
import { LoopRun } from '../../loop/model/loop-run';
import { FindingsList } from '../loop/findings-list';

/** A company's research findings, ticked to keep; nothing saves before. */
@Component({
  selector: 'app-research-review',
  imports: [FindingsList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-h-0 flex-1 flex-col' },
  styles: `
    .edit-body {
      flex: 1;
      min-height: 0;
      overflow-y: auto;
    }
  `,
  template: `
    <div class="edit-body">
      <app-findings-list [(review)]="review" />
      @if (error()) {
        <p class="alert" role="alert">{{ error() }}</p>
      }
    </div>
    <div class="edit-foot">
      @if (review().findings.length) {
        <span class="edit-note">{{ kept() }} of {{ review().findings.length }} ticked</span>
        <button type="button" class="btn btn-sm" [disabled]="busy()" (click)="done.emit()">
          Discard
        </button>
        <button
          type="button"
          class="btn btn-sm btn-primary"
          [disabled]="!kept() || busy()"
          (click)="save()"
        >
          @if (busy()) {
            <span class="spinner" aria-hidden="true"></span>
          }
          Save to company
        </button>
      } @else {
        <button type="button" class="btn btn-sm" (click)="done.emit()">Close</button>
      }
    </div>
  `,
})
export class ResearchReview {
  private readonly store = inject(LoopStore);

  readonly run = input.required<LoopRun>();
  readonly review = model.required<Review>();
  /** Saved or discarded: the run has nothing left to show. */
  readonly done = output<void>();

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly kept = computed(() => this.review().findings.filter((f) => f.keep).length);

  protected async save(): Promise<void> {
    const company = this.store.companiesById().get(this.run().companyId ?? '');
    if (!company) {
      this.error.set('The company is gone.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveCompany({
        ...company,
        ...changesFrom(company, this.review().findings, 'company'),
      });
      this.done.emit();
    } catch (err) {
      this.error.set(messageOf(err, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}
