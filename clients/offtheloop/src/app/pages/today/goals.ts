import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { messageOf } from '../../loop/errors';
import { LoopStore } from '../../loop/loop-store';
import { Goal } from '../../loop/model/goal';
import { Icon } from '../../shared/icon';

const KINDS = ['salary', 'title', 'level', 'equity', 'remote days'];

/** The career goals, where each stands now against where it's headed. */
@Component({
  selector: 'app-goals',
  imports: [FormsModule, Icon, NgTemplateOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section aria-labelledby="goals-h" class="flex flex-col">
      <div class="mb-3 flex items-center justify-between gap-3">
        <h2 id="goals-h" class="label m-0">goals</h2>
        @if (!editing() && store.goals().length) {
          <button type="button" class="btn-link" (click)="start()">+ goal</button>
        }
      </div>
      <div class="flex flex-wrap items-start gap-x-16 gap-y-8 max-[719px]:gap-x-10">
        @for (goal of store.goals(); track goal.id) {
          @if (editing()?.id === goal.id) {
            <ng-container *ngTemplateOutlet="form" />
          } @else {
            <button
              type="button"
              class="group flex min-w-0 max-w-full cursor-pointer flex-col items-start gap-1 border-0 bg-transparent p-0 text-left"
              [attr.aria-label]="describe(goal)"
              (click)="start(goal)"
            >
              <span class="label group-hover:text-[var(--color-muted)]">{{ goal.kind }}</span>
              <span
                class="text-[clamp(32px,3.6vw,48px)] leading-[1.05] font-semibold tracking-[-0.04em] break-words tabular-nums"
              >
                @if (goal.current) {
                  {{ goal.current }}<span class="text-[var(--color-faint)]"> → </span>
                }
                <span class="text-[var(--color-accent)]">{{ goal.target }}</span>
              </span>
            </button>
          }
        } @empty {
          @if (store.loaded() && !editing()) {
            <p class="m-0 text-[var(--color-dim)]">
              What are you after?
              <button
                type="button"
                class="btn-link text-[15px] text-[var(--color-accent)]"
                (click)="start()"
              >
                Set a goal
              </button>
            </p>
          }
        }
        @if (editing() && !editing()!.id) {
          <ng-container *ngTemplateOutlet="form" />
        }
      </div>

      <ng-template #form>
        <form
          class="flex basis-full flex-wrap items-end gap-x-6 gap-y-4"
          (ngSubmit)="save()"
          (keydown.escape)="cancel()"
        >
          <label class="field flex-[1_1_160px]">
            <span>type</span>
            <input
              class="input"
              name="kind"
              list="goal-kinds"
              placeholder="salary"
              autocomplete="off"
              required
              [(ngModel)]="draft.kind"
            />
          </label>
          <label class="field flex-[1_1_120px]">
            <span>now</span>
            <input
              class="input"
              name="current"
              placeholder="150k"
              autocomplete="off"
              [(ngModel)]="draft.current"
            />
          </label>
          <label class="field flex-[1_1_120px]">
            <span>goal</span>
            <input
              class="input"
              name="target"
              placeholder="200k"
              autocomplete="off"
              required
              [(ngModel)]="draft.target"
            />
          </label>
          <div class="flex items-center gap-2.5">
            <button
              type="submit"
              class="btn btn-primary"
              [disabled]="!draft.kind.trim() || !draft.target.trim() || busy()"
            >
              Save
            </button>
            <button type="button" class="btn" (click)="cancel()">Cancel</button>
            @if (draft.id) {
              <button
                type="button"
                class="btn btn-icon btn-danger border-0"
                aria-label="Delete goal"
                [disabled]="busy()"
                (click)="remove()"
              >
                <app-icon name="trash" [size]="15" />
              </button>
            }
          </div>
          @if (error()) {
            <p class="alert basis-full" role="alert">{{ error() }}</p>
          }
        </form>
      </ng-template>
      <datalist id="goal-kinds">
        @for (kind of kinds; track kind) {
          <option [value]="kind"></option>
        }
      </datalist>
    </section>
  `,
})
export class Goals {
  protected readonly store = inject(LoopStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  protected readonly kinds = KINDS;

  protected readonly editing = signal<Goal | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected draft: Goal = { id: '', kind: '', target: '', current: '' };

  protected describe(goal: Goal): string {
    const now = goal.current ? `${goal.current} now, ` : '';
    return `${goal.kind}: ${now}aiming for ${goal.target}. Edit`;
  }

  protected start(goal?: Goal): void {
    const blank = { id: '', kind: '', target: '', current: '' };
    this.draft = { ...(goal ?? blank) };
    this.error.set('');
    this.editing.set(goal ?? blank);
    const field = goal ? 'current' : 'kind';
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLInputElement>(`[name="${field}"]`)?.focus(),
      { injector: this.injector },
    );
  }

  protected cancel(): void {
    this.editing.set(null);
  }

  protected async save(): Promise<void> {
    if (!this.draft.kind.trim() || !this.draft.target.trim() || this.busy()) {
      return;
    }
    await this.run('That goal could not be saved.', () => this.store.saveGoal(this.draft));
  }

  protected async remove(): Promise<void> {
    await this.run('That goal could not be deleted.', () => this.store.deleteGoal(this.draft.id));
  }

  private async run(failed: string, write: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await write();
      this.editing.set(null);
    } catch (err) {
      this.error.set(messageOf(err, failed));
    } finally {
      this.busy.set(false);
    }
  }
}
