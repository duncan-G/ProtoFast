import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { today } from '../../loop/days';
import { messageOf } from '../../loop/errors';
import { roundNow } from '../../loop/learnings';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';

let nextId = 0;

/** One line on what was learned, filed under a round of the process. */
@Component({
  selector: 'app-learning-add',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-2' },
  template: `
    <form class="entry-box" (ngSubmit)="add()">
      <label class="sr-only" [for]="id">What you learned</label>
      <input
        name="text"
        autocomplete="off"
        placeholder="What did you learn?"
        [id]="id"
        [(ngModel)]="text"
      />
      <label class="sr-only" [for]="id + '-round'">From which round</label>
      <select name="round" [id]="id + '-round'" [(ngModel)]="round">
        @for (label of rounds(); track label) {
          <option [value]="label">{{ label }}</option>
        }
        <option value="">Not from a round</option>
      </select>
      <button type="submit" class="btn btn-xs" [disabled]="busy() || !text.trim()">Add</button>
    </form>
    @if (error()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }
  `,
})
export class LearningAdd {
  private readonly store = inject(LoopStore);

  readonly application = input.required<Application>();

  protected readonly id = `learning-add-${nextId++}`;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly rounds = computed(() => [
    ...new Set(this.application().rounds.map((round) => round.label)),
  ]);
  protected text = '';
  protected round = '';

  ngOnInit(): void {
    this.round = roundNow(this.application().rounds);
  }

  protected async add(): Promise<void> {
    const text = this.text.trim();
    if (!text || this.busy()) {
      return;
    }
    const app = this.application();
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveApplication({
        ...app,
        learnings: [...app.learnings, { text, round: this.round, on: today() }],
      });
      this.text = '';
    } catch (err) {
      this.error.set(messageOf(err, 'That could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }
}
