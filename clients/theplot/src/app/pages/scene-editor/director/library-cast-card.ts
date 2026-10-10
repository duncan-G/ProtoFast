import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  OnDestroy,
  signal,
} from '@angular/core';
import { lookAt, onScreenOf, wardrobeOf } from '../../../direction/inheritance';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { SpeechVoice } from '../../../direction/model/speech-voice';
import { VoiceQuery } from '../../../direction/model/voice-query';
import { accentLabel, ageLabel, genderLabel } from '../../../direction/voice-filters';
import { Character } from '../../../stories/model/character';
import { CharacterIdentity, EMPTY_IDENTITY } from '../../../stories/model/character-identity';
import { Wardrobe } from '../../../stories/model/wardrobe';
import { Avatar } from '../avatar';
import { initials, plural } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { SheetEditor } from '../sheet-editor';
import { LibraryPicture } from './library-picture';
import { VoicePicker } from './voice-picker';

const GENDERS = ['Female', 'Male', 'Non-binary'];
const SHOWN = 3;

/** A character's story-wide defaults: who they are, what they wear, and where this scene uses it. */
@Component({
  selector: 'app-library-cast-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, LibraryPicture, SheetEditor, VoicePicker],
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
        <button
          type="button"
          class="dr-btn is-small self-start"
          title="Have the model write the whole sheet from the story, then cast a voice if none is"
          [class.is-working]="store.describing().has(c.id)"
          [disabled]="store.describing().has(c.id)"
          (click)="store.describe({ kind: 'character', id: c.id })"
        >
          {{ store.describing().has(c.id) ? 'Suggesting…' : 'Suggest character' }}
        </button>
        <app-library-picture [target]="{ kind: 'character', id: c.id }" [name]="c.name" />
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
          <span class="dr-kicker">Description</span>
          <textarea
            class="dr-input"
            rows="4"
            maxlength="4000"
            placeholder="What they look like"
            [value]="c.description ?? ''"
            (change)="save({ description: $any($event.target).value.trim() || null })"
          ></textarea>
        </div>

        <div class="dr-field">
          <div class="dr-field-head">
            <span class="dr-kicker">Voice</span>
            <span class="flex-1"></span>
            <button
              type="button"
              class="dr-link"
              title="Cast a library voice that fits the sheet and no one else uses"
              [class.is-working]="store.castingVoice().has(c.id)"
              [disabled]="store.castingVoice().has(c.id) || store.describing().has(c.id)"
              (click)="store.castVoice(c.id)"
            >
              {{ store.castingVoice().has(c.id) ? 'Casting…' : 'Suggest' }}
            </button>
          </div>
          @if (pickingVoice()) {
            <app-voice-picker
              title="Cast a voice"
              action="Cast"
              chosenLabel="Current"
              [chosen]="castVoices()"
              [initial]="voiceFilters()"
              (picked)="castVoice($event)"
              (closed)="pickingVoice.set(false)"
            />
          } @else {
            <div class="dr-narrator-head">
              <button
                type="button"
                class="dr-voice-play"
                [disabled]="!c.voice?.previewUrl"
                [attr.aria-label]="(playing() ? 'Stop ' : 'Play ') + voiceLabel()"
                (click)="preview()"
              >
                {{ playing() ? '■' : '▶' }}
              </button>
              <span class="dr-voice-name flex-1">{{ voiceLabel() }}</span>
              <button type="button" class="dr-btn is-small" (click)="pickingVoice.set(true)">
                {{ c.voice?.voiceId ? 'Change' : 'Choose' }}
              </button>
            </div>
          }
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
                  @if (w.frames.length > 0) {
                    <span class="dr-use" [title]="w.usage"
                      >SC{{ sceneNumber() }} · {{ w.frames.length }}F</span
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
                        updateWardrobe(w.wardrobe, {
                          description: $any($event.target).value,
                        })
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
                      @if (!w.isDefault && w.frames.length === 0) {
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
              <div class="dr-wardrobe-empty">
                {{ query() ? 'No match' : 'No wardrobes yet' }}
              </div>
            }
            @if (!query() && sortedWardrobes().length > shown) {
              <button type="button" class="dr-wardrobe-more" (click)="all.set(!all())">
                {{ all() ? 'Show fewer' : 'Show all ' + sortedWardrobes().length }}
              </button>
            }
          </div>
        </div>

        <button type="button" class="dr-link self-start" (click)="details.set(!details())">
          Identity &amp; delivery {{ details() ? '▴' : '▾' }}
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
export class LibraryCastCard implements OnDestroy {
  protected readonly store = inject(SceneEditorStore);

  readonly character = input.required<Character>();

  protected readonly shown = SHOWN;
  protected readonly query = signal('');
  protected readonly all = signal(false);
  protected readonly editing = signal<string | null>(null);
  protected readonly details = signal(false);
  protected readonly problem = signal('');
  protected readonly pickingVoice = signal(false);
  protected readonly playing = signal(false);
  private audio: HTMLAudioElement | null = null;

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
  protected readonly voiceLabel = computed(() => {
    const voice = this.character().voice;
    return voice?.voiceName || voice?.voiceId || 'No voice cast';
  });
  protected readonly castVoices = computed(
    () => new Set([this.character().voice?.voiceId].filter((id): id is string => !!id)),
  );
  /** The picker opens on what the sheet already says. */
  protected readonly voiceFilters = computed<Partial<VoiceQuery>>(() => ({
    gender: genderLabel(this.identity().gender),
    age: ageLabel(this.identity().age),
    accent: accentLabel(this.character().voice?.description, this.character().description),
  }));
  protected readonly sceneNumber = computed(() => (this.store.place()?.index ?? 0) + 1);

  /** Frame numbers in this scene that show the character, by the wardrobe they wear there. */
  private readonly framesByWardrobe = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    const character = this.character();
    const byWardrobe = new Map<string, number[]>();
    this.store.frameSpans().forEach((span, i) => {
      if (onScreenOf(direction, span.frame, character.id).value) {
        const look = lookAt(direction, character.id, this.store.elementOrder(), span.last);
        const id = wardrobeOf(direction, span.frame, character, look).wardrobeId;
        if (id) {
          byWardrobe.set(id, [...(byWardrobe.get(id) ?? []), i + 1]);
        }
      }
    });
    return byWardrobe;
  });

  /** The default first, then those this scene uses, then the rest. */
  protected readonly sortedWardrobes = computed(() => {
    const used = this.framesByWardrobe();
    const scene = this.sceneNumber();
    return this.character()
      .wardrobes.map((wardrobe, index) => {
        const frames = used.get(wardrobe.id) ?? [];
        return {
          wardrobe,
          isDefault: index === 0,
          frames,
          usage: frames.length
            ? `Scene ${scene}, frame${frames.length > 1 ? 's' : ''} ${frames.join(', ')}`
            : 'Not used in this scene',
        };
      })
      .sort(
        (a, b) =>
          Number(b.isDefault) - Number(a.isDefault) ||
          Number(b.frames.length > 0) - Number(a.frames.length > 0),
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
    const spans = this.store.frameSpans();
    const shown = spans.filter((s) => onScreenOf(direction, s.frame, id).value).length;
    return `In Scene ${this.sceneNumber()} · on screen in ${shown} of ${plural(spans.length, 'frame')}`;
  });

  protected readonly overrides = computed(() => {
    const id = this.character().id;
    const look = this.store.appearanceByCharacter().get(id);
    const fields = [
      look?.wardrobeId ? 'wardrobe' : '',
      look?.wardrobeChange ? 'wardrobe changes' : '',
      look?.appearanceChange ? 'appearance' : '',
    ].filter(Boolean);
    const frames = this.store
      .frameSpans()
      .filter((s) =>
        s.frame.placements.some(
          (p) => p.characterId === id && (p.wardrobeId || p.wardrobeChange || p.appearanceChange),
        ),
      ).length;
    return [
      fields.length ? `Scene ${this.sceneNumber()} overrides ${fields.join(', ')}` : '',
      frames ? plural(frames, 'frame override') : '',
    ]
      .filter(Boolean)
      .join(' · ');
  });

  ngOnDestroy(): void {
    this.audio?.pause();
  }

  protected preview(): void {
    this.audio?.pause();
    const url = this.character().voice?.previewUrl;
    if (this.playing() || !url) {
      this.playing.set(false);
      return;
    }
    const audio = new Audio(url);
    audio.onended = () => this.playing.set(false);
    this.audio = audio;
    this.playing.set(true);
    audio.play().catch(() => this.playing.set(false));
  }

  protected async castVoice(voice: SpeechVoice): Promise<void> {
    this.pickingVoice.set(false);
    this.audio?.pause();
    this.playing.set(false);
    this.problem.set((await this.store.setCharacterVoice(this.character(), voice)) ?? '');
  }

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
      (await this.store.saveCharacterSheet({
        ...this.character(),
        ...patch,
      })) ?? '',
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
    void this.save({
      wardrobes: this.character().wardrobes.filter((w) => w !== wardrobe),
    });
  }
}
