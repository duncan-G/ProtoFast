import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  inViewOf,
  joinChanges,
  LookSource,
  onScreenOf,
  wardrobeOf,
} from '../../../direction/inheritance';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { Avatar } from '../avatar';
import { initials } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { ChangeLayers } from './change-layers';

type LookField = 'wardrobeId' | 'wardrobeChange' | 'appearanceChange';

/** "Panel 3" or "Panels 2, 5". */
function panelList(numbers: readonly number[]): string {
  return (numbers.length === 1 ? 'Panel ' : 'Panels ') + numbers.join(', ');
}

/**
 * Who is on screen and what they look like, for the selected panel or the whole scene. Looks come
 * from the library, change in the scene, and can change again in a panel.
 */
@Component({
  selector: 'app-inspector-cast-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, ChangeLayers],
  host: { class: 'dr-tab-body' },
  template: `
    <div class="dr-field">
      <div class="dr-field-head">
        <span class="dr-kicker">On screen</span>
        <span class="flex-1"></span>
        @if (panel(); as panel) {
          @if (castOverridden()) {
            <span class="dr-override"
              >override ·
              <button
                type="button"
                class="dr-link is-quiet"
                (click)="store.resetCast(panel.firstElementId)"
              >
                reset
              </button></span
            >
          } @else {
            <span class="dr-inherited">from scene</span>
          }
        }
      </div>
      <div class="dr-chain" aria-hidden="true">
        <span class="dr-layer-tag is-library">Library</span>→<span class="dr-layer-tag is-scene"
          >Scene</span
        >→<span class="dr-layer-tag is-here">Panel</span>
      </div>
      @for (row of rows(); track row.character.id) {
        <div class="dr-cast" [class.is-off]="!row.on">
          <div
            class="dr-cast-head"
            [attr.role]="row.on ? 'button' : null"
            [attr.tabindex]="row.on ? 0 : null"
            [attr.aria-expanded]="row.on ? row.open : null"
            (click)="toggleOpen(row.character.id, row.on)"
            (keydown.enter)="toggleOpen(row.character.id, row.on)"
          >
            <app-avatar
              [shape]="store.kindOf(row.character).avatarShape"
              [hue]="row.character.hue"
              [initials]="initialsOf(row.character.name)"
              [size]="26"
            />
            <span class="dr-cast-name">
              <span>{{ row.character.name }}</span>
              <span class="dr-cast-look">{{ row.summary }}</span>
            </span>
            @if (row.on && !row.open && row.edits > 0) {
              <span class="dr-cast-edits"
                >{{ row.edits }} edit{{ row.edits === 1 ? '' : 's' }}</span
              >
            }
            <button
              type="button"
              class="dr-switch"
              role="switch"
              [attr.aria-checked]="row.on"
              [attr.aria-label]="row.character.name + ' on screen'"
              (click)="
                $event.stopPropagation(); store.setOnScreen(row.character.id, !row.on, panelKey())
              "
            >
              <span></span>
            </button>
            <span class="dr-chev" aria-hidden="true">{{
              row.on ? (row.open ? '▴' : '▾') : ''
            }}</span>
          </div>
          @if (row.on && row.open) {
            <div class="dr-cast-body">
              <div class="dr-field">
                <div class="dr-field-head">
                  <span class="dr-kicker">Wardrobe</span>
                  <span class="flex-1"></span>
                  <span class="dr-layer-tag" [class]="'is-' + tagClass(row.wardrobeSource)">{{
                    row.wardrobeSource
                  }}</span>
                  @if (row.wardrobeSource === here()) {
                    <button
                      type="button"
                      class="dr-link is-quiet"
                      (click)="set(row.character.id, 'wardrobeId', null)"
                    >
                      reset
                    </button>
                  }
                </div>
                @if (row.character.wardrobes.length > 0) {
                  <select
                    class="dr-input"
                    aria-label="Wardrobe"
                    (edit)="set(row.character.id, 'wardrobeId', $any($event.target).value)"
                  >
                    @for (w of row.character.wardrobes; track w.id; let first = $first) {
                      <option [value]="w.id" [selected]="w.id === row.wardrobeId">
                        {{ w.label }}{{ first ? ' (default)' : '' }}
                      </option>
                    }
                  </select>
                } @else {
                  <span class="dr-hint"
                    >No wardrobes on the sheet yet.
                    <button type="button" class="dr-link" (click)="openInLibrary(row.character.id)">
                      Add one in the library
                    </button></span
                  >
                }
                @if (panel() && row.wardrobeSource === 'panel') {
                  <button
                    type="button"
                    class="dr-link self-start"
                    (click)="promote(row.character.id, 'wardrobeId')"
                  >
                    Use this wardrobe in all panels
                  </button>
                }
                @if (!panel() && row.wardrobePanels.length > 0) {
                  <div class="dr-note">
                    <span class="flex-1"
                      >{{ list(row.wardrobePanels) }} override{{
                        row.wardrobePanels.length === 1 ? 's' : ''
                      }}
                      this</span
                    >
                    <button
                      type="button"
                      class="dr-link is-quiet"
                      (click)="store.clearPanelLooks(row.character.id, 'wardrobeId')"
                    >
                      use this everywhere
                    </button>
                  </div>
                }
              </div>
              <app-change-layers
                title="Wardrobe changes"
                baseTag="Outfit"
                [base]="row.outfit"
                [sceneText]="panel() ? row.scene.wardrobeChange : ''"
                [hereTag]="hereTag()"
                [text]="row.own.wardrobeChange"
                [placeholder]="placeholder('e.g. hood up, soaked through')"
                [hint]="
                  panel()
                    ? ''
                    : 'Applies to every panel in this scene. For a different outfit, pick another wardrobe.'
                "
                [canPromote]="!!panel() && !!row.own.wardrobeChange"
                [panelNote]="note(row.wardrobeChangePanels, 'wardrobe')"
                (edit)="set(row.character.id, 'wardrobeChange', $event)"
                (clear)="set(row.character.id, 'wardrobeChange', '')"
                (promote)="promote(row.character.id, 'wardrobeChange')"
                (clearPanels)="store.clearPanelLooks(row.character.id, 'wardrobeChange')"
              />
              <app-change-layers
                title="Appearance changes"
                [base]="row.character.description ?? ''"
                [sceneText]="panel() ? row.scene.appearanceChange : ''"
                [hereTag]="hereTag()"
                [text]="row.own.appearanceChange"
                [placeholder]="placeholder('e.g. bruise on left cheek')"
                [hint]="
                  panel()
                    ? 'Adds to the library description and scene changes above. Never replaces them.'
                    : 'Applies to every panel in this scene, on top of the library description.'
                "
                [canPromote]="!!panel() && !!row.own.appearanceChange"
                [panelNote]="note(row.appearancePanels, 'appearance')"
                (edit)="set(row.character.id, 'appearanceChange', $event)"
                (clear)="set(row.character.id, 'appearanceChange', '')"
                (promote)="promote(row.character.id, 'appearanceChange')"
                (clearPanels)="store.clearPanelLooks(row.character.id, 'appearanceChange')"
              />
            </div>
          }
        </div>
      } @empty {
        <p class="dr-hint">Nobody speaks or is mentioned in this scene yet.</p>
      }
    </div>

    <div class="dr-field">
      <div class="dr-field-head">
        <span class="dr-kicker">Props</span>
        <span class="flex-1"></span>
        @if (panel(); as panel) {
          @if (panel.props.length > 0) {
            <span class="dr-override"
              >override ·
              <button
                type="button"
                class="dr-link is-quiet"
                (click)="store.resetProps(panel.firstElementId)"
              >
                reset
              </button></span
            >
          } @else {
            <span class="dr-inherited">from scene</span>
          }
        }
      </div>
      <div class="dr-pills">
        @for (prop of props(); track prop.id) {
          <button
            type="button"
            class="dr-prop"
            [class.is-on]="prop.on"
            [title]="prop.state ? 'In this scene: ' + prop.state : ''"
            (click)="store.setInView(prop.id, !prop.on, panelKey())"
          >
            @if (prop.on) {
              {{ prop.name }} <span aria-hidden="true">×</span>
            } @else {
              + {{ prop.name }}
            }
          </button>
        } @empty {
          <span class="dr-hint">No props are mentioned in this scene.</span>
        }
      </div>
    </div>

    <div class="dr-tab-foot">
      <span class="flex-1">Story-wide defaults live in the library on the left.</span>
      <button
        type="button"
        class="dr-link"
        [disabled]="store.isDirecting({ kind: 'continuity' })"
        (click)="store.direct({ kind: 'continuity' })"
      >
        Suggest looks
      </button>
    </div>
  `,
})
export class InspectorCastTab {
  protected readonly store = inject(SceneEditorStore);

  private readonly openIds = signal<ReadonlySet<string>>(new Set());

  protected readonly panel = computed(() =>
    this.store.inspectorScope() === 'panel' ? (this.store.selectedPanel()?.panel ?? null) : null,
  );
  protected readonly panelKey = computed(() => this.panel()?.firstElementId ?? null);
  protected readonly here = computed<LookSource>(() => (this.panel() ? 'panel' : 'scene'));
  protected readonly hereTag = computed(() =>
    this.panel() ? `Panel ${this.store.selectedPanelNumber()}` : 'Scene',
  );
  protected readonly castOverridden = computed(
    () => this.panel()?.placements.some((p) => p.onScreen !== null) ?? false,
  );

  protected readonly rows = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    const panel = this.panel();
    const opened = this.openIds();
    return this.store.cast().map((character) => {
      const on = onScreenOf(direction, panel, character.id).value;
      const { wardrobeId, source } = wardrobeOf(direction, panel, character);
      const wardrobe = character.wardrobes.find((w) => w.id === wardrobeId) ?? null;
      const scene = this.store.appearanceByCharacter().get(character.id);
      const placement = panel?.placements.find((p) => p.characterId === character.id);
      const own = panel
        ? {
            wardrobeChange: placement?.wardrobeChange ?? '',
            appearanceChange: placement?.appearanceChange ?? '',
          }
        : {
            wardrobeChange: scene?.wardrobeChange ?? '',
            appearanceChange: scene?.appearanceChange ?? '',
          };
      const sceneLayer = {
        wardrobeChange: scene?.wardrobeChange ?? '',
        appearanceChange: scene?.appearanceChange ?? '',
      };
      const variation = joinChanges(sceneLayer.wardrobeChange, panel ? own.wardrobeChange : '');
      return {
        character,
        on,
        open: opened.has(character.id),
        wardrobeId,
        wardrobeSource: source,
        outfit: wardrobe ? [wardrobe.label, wardrobe.description].filter(Boolean).join(': ') : '',
        scene: sceneLayer,
        own,
        summary: on ? [wardrobe?.label, variation].filter(Boolean).join(' · ') : 'Off screen',
        edits: [source === this.here(), own.wardrobeChange, own.appearanceChange].filter(Boolean)
          .length,
        wardrobePanels: this.store.panelsWithLook(character.id, 'wardrobeId'),
        wardrobeChangePanels: this.store.panelsWithLook(character.id, 'wardrobeChange'),
        appearancePanels: this.store.panelsWithLook(character.id, 'appearanceChange'),
      };
    });
  });

  protected readonly props = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    return this.store.propsInScene().map((prop) => ({
      id: prop.id,
      name: prop.name,
      on: inViewOf(direction, this.panel(), prop.id).value,
      state: this.store.propStateByProp().get(prop.id)?.state.join(', ') ?? '',
    }));
  });

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected tagClass(source: LookSource): string {
    return source === 'panel' ? 'here' : source;
  }

  protected list(numbers: readonly number[]): string {
    return panelList(numbers);
  }

  protected note(numbers: readonly number[], what: string): string {
    if (this.panel() || numbers.length === 0) {
      return '';
    }
    return `${panelList(numbers)} add${numbers.length === 1 ? 's' : ''} further ${what} changes`;
  }

  protected placeholder(example: string): string {
    return this.panel()
      ? `What changes in panel ${this.store.selectedPanelNumber()}? ${example}`
      : `What changes in this scene? ${example}`;
  }

  protected toggleOpen(characterId: string, on: boolean): void {
    if (!on) {
      return;
    }
    this.openIds.update((ids) => {
      const next = new Set(ids);
      if (!next.delete(characterId)) {
        next.add(characterId);
      }
      return next;
    });
  }

  protected set(characterId: string, field: LookField, value: string | null): void {
    this.store.setLook(characterId, field, value || null, this.panelKey());
  }

  protected promote(characterId: string, field: LookField): void {
    const panelKey = this.panelKey();
    if (panelKey) {
      this.store.promoteLook(characterId, field, panelKey);
    }
  }

  protected openInLibrary(characterId: string): void {
    this.store.directorLibraryOpen.set(true);
    this.store.directorLibraryTab.set('cast');
    this.store.directorLibraryEntry.set(characterId);
  }
}
