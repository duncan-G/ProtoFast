import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { onScreenOf, wardrobeOf } from '../../../direction/inheritance';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { Character } from '../../../stories/model/character';
import { CharacterIdentity, EMPTY_IDENTITY } from '../../../stories/model/character-identity';
import { Wardrobe } from '../../../stories/model/wardrobe';
import { Avatar } from '../avatar';
import { initials, plural } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { SheetEditor } from '../sheet-editor';

const GENDERS = ['Female', 'Male', 'Non-binary'];
const SHOWN = 3;

/** A character's story-wide defaults: who they are, what they wear, and where this scene uses it. */
@Component({
  selector: 'app-library-cast-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, SheetEditor],
  host: { class: 'dr-card', '[class.is-open]': 'open()' },
  template: `
    @let c = character();
    <div
      class="dr-card-head"
      role="button"
      tabindex="0"
      [attr.aria-expanded]="open()"
      (click)="toggle()"
      (keydown.enter)="toggle()"
    >
      <app-avatar
        [shape]="store.kindOf(c).avatarShape"
        [hue]="c.hue"
        [initials]="initialsOf(c.name)"
        [size]="30"
      />
      <span class="dr-card-name">
        <span>{{ c.name }}</span>
        <span class="dr-card-sub">{{ summary() }}</span>
      </span>
      <span class="dr-chev" aria-hidden="true">{{ open() ? '▴' : '▾' }}</span>
    </div>

    @if (open()) {
      <div class="dr-card-body">
        <div class="dr-card-grid">
          <label class="dr-field">
            <span class="dr-kicker">Type</span>
            <select
              class="dr-input"
              (change)="store.setCharacterKind(c.id, $any($event.target).value)"
            >
              @for (kind of store.kinds(); track kind.label) {
                <option [value]="kind.label" [selected]="kind.label === c.kind">
                  {{ kind.label }}
                </option>
              }
            </select>
          </label>
          <label class="dr-field">
            <span class="dr-kicker">Gender</span>
            <select class="dr-input" (change)="saveIdentity({ gender: $any($event.target).value })">
              <option value="" [selected]="!identity().gender">—</option>
              @for (gender of genders(); track gender) {
                <option [value]="gender" [selected]="gender === identity().gender">
                  {{ gender }}
                </option>
              }
            </select>
          </label>
          <label class="dr-field">
            <span class="dr-kicker">Age</span>
            <input
              class="dr-input dr-input-mono"
              placeholder="—"
              maxlength="1000"
              [value]="identity().age"
              (change)="saveIdentity({ age: $any($event.target).value.trim() })"
            />
          </label>
        </div>

        <div class="dr-field">
          <div class="dr-field-head">
            <span class="dr-kicker">Description</span>
            <span class="flex-1"></span>
            <button
              type="button"
              class="dr-link"
              title="Have the model write the sheet from the story"
              [disabled]="store.describing() !== null"
              (click)="store.describe({ kind: 'character', id: c.id })"
            >
              {{ store.describing() === c.id ? 'Writing…' : 'Suggest' }}
            </button>
          </div>
          <textarea
            class="dr-input"
            rows="4"
            maxlength="4000"
            placeholder="Who they are and what they look like"
            [value]="c.description ?? ''"
            (change)="save({ description: $any($event.target).value.trim() || null })"
          ></textarea>
        </div>

        <div class="dr-field">
          <div class="dr-field-head">
            <span class="dr-kicker">Wardrobe</span>
            <span class="dr-count">{{ c.wardrobes.length }}</span>
            <span class="flex-1"></span>
            <button type="button" class="dr-link" (click)="addWardrobe()">+ Add</button>
          </div>
          @if (c.wardrobes.length > 5) {
            <input
              class="dr-input is-soft"
              placeholder="Filter wardrobes…"
              aria-label="Filter wardrobes"
              [value]="query()"
              (input)="query.set($any($event.target).value)"
            />
          }
          <div class="dr-wardrobes">
            @for (w of shownWardrobes(); track w.wardrobe.id || w.wardrobe.label) {
              <div class="dr-wardrobe" [class.is-open]="editing() === w.wardrobe.label">
                <div
                  class="dr-wardrobe-head"
                  role="button"
                  tabindex="0"
                  (click)="toggleWardrobe(w.wardrobe.label)"
                  (keydown.enter)="toggleWardrobe(w.wardrobe.label)"
                >
                  <span class="dr-wardrobe-name">
                    <span>{{ w.wardrobe.label }}</span>
                    @if (editing() !== w.wardrobe.label) {
                      <span class="dr-card-sub">{{
                        w.wardrobe.description || 'No description yet'
                      }}</span>
                    }
                  </span>
                  @if (w.isDefault) {
                    <span class="dr-default">Default</span>
                  }
                  @if (w.panels.length > 0) {
                    <span class="dr-use" [title]="w.usage"
                      >SC{{ sceneNumber() }} · {{ w.panels.length }}P</span
                    >
                  }
                  <span class="dr-chev" aria-hidden="true">{{
                    editing() === w.wardrobe.label ? '▴' : '▾'
                  }}</span>
                </div>
                @if (editing() === w.wardrobe.label) {
                  <div class="dr-wardrobe-body">
                    <input
                      class="dr-input"
                      aria-label="Wardrobe name"
                      maxlength="255"
                      [value]="w.wardrobe.label"
                      (change)="renameWardrobe(w.wardrobe, $any($event.target).value)"
                    />
                    <textarea
                      class="dr-input"
                      rows="2"
                      placeholder="What it looks like…"
                      maxlength="1000"
                      [value]="w.wardrobe.description"
                      (change)="
                        updateWardrobe(w.wardrobe, { description: $any($event.target).value })
                      "
                    ></textarea>
                    <span class="dr-hint">{{ w.usage }}</span>
                    <div class="dr-row-actions">
                      @if (!w.isDefault) {
                        <button
                          type="button"
                          class="dr-btn is-small"
                          (click)="makeDefault(w.wardrobe)"
                        >
                          Make default
                        </button>
                      }
                      <span class="flex-1"></span>
                      @if (!w.isDefault && w.panels.length === 0) {
                        <button
                          type="button"
                          class="dr-danger"
                          (click)="removeWardrobe(w.wardrobe)"
                        >
                          Delete
                        </button>
                      }
                    </div>
                  </div>
                }
              </div>
            } @empty {
              <div class="dr-wardrobe-empty">{{ query() ? 'No match' : 'No wardrobes yet' }}</div>
            }
            @if (!query() && sortedWardrobes().length > shown) {
              <button type="button" class="dr-wardrobe-more" (click)="all.set(!all())">
                {{ all() ? 'Show fewer' : 'Show all ' + sortedWardrobes().length }}
              </button>
            }
          </div>
        </div>

        <button type="button" class="dr-link self-start" (click)="details.set(!details())">
          Identity &amp; voice {{ details() ? '▴' : '▾' }}
        </button>
        @if (details()) {
          <app-sheet-editor [target]="{ kind: 'character', id: c.id }" [details]="true" />
        }

        @if (problem()) {
          <span class="dr-error">{{ problem() }}</span>
        }
        <div class="dr-card-foot">
          {{ usage() }}
          @if (overrides()) {
            <br /><span class="dr-accent">{{ overrides() }}</span>
          }
        </div>
      </div>
    }
  `,
})
export class LibraryCastCard {
  protected readonly store = inject(SceneEditorStore);

  readonly character = input.required<Character>();

  protected readonly shown = SHOWN;
  protected readonly query = signal('');
  protected readonly all = signal(false);
  protected readonly editing = signal<string | null>(null);
  protected readonly details = signal(false);
  protected readonly problem = signal('');

  protected readonly open = computed(
    () => this.store.directorLibraryEntry() === this.character().id,
  );
  protected readonly identity = computed<CharacterIdentity>(
    () => this.character().appearance ?? EMPTY_IDENTITY,
  );
  protected readonly genders = computed(() => {
    const own = this.identity().gender;
    return own && !GENDERS.includes(own) ? [...GENDERS, own] : GENDERS;
  });
  protected readonly summary = computed(() =>
    [this.character().kind, this.identity().gender, this.identity().age]
      .filter(Boolean)
      .join(' · '),
  );
  protected readonly sceneNumber = computed(() => (this.store.place()?.index ?? 0) + 1);

  /** Panel numbers in this scene that show the character, by the wardrobe they wear there. */
  private readonly panelsByWardrobe = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    const character = this.character();
    const byWardrobe = new Map<string, number[]>();
    this.store.panelSpans().forEach((span, i) => {
      if (onScreenOf(direction, span.panel, character.id).value) {
        const id = wardrobeOf(direction, span.panel, character).wardrobeId;
        if (id) {
          byWardrobe.set(id, [...(byWardrobe.get(id) ?? []), i + 1]);
        }
      }
    });
    return byWardrobe;
  });

  /** The default first, then those this scene uses, then the rest. */
  protected readonly sortedWardrobes = computed(() => {
    const used = this.panelsByWardrobe();
    const scene = this.sceneNumber();
    return this.character()
      .wardrobes.map((wardrobe, index) => {
        const panels = used.get(wardrobe.id) ?? [];
        return {
          wardrobe,
          isDefault: index === 0,
          panels,
          usage: panels.length
            ? `Scene ${scene}, panel${panels.length > 1 ? 's' : ''} ${panels.join(', ')}`
            : 'Not used in this scene',
        };
      })
      .sort(
        (a, b) =>
          Number(b.isDefault) - Number(a.isDefault) ||
          Number(b.panels.length > 0) - Number(a.panels.length > 0),
      );
  });

  protected readonly shownWardrobes = computed(() => {
    const needle = this.query().trim().toLowerCase();
    const hits = this.sortedWardrobes().filter(
      (w) =>
        !needle || `${w.wardrobe.label} ${w.wardrobe.description}`.toLowerCase().includes(needle),
    );
    return this.all() || needle ? hits : hits.slice(0, SHOWN);
  });

  protected readonly usage = computed(() => {
    const id = this.character().id;
    if (!this.store.cast().some((c) => c.id === id)) {
      return `Not in Scene ${this.sceneNumber()}`;
    }
    const direction = this.store.direction() ?? emptyDirection('');
    const spans = this.store.panelSpans();
    const shown = spans.filter((s) => onScreenOf(direction, s.panel, id).value).length;
    return `In Scene ${this.sceneNumber()} · on screen in ${shown} of ${plural(spans.length, 'panel')}`;
  });

  protected readonly overrides = computed(() => {
    const id = this.character().id;
    const look = this.store.appearanceByCharacter().get(id);
    const fields = [
      look?.wardrobeId ? 'wardrobe' : '',
      look?.wardrobeChange ? 'wardrobe changes' : '',
      look?.appearanceChange ? 'appearance' : '',
    ].filter(Boolean);
    const panels = this.store
      .panelSpans()
      .filter((s) =>
        s.panel.placements.some(
          (p) => p.characterId === id && (p.wardrobeId || p.wardrobeChange || p.appearanceChange),
        ),
      ).length;
    return [
      fields.length ? `Scene ${this.sceneNumber()} overrides ${fields.join(', ')}` : '',
      panels ? plural(panels, 'panel override') : '',
    ]
      .filter(Boolean)
      .join(' · ');
  });

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected toggle(): void {
    this.store.directorLibraryEntry.set(this.open() ? null : this.character().id);
  }

  protected toggleWardrobe(label: string): void {
    this.editing.set(this.editing() === label ? null : label);
  }

  protected async save(patch: Partial<Character>): Promise<void> {
    this.problem.set(
      (await this.store.saveCharacterSheet({ ...this.character(), ...patch })) ?? '',
    );
  }

  protected saveIdentity(patch: Partial<CharacterIdentity>): Promise<void> {
    return this.save({ appearance: { ...this.identity(), ...patch } });
  }

  protected addWardrobe(): void {
    const labels = new Set(this.character().wardrobes.map((w) => w.label.toLowerCase()));
    let n = 1;
    while (labels.has(`new wardrobe ${n}`)) {
      n++;
    }
    const label = `New wardrobe ${n}`;
    this.all.set(true);
    this.query.set('');
    this.editing.set(label);
    void this.save({
      wardrobes: [...this.character().wardrobes, { id: '', label, description: '' }],
    });
  }

  protected updateWardrobe(wardrobe: Wardrobe, patch: Partial<Wardrobe>): Promise<void> {
    return this.save({
      wardrobes: this.character().wardrobes.map((w) => (w === wardrobe ? { ...w, ...patch } : w)),
    });
  }

  protected renameWardrobe(wardrobe: Wardrobe, label: string): void {
    const next = label.trim();
    if (next && next !== wardrobe.label) {
      this.editing.set(next);
      void this.updateWardrobe(wardrobe, { label: next });
    }
  }

  /** The sheet's first wardrobe is its default. */
  protected makeDefault(wardrobe: Wardrobe): Promise<void> {
    return this.save({
      wardrobes: [wardrobe, ...this.character().wardrobes.filter((w) => w !== wardrobe)],
    });
  }

  protected removeWardrobe(wardrobe: Wardrobe): void {
    this.editing.set(null);
    void this.save({ wardrobes: this.character().wardrobes.filter((w) => w !== wardrobe) });
  }
}
