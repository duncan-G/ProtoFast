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
import { findByName } from '../../stories/library-names';
import { Location } from '../../stories/model/location';
import { SceneElement } from '../../stories/model/scene-element';
import { parseLocationQuery, settingPrefix } from './format';
import { SceneEditorStore } from './scene-editor-store';

@Component({
  selector: 'app-location-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'se-combo block' },
  template: `
    <div class="se-combo-field">
      @if (current(); as location) {
        <span class="se-swatch hue-fill" [style.--hue]="location.hue"></span>
      } @else {
        <span class="se-swatch se-swatch-none"></span>
      }
      <input
        #field
        class="se-combo-input se-script"
        role="combobox"
        aria-label="Location"
        [attr.aria-expanded]="open()"
        [placeholder]="
          'Search ' + store.locations().length + ' locations, or type a new one (e.g. EXT. Harbor)'
        "
        [value]="open() ? query() : currentLabel()"
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
          @for (location of group.items; track location.id) {
            <button
              type="button"
              class="se-pop-item"
              role="option"
              [class.is-active]="flat()[active()] === location"
              [attr.aria-selected]="location.id === element().locationId"
              (mousedown)="$event.preventDefault(); pick(location)"
            >
              <span class="se-swatch hue-fill" [style.--hue]="location.hue"></span>
              <span class="se-pop-label se-script text-[13px] font-bold">{{
                label(location)
              }}</span>
              <span class="se-pop-meta">{{ meta(location) }}</span>
              <span class="se-pop-check">{{
                location.id === element().locationId ? '✓' : ''
              }}</span>
            </button>
          }
        }
        @if (flat().length === 0 && !parsed().name) {
          <div class="se-pop-empty">No location matches.</div>
        }
        @if (canCreate()) {
          <button
            type="button"
            class="se-pop-item se-pop-create"
            (mousedown)="$event.preventDefault(); create()"
          >
            <span class="se-pop-label">+ Create “{{ createLabel() }}”</span>
            <span class="se-pop-meta">adds to library</span>
          </button>
        }
        @if (problem()) {
          <p class="se-error px-2 py-1.5">{{ problem() }}</p>
        }
      </div>
    }
  `,
})
export class LocationPicker {
  protected readonly store = inject(SceneEditorStore);

  readonly element = input.required<SceneElement>();
  readonly autofocus = input(false);
  readonly escape = output();

  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly active = signal(0);
  protected readonly problem = signal('');
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');

  protected readonly current = computed(() => {
    const id = this.element().locationId;
    return id ? (this.store.locationById().get(id) ?? null) : null;
  });
  protected readonly currentLabel = computed(() => {
    const location = this.current();
    return location ? this.label(location) : '';
  });
  protected readonly parsed = computed(() => parseLocationQuery(this.query()));

  private readonly matches = computed(() => {
    const { setting, name } = this.parsed();
    const needle = name.toLowerCase();
    return this.store
      .locations()
      .filter((l) => (!setting || l.setting === setting) && l.name.toLowerCase().includes(needle));
  });
  protected readonly groups = computed(() => {
    const used = this.store.stats().headings;
    const inScene = this.matches().filter((l) => used.has(l.id));
    const rest = this.matches()
      .filter((l) => !used.has(l.id))
      .sort((a, b) => a.name.localeCompare(b.name));
    return [
      { title: 'In this scene', items: inScene },
      { title: this.parsed().name ? 'Library' : 'All locations', items: rest },
    ].filter((g) => g.items.length > 0);
  });
  protected readonly flat = computed(() => this.groups().flatMap((g) => g.items));

  protected readonly canCreate = computed(() => {
    const name = this.parsed().name;
    return !!name && !findByName(this.store.locations(), name);
  });
  protected readonly createLabel = computed(
    () => `${settingPrefix(this.parsed().setting ?? 'Interior')} ${this.parsed().name}`,
  );
  protected readonly countLabel = computed(() => {
    if (!this.open()) {
      return this.current() ? 'Change' : 'Pick one';
    }
    const total = this.store.locations().length;
    return this.parsed().name ? `${this.matches().length} of ${total}` : `${total} total`;
  });

  constructor() {
    afterNextRender(() => {
      if (this.autofocus()) {
        this.field().nativeElement.focus();
      }
    });
  }

  protected label(location: Location): string {
    return `${settingPrefix(location.setting)} ${location.name}`;
  }

  protected meta(location: Location): string {
    const uses = this.store.stats().headings.get(location.id) ?? 0;
    const onlyHere = location.id === this.element().locationId && uses === 1;
    return uses && !onlyHere ? 'in scene' : '';
  }

  protected pick(location: Location): void {
    this.store.updateElement(this.element().id, { locationId: location.id });
    this.close();
    this.field().nativeElement.blur();
  }

  protected async create(): Promise<void> {
    const { setting, name } = this.parsed();
    const created = await this.store.addLocation(name, setting ?? 'Interior');
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
      const location = this.flat()[this.active()];
      if (location) {
        this.pick(location);
      } else if (this.canCreate()) {
        void this.create();
      }
    } else if (event.key === 'Escape') {
      event.stopPropagation();
      if (this.open()) {
        this.close();
        this.field().nativeElement.blur();
      } else {
        this.escape.emit();
      }
    }
  }
}
