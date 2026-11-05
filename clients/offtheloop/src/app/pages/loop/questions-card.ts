import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  Injector,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SideSheet } from '../../editors/side-sheet';
import { messageOf } from '../../loop/errors';
import { LoopStore } from '../../loop/loop-store';
import { Application } from '../../loop/model/application';
import { Icon } from '../../shared/icon';

type Show = 'ask' | 'asked' | 'all';

interface Row {
  key: string;
  asked: boolean;
  /** Its place in questions, or in asked once asked. */
  at: number;
  text: string;
  answer: string;
}

/** Questions to ask, ticked off once asked, with what they said kept beside them. */
@Component({
  selector: 'app-questions-card',
  imports: [FormsModule, Icon, SideSheet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card card-pad', role: 'region', 'aria-label': 'Questions' },
  template: `
    <div class="card-head">
      <h3 class="card-title">
        Questions
        @if (total()) {
          <span class="badge">{{ total() }}</span>
        }
      </h3>
      @if (rows('asked').length) {
        <div class="seg" role="group" aria-label="Show">
          @for (tab of cardTabs(); track tab.id) {
            <button
              type="button"
              [attr.aria-pressed]="tab.id === showing()"
              (click)="show.set(tab.id)"
            >
              {{ tab.label }}<span class="seg-count">{{ tab.count }}</span>
            </button>
          }
        </div>
      }
    </div>

    @if (shown().length) {
      <ul class="m-0 list-none p-0">
        @for (row of shown().slice(0, limit); track row.key) {
          <li class="check-row" [attr.data-asked]="row.asked">
            <button
              type="button"
              role="checkbox"
              class="check"
              [attr.aria-checked]="row.asked"
              [attr.aria-label]="(row.asked ? 'Asked: ' : 'Mark asked: ') + row.text"
              [disabled]="busy()"
              (click)="toggle(row)"
            >
              <app-icon name="check" [size]="13" [weight]="2.4" />
            </button>
            <span class="check-text">
              <span>{{ row.text }}</span>
              @if (row.answer) {
                <span class="check-answer">{{ row.answer }}</span>
              }
            </span>
          </li>
        }
      </ul>
    } @else {
      <p class="quiet">
        {{
          showing() === 'asked'
            ? 'None asked yet.'
            : 'Nothing to ask yet. Add what you want to know.'
        }}
      </p>
    }

    @if (shown().length > limit || rows('asked').length) {
      <div class="card-actions">
        <button type="button" class="btn btn-xs" (click)="openSheet()">
          {{ shown().length > limit ? 'Show all ' + shown().length : 'Answers and edits' }}
          <app-icon name="next" [size]="13" />
        </button>
      </div>
    }

    <form class="entry-box" (ngSubmit)="add()">
      <label class="sr-only" for="question-add">New question</label>
      <input
        id="question-add"
        name="fresh"
        autocomplete="off"
        placeholder="Add a question, e.g. Who owns on-call?"
        [(ngModel)]="fresh"
      />
      <button type="submit" class="btn btn-xs" [disabled]="busy() || !fresh.trim()">Add</button>
    </form>
    @if (error() && !sheet()) {
      <p class="alert" role="alert">{{ error() }}</p>
    }

    @if (sheet()) {
      <app-side-sheet
        heading="Questions"
        [count]="total()"
        [subtitle]="application().role + ' · ' + application().company"
        (closed)="closeSheet()"
      >
        <div sheet-tools class="flex flex-col gap-3">
          <label class="search-box">
            <app-icon name="search" [size]="15" />
            <span class="sr-only">Find a question</span>
            <input
              type="search"
              name="query"
              autocomplete="off"
              autofocus
              placeholder="Find a question"
              [(ngModel)]="query"
            />
          </label>
          <div class="seg self-start" role="group" aria-label="Show">
            @for (tab of sheetTabs(); track tab.id) {
              <button
                type="button"
                [attr.aria-pressed]="tab.id === sheetShow()"
                (click)="sheetShow.set(tab.id)"
              >
                {{ tab.label }}<span class="seg-count">{{ tab.count }}</span>
              </button>
            }
          </div>
        </div>

        <ul class="m-0 list-none p-0">
          @for (row of found(); track row.key) {
            <li class="check-row" [attr.data-asked]="row.asked">
              <button
                type="button"
                role="checkbox"
                class="check"
                [attr.aria-checked]="row.asked"
                [attr.aria-label]="(row.asked ? 'Asked: ' : 'Mark asked: ') + row.text"
                [disabled]="busy()"
                (click)="toggle(row)"
              >
                <app-icon name="check" [size]="13" [weight]="2.4" />
              </button>
              <div class="check-text">
                @if (editing() === row.key + ':text') {
                  <form
                    class="item"
                    (ngSubmit)="saveText(row)"
                    (keydown.escape)="stopEditing($event)"
                  >
                    <label class="sr-only" for="question-edit">Edit the question</label>
                    <input
                      id="question-edit"
                      class="item-input"
                      name="draft"
                      autocomplete="off"
                      [(ngModel)]="draft"
                    />
                  </form>
                } @else {
                  <button
                    type="button"
                    class="item-text p-0"
                    [attr.aria-label]="'Edit ' + row.text"
                    (click)="edit(row, 'text')"
                  >
                    {{ row.text }}
                  </button>
                }
                @if (row.asked) {
                  @if (editing() === row.key + ':answer') {
                    <form
                      class="item"
                      (ngSubmit)="saveAnswer(row)"
                      (keydown.escape)="stopEditing($event)"
                    >
                      <label class="sr-only" for="question-answer">What they said</label>
                      <input
                        id="question-answer"
                        class="item-input text-[13.5px]"
                        name="draft"
                        autocomplete="off"
                        placeholder="What they said"
                        [(ngModel)]="draft"
                      />
                    </form>
                  } @else if (row.answer) {
                    <button
                      type="button"
                      class="item-text check-answer p-0"
                      [attr.aria-label]="'Edit the answer to ' + row.text"
                      (click)="edit(row, 'answer')"
                    >
                      {{ row.answer }}
                    </button>
                  } @else {
                    <button
                      type="button"
                      class="btn-add mt-1 min-h-[30px] self-start"
                      (click)="edit(row, 'answer')"
                    >
                      <app-icon name="plus" [size]="10" [weight]="2" />Note the answer
                    </button>
                  }
                }
              </div>
              <button
                type="button"
                class="btn btn-icon btn-xs h-8 min-h-8 w-8 flex-none"
                [attr.aria-label]="'Remove ' + row.text"
                [disabled]="busy()"
                (click)="remove(row)"
              >
                <app-icon name="close" [size]="12" />
              </button>
            </li>
          } @empty {
            <li class="quiet py-6 text-center">No question matches.</li>
          }
        </ul>
        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }

        <form sheet-foot class="side-sheet-foot" (ngSubmit)="add()">
          <div class="entry-box">
            <label class="sr-only" for="question-add-sheet">New question</label>
            <input
              id="question-add-sheet"
              name="fresh"
              autocomplete="off"
              placeholder="Add a question"
              [(ngModel)]="fresh"
            />
            <button type="submit" class="btn btn-xs" [disabled]="busy() || !fresh.trim()">
              Add
            </button>
          </div>
        </form>
      </app-side-sheet>
    }
  `,
})
export class QuestionsCard {
  private readonly store = inject(LoopStore);
  private readonly injector = inject(Injector);

  readonly application = input.required<Application>();

  protected readonly limit = 5;
  protected readonly show = signal<Exclude<Show, 'all'>>('ask');
  protected readonly sheet = signal(false);
  protected readonly sheetShow = signal<Show>('ask');
  protected readonly query = signal('');
  protected readonly editing = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected fresh = '';
  protected draft = '';

  private readonly all = computed((): Row[] => {
    const app = this.application();
    return [
      ...app.questions.map((text, at) => ({ key: `q${at}`, asked: false, at, text, answer: '' })),
      ...app.asked.map((asked, at) => ({
        key: `a${at}`,
        asked: true,
        at,
        text: asked.question,
        answer: asked.answer,
      })),
    ];
  });

  protected readonly total = computed(() => this.all().length);
  /** Asked ones show only while there are any. */
  protected readonly showing = computed(() => (this.rows('asked').length ? this.show() : 'ask'));
  protected readonly shown = computed(() => this.rows(this.showing()));

  protected readonly found = computed(() => {
    const query = this.query().trim().toLowerCase();
    return this.rows(this.sheetShow()).filter(
      (row) => !query || `${row.text} ${row.answer}`.toLowerCase().includes(query),
    );
  });

  protected readonly cardTabs = computed(() => [
    { id: 'ask' as const, label: 'To ask', count: this.rows('ask').length },
    { id: 'asked' as const, label: 'Asked', count: this.rows('asked').length },
  ]);

  protected readonly sheetTabs = computed(() => [
    ...this.cardTabs(),
    { id: 'all' as const, label: 'All', count: this.total() },
  ]);

  protected rows(show: Show): Row[] {
    return this.all().filter((row) => show === 'all' || row.asked === (show === 'asked'));
  }

  protected openSheet(): void {
    this.query.set('');
    this.sheetShow.set(this.showing());
    this.error.set('');
    this.sheet.set(true);
  }

  protected closeSheet(): void {
    this.sheet.set(false);
    this.editing.set(null);
  }

  protected edit(row: Row, part: 'text' | 'answer'): void {
    this.draft = part === 'text' ? row.text : row.answer;
    this.editing.set(`${row.key}:${part}`);
    const id = part === 'text' ? 'question-edit' : 'question-answer';
    afterNextRender(() => document.getElementById(id)?.focus(), { injector: this.injector });
  }

  /** Escape closes the line being edited, not the sheet around it. */
  protected stopEditing(event: Event): void {
    event.stopPropagation();
    event.preventDefault();
    this.editing.set(null);
  }

  protected async toggle(row: Row): Promise<void> {
    const app = this.application();
    const questions = [...app.questions];
    const asked = [...app.asked];
    if (row.asked) {
      asked.splice(row.at, 1);
      questions.push(row.text);
    } else {
      questions.splice(row.at, 1);
      asked.push({ question: row.text, answer: '' });
    }
    await this.write({ questions, asked });
  }

  /** Clearing a question removes it. */
  protected async saveText(row: Row): Promise<void> {
    const text = this.draft.trim();
    const app = this.application();
    const questions = [...app.questions];
    const asked = [...app.asked];
    if (row.asked) {
      if (text) {
        asked[row.at] = { ...asked[row.at], question: text };
      } else {
        asked.splice(row.at, 1);
      }
    } else if (text) {
      questions[row.at] = text;
    } else {
      questions.splice(row.at, 1);
    }
    if (await this.write({ questions, asked })) {
      this.editing.set(null);
    }
  }

  protected async saveAnswer(row: Row): Promise<void> {
    const asked = [...this.application().asked];
    asked[row.at] = { ...asked[row.at], answer: this.draft.trim() };
    if (await this.write({ asked })) {
      this.editing.set(null);
    }
  }

  protected remove(row: Row): Promise<boolean> {
    const app = this.application();
    return row.asked
      ? this.write({ asked: app.asked.filter((_, at) => at !== row.at) })
      : this.write({ questions: app.questions.filter((_, at) => at !== row.at) });
  }

  protected async add(): Promise<void> {
    const text = this.fresh.trim();
    if (text && (await this.write({ questions: [...this.application().questions, text] }))) {
      this.fresh = '';
      this.show.set('ask');
    }
  }

  private async write(changes: Partial<Application>): Promise<boolean> {
    if (this.busy()) {
      return false;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.store.saveApplication({ ...this.application(), ...changes });
      return true;
    } catch (err) {
      this.error.set(messageOf(err, 'That could not be saved.'));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
