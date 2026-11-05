import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { messageOf } from '../../loop/errors';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { RoundState } from '../../loop/model/round-state';
import { Icon } from '../../shared/icon';

const SHOWN = 5;

const NEXT_STATE: Record<RoundState, RoundState> = {
  upcoming: 'current',
  current: 'done',
  done: 'upcoming',
};

interface DraftRound {
  label: string;
  state: RoundState;
}

/** An application's process, round by round, edited in a panel in place. */
@Component({
  selector: 'app-rounds',
  imports: [FormsModule, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col gap-2.5', role: 'group', 'aria-label': 'Interview rounds' },
  template: `
    @if (editing()) {
      <form
        class="edit-form edit-panel"
        aria-labelledby="rounds-heading"
        (ngSubmit)="save()"
        (keydown)="onKeydown($event)"
      >
        <div class="edit-head">
          <h3 id="rounds-heading" class="edit-heading">editing · process</h3>
          <span class="keys wide-only">dot: upcoming → current → done</span>
        </div>
        <div class="edit-body rounds-edit">
          @for (round of rounds(); track round; let i = $index) {
            <div class="round-row" data-round-row [class.dragging]="dragging() === i">
              <span
                class="round-grip"
                aria-hidden="true"
                title="Drag to reorder, or Alt+↑ / Alt+↓"
                (pointerdown)="grab($event, i)"
              >
                <svg width="10" height="14" viewBox="0 0 10 14" fill="currentColor">
                  <circle cx="2.5" cy="2.5" r="1.3" />
                  <circle cx="7.5" cy="2.5" r="1.3" />
                  <circle cx="2.5" cy="7" r="1.3" />
                  <circle cx="7.5" cy="7" r="1.3" />
                  <circle cx="2.5" cy="11.5" r="1.3" />
                  <circle cx="7.5" cy="11.5" r="1.3" />
                </svg>
              </span>
              <button
                type="button"
                class="round-state"
                [attr.data-state]="round.state"
                [attr.aria-label]="stateLabel(round, i)"
                (click)="cycle(i)"
              >
                <span class="round-dot"></span>{{ round.state }}
              </button>
              <label class="sr-only" [for]="'round-' + i">Round {{ i + 1 }}</label>
              <input
                class="round-input"
                autocomplete="off"
                placeholder="recruiter screen"
                [id]="'round-' + i"
                [attr.data-state]="round.state"
                [value]="round.label"
                (input)="rename(i, $any($event.target).value)"
                (keydown.alt.arrowup)="move(i, -1, $event)"
                (keydown.alt.arrowdown)="move(i, 1, $event)"
              />
              <button
                type="button"
                class="round-remove"
                [attr.aria-label]="'Remove ' + (round.label || 'round ' + (i + 1))"
                (click)="remove(i)"
              >
                <app-icon name="close" [size]="12" />
              </button>
            </div>
          }
          <div class="round-row round-add">
            <span class="round-grip" aria-hidden="true"><app-icon name="plus" [size]="12" /></span>
            <label class="sr-only" for="round-new">Add a round</label>
            <input
              id="round-new"
              class="round-input"
              autocomplete="off"
              [placeholder]="rounds().length ? 'add a round, then ↵' : 'first round, then ↵'"
              [value]="fresh"
              (input)="fresh = $any($event.target).value"
              (keydown.enter)="add($event)"
            />
          </div>
          @if (error()) {
            <p class="alert mt-3" role="alert">{{ error() }}</p>
          }
        </div>
        <div class="edit-foot">
          <button type="button" class="btn btn-sm" (click)="editing.set(false)">Cancel</button>
          <button type="submit" class="btn btn-sm btn-primary" [disabled]="busy()">
            @if (busy()) {
              <span class="spinner" aria-hidden="true"></span>
            }
            Save
          </button>
        </div>
      </form>
    } @else {
      <div class="card-head">
        <h3 class="field-label text-[13px] font-medium">
          Interview rounds
          @if (application().rounds.length) {
            · {{ application().rounds.length }}
          }
        </h3>
        @if (application().rounds.length) {
          <button type="button" class="btn btn-xs" (click)="edit()">
            <app-icon name="edit" [size]="13" />Edit
          </button>
        }
      </div>
      @if (application().rounds.length) {
        <ol class="rounds">
          @for (round of visible(); track $index) {
            <li
              [attr.data-state]="round.state"
              [style.color]="round.state === 'current' ? hot() : null"
            >
              @if (round.state === 'done') {
                <svg class="round-mark" viewBox="0 0 16 16" aria-hidden="true">
                  <path d="m3 8.5 3.2 3L13 4.5" />
                </svg>
              } @else {
                <span class="round-mark" aria-hidden="true">
                  <span
                    class="round-dot"
                    [style.background]="round.state === 'current' ? hot() : null"
                  ></span>
                </span>
              }
              <span class="sr-only">{{ round.state }}:</span>
              {{ round.label }}
            </li>
          }
        </ol>
        @if (earlier()) {
          <button
            type="button"
            class="btn btn-xs self-start"
            [attr.aria-expanded]="showingAll()"
            (click)="showingAll.set(!showingAll())"
          >
            {{ showingAll() ? 'Show fewer' : 'Show ' + earlier() + ' earlier' }}
          </button>
        }
      } @else {
        <button type="button" class="add-tile" (click)="edit()">
          <span class="add-tile-mark"><app-icon name="plus" [size]="13" [weight]="2" /></span>
          <span class="add-tile-text"
            >Add a round<small>Recruiter call, tech screen, onsite</small></span
          >
        </button>
      }
    }
  `,
})
export class Rounds {
  private readonly store = inject(LoopStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly application = input.required<Application>();
  /** The colour of the round under way. */
  readonly hot = input.required<string>();

  protected readonly editing = signal(false);
  protected readonly showingAll = signal(false);
  /** Rounds done long ago fold away once there are more than a few. */
  protected readonly earlier = computed(() =>
    Math.max(0, this.application().rounds.length - SHOWN),
  );
  protected readonly visible = computed(() =>
    this.application().rounds.slice(this.showingAll() ? 0 : this.earlier()),
  );
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly rounds = signal<DraftRound[]>([]);
  protected readonly dragging = signal<number | null>(null);
  protected fresh = '';

  protected edit(): void {
    this.rounds.set(this.application().rounds.map((round) => ({ ...round })));
    this.fresh = '';
    this.error.set('');
    this.editing.set(true);
    this.focus('#round-new');
  }

  protected stateLabel(round: DraftRound, at: number): string {
    return `${round.label || `Round ${at + 1}`} is ${round.state}. Change`;
  }

  protected rename(at: number, label: string): void {
    this.rounds()[at].label = label;
  }

  /** In place, so the row and its focus stay. */
  protected cycle(at: number): void {
    const round = this.rounds()[at];
    round.state = NEXT_STATE[round.state];
    this.rounds.set([...this.rounds()]);
  }

  protected remove(at: number): void {
    this.rounds.update((list) => list.filter((_, i) => i !== at));
  }

  /** The first round is under way; later ones are still to come. */
  protected add(event: Event): void {
    event.preventDefault();
    const label = this.fresh.trim();
    if (!label) {
      return;
    }
    this.rounds.update((list) => [
      ...list,
      { label, state: list.length ? 'upcoming' : 'current' },
    ]);
    this.fresh = '';
    (event.target as HTMLInputElement).value = '';
  }

  protected move(at: number, by: number, event: Event): void {
    event.preventDefault();
    const to = at + by;
    if (to < 0 || to >= this.rounds().length) {
      return;
    }
    this.reorder(at, to);
    this.focus(`#round-${to}`);
  }

  /** Dragged by the grip: each row passed at its middle swaps places with it. */
  protected grab(event: PointerEvent, from: number): void {
    const grip = event.currentTarget as HTMLElement;
    grip.setPointerCapture(event.pointerId);
    event.preventDefault();
    let at = from;
    this.dragging.set(at);
    const moved = (e: PointerEvent) => {
      const rows = [...this.host.nativeElement.querySelectorAll<HTMLElement>('[data-round-row]')];
      const before = rows.findIndex((row) => {
        const box = row.getBoundingClientRect();
        return e.clientY < box.top + box.height / 2;
      });
      const to = before === -1 ? rows.length - 1 : before > at ? before - 1 : before;
      if (to !== at) {
        this.reorder(at, to);
        at = to;
        this.dragging.set(at);
      }
    };
    const dropped = () => {
      grip.removeEventListener('pointermove', moved);
      grip.removeEventListener('pointerup', dropped);
      grip.removeEventListener('pointercancel', dropped);
      this.dragging.set(null);
    };
    grip.addEventListener('pointermove', moved);
    grip.addEventListener('pointerup', dropped);
    grip.addEventListener('pointercancel', dropped);
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
      event.preventDefault();
      void this.save();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      this.editing.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (this.busy()) {
      return;
    }
    const rounds = this.rounds().filter((round) => round.label.trim());
    const fresh = this.fresh.trim();
    if (fresh) {
      rounds.push({ label: fresh, state: rounds.length ? 'upcoming' : 'current' });
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveApplication({ ...this.application(), rounds });
      this.editing.set(false);
    } catch (err) {
      this.error.set(messageOf(err, 'The process could not be saved.'));
    } finally {
      this.busy.set(false);
    }
  }

  private reorder(from: number, to: number): void {
    this.rounds.update((list) => {
      const next = [...list];
      const [round] = next.splice(from, 1);
      next.splice(to, 0, round);
      return next;
    });
  }

  private focus(selector: string): void {
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(),
      { injector: this.injector },
    );
  }
}
