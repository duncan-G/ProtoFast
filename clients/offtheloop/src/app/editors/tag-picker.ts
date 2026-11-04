import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  input,
  linkedSignal,
  model,
  signal,
} from '@angular/core';
import { tagKey } from '../loop/note-tags';
import { Icon } from '../shared/icon';

interface Choice {
  tag: string;
  adds: boolean;
}

let nextId = 0;

/** A tag picked from a list, or typed in when it isn't there. */
@Component({
  selector: 'app-tag-picker',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tag-picker' },
  template: `
    <input
      class="input"
      role="combobox"
      aria-autocomplete="list"
      autocomplete="off"
      [id]="inputId()"
      [placeholder]="placeholder()"
      [value]="text()"
      [attr.aria-expanded]="expanded()"
      [attr.aria-controls]="expanded() ? listId : null"
      [attr.aria-activedescendant]="expanded() && active() >= 0 ? optionId(active()) : null"
      (input)="type($any($event.target).value)"
      (click)="show()"
      (keydown)="key($event)"
      (blur)="commit()"
    />
    <button
      type="button"
      class="tag-picker-toggle"
      tabindex="-1"
      aria-label="Show tags"
      (mousedown)="$event.preventDefault()"
      (click)="toggle()"
    >
      <app-icon name="down" [size]="14" />
    </button>
    @if (expanded()) {
      <ul
        role="listbox"
        class="tag-picker-list"
        aria-label="Tags"
        [id]="listId"
        (mousedown)="$event.preventDefault()"
      >
        @for (choice of choices(); track choice.tag; let i = $index) {
          <li
            role="option"
            [id]="optionId(i)"
            [attr.aria-selected]="i === active()"
            [class.is-current]="!choice.adds && choice.tag === current()"
            (mousemove)="active.set(i)"
            (click)="pick(choice)"
          >
            @if (choice.adds) {
              <app-icon name="plus" [size]="12" [weight]="2" />add “{{ choice.tag }}”
            } @else {
              {{ choice.tag }}
            }
          </li>
        }
      </ul>
    }
  `,
})
export class TagPicker {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly value = model('');
  readonly tags = input<readonly string[]>([]);
  readonly inputId = input.required<string>();
  readonly placeholder = input('');

  protected readonly listId = `tag-picker-${nextId++}`;
  protected readonly text = linkedSignal(() => this.value());
  /** What was typed since the list opened; null shows every tag. */
  private readonly query = signal<string | null>(null);
  protected readonly open = signal(false);
  protected readonly active = signal(-1);
  protected readonly current = computed(() => tagKey(this.value()));

  protected readonly choices = computed<Choice[]>(() => {
    const typed = tagKey(this.query() ?? '');
    const choices = [
      ...this.tags().filter((tag) => tag.startsWith(typed)),
      ...this.tags().filter((tag) => !tag.startsWith(typed) && tag.includes(typed)),
    ].map((tag) => ({ tag, adds: false }));
    if (typed && !this.tags().includes(typed)) {
      choices.push({ tag: typed, adds: true });
    }
    return choices;
  });

  protected readonly expanded = computed(() => this.open() && this.choices().length > 0);

  constructor() {
    afterRenderEffect(() => {
      if (this.expanded() && this.active() >= 0) {
        this.host.nativeElement
          .querySelector(`#${this.optionId(this.active())}`)
          ?.scrollIntoView({ block: 'nearest' });
      }
    });
  }

  protected optionId(index: number): string {
    return `${this.listId}-${index}`;
  }

  protected show(): void {
    if (this.open()) {
      return;
    }
    this.query.set(null);
    this.open.set(true);
    this.active.set(this.choices().findIndex((c) => c.tag === this.current()));
  }

  protected toggle(): void {
    if (this.open()) {
      this.commit();
      return;
    }
    this.host.nativeElement.querySelector('input')?.focus();
    this.show();
  }

  protected type(text: string): void {
    this.text.set(text);
    this.query.set(text);
    this.open.set(true);
    this.active.set(this.choices().length ? 0 : -1);
  }

  protected key(event: KeyboardEvent): void {
    const count = this.choices().length;
    switch (event.key) {
      case 'ArrowDown':
      case 'ArrowUp':
        event.preventDefault();
        if (!this.open()) {
          this.show();
        } else if (count) {
          const by = event.key === 'ArrowDown' ? 1 : -1;
          const at = this.active();
          this.active.set(at < 0 ? (by > 0 ? 0 : count - 1) : (at + by + count) % count);
        }
        break;
      case 'Enter':
        if (this.expanded()) {
          event.preventDefault();
          const choice = this.choices()[this.active()];
          if (choice) {
            this.pick(choice);
          } else {
            this.commit();
          }
        } else {
          this.commit();
        }
        break;
      case 'Escape':
        if (this.expanded()) {
          // Otherwise the dialog around it closes too.
          event.preventDefault();
          event.stopPropagation();
          this.open.set(false);
          this.text.set(this.value());
        }
        break;
    }
  }

  protected pick(choice: Choice): void {
    this.value.set(choice.tag);
    this.text.set(choice.tag);
    this.open.set(false);
  }

  protected commit(): void {
    this.open.set(false);
    if (this.text() !== this.value()) {
      this.value.set(tagKey(this.text()));
    }
    this.text.set(this.value());
  }
}
