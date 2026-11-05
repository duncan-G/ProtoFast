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
import { messageOf } from '../loop/errors';
import { Icon } from '../shared/icon';

export type ListMarker = '·' | '?' | '!';

/** A list on a record, as its page lays it out. */
export interface ListSpec<T> {
  key: { [K in keyof T]: T[K] extends string[] ? K : never }[keyof T];
  label: string;
  placeholder: string;
  look?: 'rows' | 'tags';
  /** What each row starts with: a dot, a question to ask, or a warning. */
  marker?: ListMarker;
}

/** A list edited where it is shown: click an entry to change it, clear it to remove it. */
@Component({
  selector: 'app-list-field',
  imports: [FormsModule, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex min-w-0 flex-col', role: 'group', '[attr.aria-label]': 'label()' },
  template: `
    @if (heading()) {
      <h3 class="caption mb-1.5">
        {{ label() }}
        @if (items().length) {
          <span class="count">{{ items().length }}</span>
        }
      </h3>
    }
    @if (look() === 'tags') {
      <div class="tags">
        @for (row of rows(); track row.at) {
          @let item = row.item;
          @let i = row.at;
          @if (editing() === i) {
            <form class="tag tag-editing" (ngSubmit)="commit()" (keydown.escape)="stop($event)">
              <label class="sr-only" [for]="inputId">Edit {{ item }}</label>
              <input
                class="tag-input"
                name="entry"
                autocomplete="off"
                [id]="inputId"
                [(ngModel)]="draft"
              />
            </form>
          } @else {
            <span class="tag">
              <button
                type="button"
                class="tag-text"
                [attr.aria-label]="'Edit ' + item"
                (click)="edit(i)"
              >
                {{ item }}
              </button>
              <button
                type="button"
                class="tag-remove"
                [attr.aria-label]="'Remove ' + item"
                [disabled]="busy()"
                (click)="remove(i)"
              >
                <app-icon name="close" [size]="11" />
              </button>
            </span>
          }
        }
        <form class="tag tag-add" (ngSubmit)="add()" (keydown.escape)="fresh = ''">
          <app-icon name="plus" [size]="10" [weight]="1.8" />
          <label class="sr-only" [for]="inputId + '-add'">Add to {{ label() }}</label>
          <input
            class="tag-input"
            name="fresh"
            autocomplete="off"
            placeholder="add"
            [attr.autofocus]="focusAdd() ? '' : null"
            [id]="inputId + '-add'"
            [(ngModel)]="fresh"
          />
        </form>
      </div>
    } @else {
      <div class="rows" [class.rows-split]="items().length > splitAfter">
        @for (row of rows(); track row.at) {
          @let item = row.item;
          @let i = row.at;
          @if (editing() === i) {
            <form class="item" (ngSubmit)="commit()" (keydown.escape)="stop($event)">
              <span class="list-marker" [attr.data-marker]="marker()" aria-hidden="true">{{
                marker() === '·' ? '' : marker()
              }}</span>
              <label class="sr-only" [for]="inputId">Edit {{ item }}</label>
              <input
                class="item-input"
                name="entry"
                autocomplete="off"
                [id]="inputId"
                [(ngModel)]="draft"
              />
              <span class="item-keys" aria-hidden="true">↵ save · esc</span>
            </form>
          } @else {
            <div class="item">
              <span class="list-marker" [attr.data-marker]="marker()" aria-hidden="true">{{
                marker() === '·' ? '' : marker()
              }}</span>
              <button
                type="button"
                class="item-text"
                [attr.aria-label]="'Edit ' + item"
                (click)="edit(i)"
              >
                {{ item }}
              </button>
              <button
                type="button"
                class="item-remove"
                [attr.aria-label]="'Remove ' + item"
                [disabled]="busy()"
                (click)="remove(i)"
              >
                <app-icon name="close" [size]="13" />
              </button>
            </div>
          }
        }
      </div>
      <form class="item item-add" (ngSubmit)="add()" (keydown.escape)="fresh = ''">
        <app-icon name="plus" [size]="11" [weight]="1.8" />
        <label class="sr-only" [for]="inputId + '-add'">Add to {{ label() }}</label>
        <input
          class="item-input"
          name="fresh"
          autocomplete="off"
          [placeholder]="placeholder() ? 'add, e.g. ' + placeholder() : 'add, then ↵'"
          [attr.autofocus]="focusAdd() ? '' : null"
          [id]="inputId + '-add'"
          [(ngModel)]="fresh"
        />
      </form>
    }
    @if (error()) {
      <p class="alert mt-2" role="alert">{{ error() }}</p>
    }
  `,
})
export class ListField {
  private static next = 0;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly label = input.required<string>();
  readonly items = input.required<readonly string[]>();
  readonly placeholder = input('');
  readonly look = input<'rows' | 'tags'>('rows');
  readonly marker = input<ListMarker>('·');
  readonly save = input.required<(items: string[]) => Promise<unknown>>();
  readonly heading = input(true);
  /** Shows only the entries holding it, case aside. */
  readonly query = input('');
  /** For a sheet opened to add: the add row takes the focus. */
  readonly focusAdd = input(false);

  /** Longer lists flow into columns where there's room for two. */
  protected readonly splitAfter = 5;
  protected readonly inputId = `list-entry-${ListField.next++}`;
  protected readonly rows = computed(() => {
    const query = this.query().trim().toLowerCase();
    return this.items()
      .map((item, at) => ({ item, at }))
      .filter((row) => !query || row.item.toLowerCase().includes(query));
  });
  protected readonly editing = signal<number | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected draft = '';
  protected fresh = '';

  protected edit(at: number): void {
    this.draft = this.items()[at] ?? '';
    this.error.set('');
    this.editing.set(at);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLElement>(`#${this.inputId}`)?.focus(),
      { injector: this.injector },
    );
  }

  /** In a sheet, Escape closes the entry being edited, not the sheet. */
  protected stop(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    this.editing.set(null);
    this.error.set('');
  }

  protected async commit(): Promise<void> {
    const at = this.editing();
    if (at === null) {
      return;
    }
    const items = [...this.items()];
    const text = this.draft.trim();
    if (text) {
      items[at] = text;
    } else {
      items.splice(at, 1);
    }
    if (await this.write(items)) {
      this.editing.set(null);
    }
  }

  /** The add row stays focused for the next one. */
  protected async add(): Promise<void> {
    const text = this.fresh.trim();
    if (text && (await this.write([...this.items(), text]))) {
      this.fresh = '';
    }
  }

  protected async remove(at: number): Promise<void> {
    const items = [...this.items()];
    items.splice(at, 1);
    await this.write(items);
  }

  private async write(items: string[]): Promise<boolean> {
    if (this.busy()) {
      return false;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.save()(items);
      return true;
    } catch (err) {
      this.error.set(messageOf(err, 'That could not be saved.'));
      return false;
    } finally {
      this.busy.set(false);
    }
  }
}
