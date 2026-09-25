import {
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
import { findByName } from '../../stories/library-names';
import { Character } from '../../stories/model/character';
import { SceneElement } from '../../stories/model/scene-element';
import { Avatar } from './avatar';
import { initials, plural } from './format';
import { SceneEditorStore } from './scene-editor-store';

@Component({
  selector: 'app-speaker-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar],
  host: { class: 'se-combo block max-w-[380px]' },
  template: `
    <div class="se-combo-field is-pill">
      @if (current(); as speaker) {
        <app-avatar
          [shape]="store.kindOf(speaker).avatarShape"
          [hue]="speaker.hue"
          [initials]="initialsOf(speaker)"
          [size]="24"
        />
      } @else {
        <app-avatar [shape]="'Circle'" initials="?" [size]="24" />
      }
      <input
        #field
        class="se-combo-input"
        role="combobox"
        aria-label="Speaker"
        [attr.aria-expanded]="open()"
        placeholder="Search characters or type a new name"
        [value]="open() ? query() : (current()?.name ?? '')"
        (focus)="open.set(true)"
        (blur)="close()"
        (input)="query.set($any($event.target).value); active.set(0)"
        (keydown)="onKey($event)"
      />
      <span class="se-combo-meta">{{ countLabel() }} ▾</span>
    </div>
    @if (open()) {
      <div class="se-pop" role="listbox">
        @for (group of groups(); track group.title) {
          <div class="se-pop-group">{{ group.title }}</div>
          @for (character of group.items; track character.id) {
            <button
              type="button"
              class="se-pop-item"
              role="option"
              [class.is-active]="flat()[active()] === character"
              [attr.aria-selected]="character.id === element().speakerId"
              (mousedown)="$event.preventDefault(); pick(character)"
            >
              <app-avatar
                [shape]="store.kindOf(character).avatarShape"
                [hue]="character.hue"
                [initials]="initialsOf(character)"
                [size]="22"
              />
              <span class="se-pop-label">{{ character.name }}</span>
              <span class="se-pop-meta">{{ meta(character) }}</span>
              <span class="se-pop-check">{{
                character.id === element().speakerId ? '✓' : ''
              }}</span>
            </button>
          }
        }
        @if (canCreate()) {
          <button
            type="button"
            class="se-pop-item se-pop-create"
            (mousedown)="$event.preventDefault(); create()"
          >
            <span class="se-pop-label">+ Add “{{ query().trim() }}”</span>
            <span class="se-pop-meta">adds to characters</span>
          </button>
        }
        @if (problem()) {
          <p class="se-error px-2 py-1.5">{{ problem() }}</p>
        }
      </div>
    }
  `,
})
export class SpeakerPicker {
  protected readonly store = inject(SceneEditorStore);

  readonly element = input.required<SceneElement>();
  readonly index = input.required<number>();
  readonly picked = output();

  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly active = signal(0);
  protected readonly problem = signal('');
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');

  protected readonly current = computed(() => {
    const id = this.element().speakerId;
    return id ? (this.store.characterById().get(id) ?? null) : null;
  });

  private readonly matches = computed(() => {
    const needle = this.query().trim().toLowerCase();
    return this.store.characters().filter((c) => c.name.toLowerCase().includes(needle));
  });
  protected readonly groups = computed(() => {
    const lines = this.store.stats().lines;
    const recent: string[] = [];
    for (const e of this.store.elements().slice(0, this.index()).reverse()) {
      if (e.type === 'Dialogue' && e.speakerId && !recent.includes(e.speakerId)) {
        recent.push(e.speakerId);
      }
    }
    const rank = (id: string) => (recent.includes(id) ? recent.indexOf(id) : recent.length);
    const speaking = this.matches()
      .filter((c) => lines.has(c.id))
      .sort((a, b) => rank(a.id) - rank(b.id));
    const rest = this.matches()
      .filter((c) => !lines.has(c.id))
      .sort((a, b) => a.name.localeCompare(b.name));
    return [
      { title: 'Speaking in this scene', items: speaking },
      { title: this.query().trim() ? 'Characters' : 'Other characters', items: rest },
    ].filter((g) => g.items.length > 0);
  });
  protected readonly flat = computed(() => this.groups().flatMap((g) => g.items));
  protected readonly canCreate = computed(() => {
    const name = this.query().trim();
    return !!name && !findByName(this.store.characters(), name);
  });
  protected readonly countLabel = computed(() => {
    if (!this.open()) {
      return this.current() ? 'Change' : 'Pick one';
    }
    const total = this.store.characters().length;
    return this.query().trim()
      ? `${this.matches().length} of ${total}`
      : plural(total, 'character');
  });

  protected initialsOf(character: Character): string {
    return initials(character.name);
  }

  protected meta(character: Character): string {
    const lines = this.store.stats().lines.get(character.id);
    return lines ? plural(lines, 'line') : character.kind;
  }

  protected pick(character: Character): void {
    this.store.updateElement(this.element().id, { speakerId: character.id });
    this.close();
    this.field().nativeElement.blur();
    this.picked.emit();
  }

  protected async create(): Promise<void> {
    const created = await this.store.addCharacter(this.query());
    if (typeof created === 'string') {
      this.problem.set(created);
      return;
    }
    this.pick(created);
  }

  protected close(): void {
    this.open.set(false);
    this.query.set('');
    this.active.set(0);
    this.problem.set('');
  }

  protected onKey(event: KeyboardEvent): void {
    const count = this.flat().length;
    if (event.key === 'ArrowDown' && count) {
      event.preventDefault();
      this.open.set(true);
      this.active.update((i) => (i + 1) % count);
    } else if (event.key === 'ArrowUp' && count) {
      event.preventDefault();
      this.active.update((i) => (i - 1 + count) % count);
    } else if (event.key === 'Enter') {
      event.preventDefault();
      const character = this.flat()[this.active()];
      if (character) {
        this.pick(character);
      } else if (this.canCreate()) {
        void this.create();
      }
    } else if (event.key === 'Escape') {
      event.stopPropagation();
      this.close();
      this.field().nativeElement.blur();
    }
  }
}
