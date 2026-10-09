import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { Character } from '../../stories/model/character';
import { EMPTY_IDENTITY } from '../../stories/model/character-identity';
import { Location } from '../../stories/model/location';
import { Prop } from '../../stories/model/prop';
import { ReferenceTarget } from '../../stories/model/reference-target';
import { EMPTY_VOICE, VoiceProfile } from '../../stories/model/voice-profile';
import { SceneEditorStore } from './scene-editor-store';
import { StateChips } from './state-chips';

/**
 * A library entry's sheet, edited in place in director mode. Fields save when they lose focus;
 * "Describe" has the model write the sheet from everything the story says about the entry. With
 * `details`, only what the director library's cards leave out: identity, voice, look and sound.
 */
@Component({
  selector: 'app-sheet-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StateChips],
  host: { class: 'se-sheet' },
  template: `
    @if (!details()) {
      <div class="se-direction-row">
        <span class="se-kicker">Sheet</span>
        <span class="flex-1"></span>
        <button
          type="button"
          class="se-btn se-btn-text"
          [class.is-working]="store.describing() === target().id"
          [disabled]="store.describing() !== null"
          title="Have the model write this sheet from the story"
          (click)="store.describe(target())"
        >
          {{ store.describing() === target().id ? 'Describing…' : 'Describe' }}
        </button>
      </div>
    }
    @if (problem()) {
      <p class="se-error">{{ problem() }}</p>
    }
    @switch (target().kind) {
      @case ('character') {
        @if (character(); as c) {
          @if (!details()) {
            <textarea
              class="se-field se-textarea-small"
              rows="2"
              placeholder="Who they are, for a reader"
              aria-label="Description"
              maxlength="4000"
              [value]="c.description ?? ''"
              (change)="
                saveCharacter(c, {
                  description: $any($event.target).value || null,
                })
              "
            ></textarea>
          }
          <div class="se-kicker">Identity · never changes</div>
          <div class="se-sheet-grid">
            @for (field of shownIdentityFields(); track field.key) {
              <input
                class="se-field"
                [placeholder]="field.label"
                [attr.aria-label]="field.label"
                maxlength="1000"
                [value]="c.appearance?.[field.key] ?? ''"
                (change)="saveIdentity(c, field.key, $any($event.target).value)"
              />
            }
          </div>
          <app-state-chips
            [state]="c.appearance?.marks ?? []"
            placeholder="marks: scar, glasses…"
            (change)="
              saveCharacter(c, {
                appearance: { ...(c.appearance ?? empty), marks: $event },
              })
            "
          />
          <div class="se-kicker">Voice</div>
          <input
            class="se-field"
            placeholder="voice: age, texture, pace, accent"
            aria-label="Voice description"
            maxlength="1000"
            [value]="c.voice?.description ?? ''"
            (change)="saveVoice(c, { description: $any($event.target).value })"
          />
          <input
            class="se-field"
            placeholder="baseline delivery, e.g. dry, understated"
            aria-label="Baseline delivery"
            maxlength="1000"
            [value]="c.voice?.baseline ?? ''"
            (change)="saveVoice(c, { baseline: $any($event.target).value })"
          />
          @if (!details()) {
            <div class="se-kicker">Wardrobes</div>
            @for (wardrobe of c.wardrobes; track $index) {
              <div class="se-wardrobe">
                <input
                  class="se-field"
                  placeholder="label, e.g. work suit"
                  aria-label="Wardrobe label"
                  maxlength="255"
                  [value]="wardrobe.label"
                  (change)="
                    saveWardrobe(c, $index, {
                      label: $any($event.target).value,
                    })
                  "
                />
                <input
                  class="se-field"
                  placeholder="description"
                  aria-label="Wardrobe description"
                  maxlength="1000"
                  [value]="wardrobe.description"
                  (change)="
                    saveWardrobe(c, $index, {
                      description: $any($event.target).value,
                    })
                  "
                />
                <button
                  type="button"
                  class="se-entry-del"
                  title="Remove this wardrobe"
                  aria-label="Remove wardrobe"
                  (click)="
                    saveCharacter(c, {
                      wardrobes: c.wardrobes.filter((_, i) => i !== $index),
                    })
                  "
                >
                  ×
                </button>
              </div>
            }
            <button type="button" class="se-btn se-btn-text self-start" (click)="addWardrobe(c)">
              + Add wardrobe
            </button>
          }
        }
      }
      @case ('location') {
        @if (location(); as l) {
          @if (!details()) {
            <textarea
              class="se-field se-textarea-small"
              rows="2"
              placeholder="What it is, for a reader"
              aria-label="Description"
              maxlength="4000"
              [value]="l.description ?? ''"
              (change)="
                saveLocation(l, {
                  description: $any($event.target).value || null,
                })
              "
            ></textarea>
          }
          <input
            class="se-field"
            placeholder="layout, for a picture"
            aria-label="Layout"
            maxlength="1000"
            [value]="l.appearance?.layout ?? ''"
            (change)="
              saveLocation(l, {
                appearance: {
                  ...(l.appearance ?? emptyLook),
                  layout: $any($event.target).value,
                },
              })
            "
          />
          <input
            class="se-field"
            placeholder="key features a picture always shows"
            aria-label="Key features"
            maxlength="1000"
            [value]="l.appearance?.keyFeatures ?? ''"
            (change)="
              saveLocation(l, {
                appearance: {
                  ...(l.appearance ?? emptyLook),
                  keyFeatures: $any($event.target).value,
                },
              })
            "
          />
          <div class="se-kicker">Lighting</div>
          @for (light of l.appearance?.lighting ?? []; track $index) {
            <div class="se-wardrobe">
              <select
                class="se-field se-field-inline"
                aria-label="Time of day"
                [value]="light.timeOfDay"
                (change)="
                  saveLighting(l, $index, {
                    timeOfDay: $any($event.target).value,
                  })
                "
              >
                @for (time of store.timesOfDay(); track time) {
                  <option [value]="time">{{ time }}</option>
                }
              </select>
              <input
                class="se-field"
                placeholder="the light"
                aria-label="Lighting"
                maxlength="1000"
                [value]="light.description"
                (change)="
                  saveLighting(l, $index, {
                    description: $any($event.target).value,
                  })
                "
              />
              <button
                type="button"
                class="se-entry-del"
                aria-label="Remove lighting"
                (click)="
                  saveLocation(l, {
                    appearance: {
                      ...(l.appearance ?? emptyLook),
                      lighting: (l.appearance?.lighting ?? []).filter((_, i) => i !== $index),
                    },
                  })
                "
              >
                ×
              </button>
            </div>
          }
          <button type="button" class="se-btn se-btn-text self-start" (click)="addLighting(l)">
            + Add a time of day
          </button>
          <div class="se-kicker">Acoustics</div>
          <input
            class="se-field"
            placeholder="space, e.g. small tiled kitchen"
            aria-label="Acoustic space"
            maxlength="1000"
            [value]="l.acoustics?.space ?? ''"
            (change)="
              saveLocation(l, {
                acoustics: {
                  space: $any($event.target).value,
                  ambience: l.acoustics?.ambience ?? '',
                },
              })
            "
          />
          <input
            class="se-field"
            placeholder="ambience, e.g. fridge hum, distant traffic"
            aria-label="Ambience"
            maxlength="1000"
            [value]="l.acoustics?.ambience ?? ''"
            (change)="
              saveLocation(l, {
                acoustics: {
                  space: l.acoustics?.space ?? '',
                  ambience: $any($event.target).value,
                },
              })
            "
          />
        }
      }
      @case ('prop') {
        @if (prop(); as p) {
          <textarea
            class="se-field se-textarea-small"
            rows="2"
            placeholder="What it is and what it looks like"
            aria-label="Description"
            maxlength="4000"
            [value]="p.description ?? ''"
            (change)="saveProp(p, { description: $any($event.target).value || null })"
          ></textarea>
        }
      }
    }
  `,
})
export class SheetEditor {
  protected readonly store = inject(SceneEditorStore);

  readonly target = input.required<ReferenceTarget>();
  readonly details = input(false);

  protected readonly empty = EMPTY_IDENTITY;
  protected readonly emptyLook = { layout: '', keyFeatures: '', lighting: [] };
  protected readonly identityFields = [
    { key: 'age', label: 'age' },
    { key: 'build', label: 'build' },
    { key: 'face', label: 'face' },
    { key: 'hair', label: 'hair' },
    { key: 'skin', label: 'skin' },
  ] as const;
  protected readonly shownIdentityFields = computed(() =>
    this.identityFields.filter((f) => !this.details() || f.key !== 'age'),
  );
  protected readonly problem = signal('');

  protected readonly character = computed(() =>
    this.target().kind === 'character'
      ? (this.store.characterById().get(this.target().id) ?? null)
      : null,
  );
  protected readonly location = computed(() =>
    this.target().kind === 'location'
      ? (this.store.locationById().get(this.target().id) ?? null)
      : null,
  );
  protected readonly prop = computed(() =>
    this.target().kind === 'prop' ? (this.store.propById().get(this.target().id) ?? null) : null,
  );

  constructor() {
    effect(() => {
      this.target();
      this.problem.set('');
    });
  }

  protected async saveCharacter(character: Character, patch: Partial<Character>): Promise<void> {
    this.problem.set((await this.store.saveCharacterSheet({ ...character, ...patch })) ?? '');
  }

  protected saveVoice(character: Character, patch: Partial<VoiceProfile>): Promise<void> {
    return this.saveCharacter(character, {
      voice: { ...EMPTY_VOICE, ...character.voice, ...patch },
    });
  }

  protected saveIdentity(
    character: Character,
    key: 'age' | 'build' | 'face' | 'hair' | 'skin',
    value: string,
  ): Promise<void> {
    return this.saveCharacter(character, {
      appearance: { ...(character.appearance ?? EMPTY_IDENTITY), [key]: value },
    });
  }

  protected saveWardrobe(
    character: Character,
    index: number,
    patch: Partial<{ label: string; description: string }>,
  ): Promise<void> {
    return this.saveCharacter(character, {
      wardrobes: character.wardrobes.map((w, i) => (i === index ? { ...w, ...patch } : w)),
    });
  }

  protected addWardrobe(character: Character): void {
    const label = `Outfit ${character.wardrobes.length + 1}`;
    void this.saveCharacter(character, {
      wardrobes: [...character.wardrobes, { id: '', label, description: '' }],
    });
  }

  protected async saveLocation(location: Location, patch: Partial<Location>): Promise<void> {
    this.problem.set((await this.store.saveLocationSheet({ ...location, ...patch })) ?? '');
  }

  protected saveLighting(
    location: Location,
    index: number,
    patch: Partial<{ timeOfDay: string; description: string }>,
  ): Promise<void> {
    const appearance = location.appearance ?? this.emptyLook;
    return this.saveLocation(location, {
      appearance: {
        ...appearance,
        lighting: appearance.lighting.map((l, i) => (i === index ? { ...l, ...patch } : l)),
      },
    });
  }

  protected addLighting(location: Location): void {
    const appearance = location.appearance ?? this.emptyLook;
    const used = new Set(appearance.lighting.map((l) => l.timeOfDay));
    const time = this.store.timesOfDay().find((t) => !used.has(t)) ?? this.store.timesOfDay()[0];
    void this.saveLocation(location, {
      appearance: {
        ...appearance,
        lighting: [...appearance.lighting, { timeOfDay: time, description: '' }],
      },
    });
  }

  protected async saveProp(prop: Prop, patch: Partial<Prop>): Promise<void> {
    this.problem.set((await this.store.savePropSheet({ ...prop, ...patch })) ?? '');
  }
}
