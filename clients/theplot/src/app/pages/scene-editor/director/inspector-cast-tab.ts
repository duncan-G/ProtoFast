import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  changesOf,
  extraOnScreenOf,
  heldIn,
  inViewOf,
  joinChanges,
  lookAt,
  LookAt,
  LookSource,
  onScreenOf,
  wardrobeOf,
} from '../../../direction/inheritance';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { Character } from '../../../stories/model/character';
import { Avatar } from '../avatar';
import { initials } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { ChangeLayers } from './change-layers';
import { PicturePreview } from './picture-preview';

type LookField = 'wardrobeId' | 'wardrobeChange' | 'appearanceChange';

/** "Frame 3" or "Frames 2, 5". */
function frameList(numbers: readonly number[]): string {
  return (numbers.length === 1 ? 'Frame ' : 'Frames ') + numbers.join(', ');
}

/** Nothing on top of the sheet: the library's picture shows it. */
function isSheet(look: LookAt, character: Character): boolean {
  return (
    (look.wardrobeId === null || look.wardrobeId === character.wardrobes[0]?.id) &&
    !look.wardrobeChange.trim() &&
    !look.appearanceChange.trim() &&
    look.holding.length === 0
  );
}

/**
 * Who is on screen and what they look like, for the selected frame or the whole scene. Looks come
 * from the library, open the scene, change on a line (picking something up, handing it over) and
 * hold until the next change; a frame can add to the look for itself alone.
 */
@Component({
  selector: 'app-inspector-cast-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Avatar, ChangeLayers, PicturePreview],
  host: { class: 'dr-tab-body' },
  template: `
    <div class="dr-field">
      <div class="dr-field-head">
        <span class="dr-kicker">On screen</span>
        <span class="flex-1"></span>
        @if (frame(); as frame) {
          @if (castOverridden()) {
            <span class="dr-override"
              >override ·
              <button
                type="button"
                class="dr-link is-quiet"
                (click)="store.resetCast(frame.firstElementId)"
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
        >→<span class="dr-layer-tag is-here">Frame</span>
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
                $event.stopPropagation(); store.setOnScreen(row.character.id, !row.on, frameKey())
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
                  <span class="dr-kicker">Carrying</span>
                  <span class="flex-1"></span>
                  <span class="dr-layer-tag" [class]="row.changeHere ? 'is-here' : 'is-scene'">{{
                    row.lookFrom
                  }}</span>
                  @if (row.changeHere; as change) {
                    <button
                      type="button"
                      class="dr-link is-quiet"
                      (click)="store.removeLookChange(row.character.id, change.elementId)"
                    >
                      reset
                    </button>
                  }
                </div>
                <div class="dr-pills">
                  @for (prop of row.carryable; track prop.id) {
                    <button
                      type="button"
                      class="dr-prop"
                      [class.is-on]="prop.on"
                      (click)="toggleHolding(row.character.id, row.holding, prop.id)"
                    >
                      @if (prop.on) {
                        {{ prop.name }} <span aria-hidden="true">×</span>
                      } @else {
                        + {{ prop.name }}
                      }
                    </button>
                  } @empty {
                    <span class="dr-hint">No props in this scene to carry.</span>
                  }
                </div>
                <span class="dr-hint">{{ row.carryHint }}</span>
              </div>
              @if (!frame() && row.changes.length > 0) {
                <div class="dr-field">
                  <span class="dr-kicker">During the scene</span>
                  @for (change of row.changes; track change.elementId) {
                    <div class="dr-note">
                      <span class="flex-1">{{ change.text }}</span>
                      <button
                        type="button"
                        class="dr-link is-quiet"
                        (click)="store.showRow(change.elementId)"
                      >
                        show
                      </button>
                      <button
                        type="button"
                        class="dr-link is-quiet"
                        (click)="store.removeLookChange(row.character.id, change.elementId)"
                      >
                        remove
                      </button>
                    </div>
                  }
                </div>
              }
              @if (!row.sheet) {
                <div class="dr-library-picture">
                  @if (row.image; as image) {
                    <app-picture-preview
                      [url]="image.url"
                      [alt]="'Picture of ' + row.character.name + ' in this look'"
                    />
                  } @else {
                    <div class="dr-picture">
                      {{ row.drawing ? 'Drawing…' : 'No image' }}
                    </div>
                  }
                  <div class="dr-library-picture-side">
                    <span class="dr-kicker">Look picture</span>
                    <span class="dr-hint" [class.is-warn]="row.image?.stale">{{
                      row.image?.stale
                        ? 'Out of date: the look changed since.'
                        : 'Frames in this look draw ' + row.character.name + ' from it.'
                    }}</span>
                    <button
                      type="button"
                      class="dr-link self-start"
                      [class.is-working]="row.drawing"
                      [disabled]="row.drawing"
                      (click)="
                        store.generateLookImage(
                          row.character.id,
                          row.changeId,
                          !!row.image && !row.image.stale
                        )
                      "
                    >
                      {{
                        row.drawing
                          ? 'Drawing…'
                          : row.image
                            ? row.image.stale
                              ? 'Update'
                              : 'Regenerate'
                            : 'Generate'
                      }}
                    </button>
                  </div>
                </div>
              }
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
                @if (frame() && row.wardrobeSource === 'frame') {
                  <button
                    type="button"
                    class="dr-link self-start"
                    (click)="promote(row.character.id, 'wardrobeId')"
                  >
                    Use this wardrobe in all frames
                  </button>
                }
                @if (!frame() && row.wardrobeFrames.length > 0) {
                  <div class="dr-note">
                    <span class="flex-1"
                      >{{ list(row.wardrobeFrames) }} override{{
                        row.wardrobeFrames.length === 1 ? 's' : ''
                      }}
                      this</span
                    >
                    <button
                      type="button"
                      class="dr-link is-quiet"
                      (click)="store.clearFrameLooks(row.character.id, 'wardrobeId')"
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
                [sceneText]="frame() ? row.scene.wardrobeChange : ''"
                [hereTag]="hereTag()"
                [text]="row.own.wardrobeChange"
                [placeholder]="placeholder('e.g. hood up, soaked through')"
                [hint]="
                  frame()
                    ? ''
                    : 'Applies to every frame in this scene. For a different outfit, pick another wardrobe.'
                "
                [canPromote]="!!frame() && !!row.own.wardrobeChange"
                [frameNote]="note(row.wardrobeChangeFrames, 'wardrobe')"
                (edit)="set(row.character.id, 'wardrobeChange', $event)"
                (clear)="set(row.character.id, 'wardrobeChange', '')"
                (promote)="promote(row.character.id, 'wardrobeChange')"
                (clearFrames)="store.clearFrameLooks(row.character.id, 'wardrobeChange')"
              />
              <app-change-layers
                title="Appearance changes"
                [base]="row.character.description ?? ''"
                [sceneText]="frame() ? row.scene.appearanceChange : ''"
                [hereTag]="hereTag()"
                [text]="row.own.appearanceChange"
                [placeholder]="placeholder('e.g. bruise on left cheek')"
                [hint]="
                  frame()
                    ? 'Adds to the library description and scene changes above. Never replaces them.'
                    : 'Applies to every frame in this scene, on top of the library description.'
                "
                [canPromote]="!!frame() && !!row.own.appearanceChange"
                [frameNote]="note(row.appearanceFrames, 'appearance')"
                (edit)="set(row.character.id, 'appearanceChange', $event)"
                (clear)="set(row.character.id, 'appearanceChange', '')"
                (promote)="promote(row.character.id, 'appearanceChange')"
                (clearFrames)="store.clearFrameLooks(row.character.id, 'appearanceChange')"
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
        <span class="dr-kicker">Extras</span>
        <span class="flex-1"></span>
        @if (frame(); as frame) {
          @if (frame.extras.length > 0) {
            <span class="dr-override"
              >override ·
              <button
                type="button"
                class="dr-link is-quiet"
                (click)="store.resetExtras(frame.firstElementId)"
              >
                reset
              </button></span
            >
          } @else {
            <span class="dr-inherited">from scene</span>
          }
        } @else {
          <button
            type="button"
            class="dr-link"
            title="Have the model find who else the scene shows; extras the library lacks are added to it"
            [class.is-working]="store.isDirecting({ kind: 'extras' })"
            [disabled]="!store.canDirect({ kind: 'extras' })"
            (click)="store.direct({ kind: 'extras' })"
          >
            {{ store.isDirecting({ kind: 'extras' }) ? 'Finding…' : 'Find extras' }}
          </button>
        }
      </div>
      <div class="dr-pills">
        @for (extra of extras(); track extra.id) {
          <button
            type="button"
            class="dr-prop"
            [class.is-on]="extra.on"
            [title]="extra.description"
            (click)="store.setExtraOnScreen(extra.id, !extra.on, frameKey())"
          >
            @if (extra.on) {
              {{ extra.name }} <span aria-hidden="true">×</span>
            } @else {
              + {{ extra.name }}
            }
          </button>
        } @empty {
          <span class="dr-hint">No extras in this scene yet.</span>
        }
      </div>
      @if (!frame()) {
        <select class="dr-input" aria-label="Add extras to the scene" (change)="addExtra($event)">
          <option value="" selected>Add extras from the library…</option>
          @for (extra of otherExtras(); track extra.id) {
            <option [value]="extra.id">{{ extra.name }}</option>
          }
          <option value="new">New extras…</option>
        </select>
      }
    </div>

    <div class="dr-field">
      <div class="dr-field-head">
        <span class="dr-kicker">Props</span>
        <span class="flex-1"></span>
        @if (frame(); as frame) {
          @if (frame.props.length > 0) {
            <span class="dr-override"
              >override ·
              <button
                type="button"
                class="dr-link is-quiet"
                (click)="store.resetProps(frame.firstElementId)"
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
            (click)="store.setInView(prop.id, !prop.on, frameKey())"
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
        [class.is-working]="store.isDirecting({ kind: 'continuity' })"
        [disabled]="!store.canDirect({ kind: 'continuity' })"
        (click)="store.direct({ kind: 'continuity' })"
      >
        {{ store.isDirecting({ kind: 'continuity' }) ? 'Suggesting…' : 'Suggest looks' }}
      </button>
    </div>
  `,
})
export class InspectorCastTab {
  protected readonly store = inject(SceneEditorStore);

  private readonly openIds = signal<ReadonlySet<string>>(new Set());

  protected readonly frame = computed(() =>
    this.store.inspectorScope() === 'frame' ? (this.store.selectedFrame()?.frame ?? null) : null,
  );
  protected readonly frameKey = computed(() => this.frame()?.firstElementId ?? null);
  protected readonly here = computed<LookSource>(() => (this.frame() ? 'frame' : 'scene'));
  protected readonly hereTag = computed(() =>
    this.frame() ? `Frame ${this.store.selectedFrameNumber()}` : 'Scene',
  );
  protected readonly castOverridden = computed(
    () => this.frame()?.placements.some((p) => p.onScreen !== null) ?? false,
  );

  protected readonly rows = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    const frame = this.frame();
    const span = frame ? this.store.selectedFrame() : null;
    const order = this.store.elementOrder();
    const opened = this.openIds();
    const props = this.store.propsInScene();
    const spans = this.store.frameSpans();
    const frameOf = (elementId: string) => {
      const at = order.get(elementId) ?? -1;
      return spans.findIndex((s) => s.first <= at && at <= s.last) + 1;
    };
    const propName = (id: string) => props.find((p) => p.id === id)?.name ?? 'something';
    return this.store.cast().map((character) => {
      const on = onScreenOf(direction, frame, character.id).value;
      const look = lookAt(direction, character.id, order, span?.last ?? null);
      const { wardrobeId, source } = wardrobeOf(direction, frame, character, look);
      const wardrobe = character.wardrobes.find((w) => w.id === wardrobeId) ?? null;
      const scene = this.store.appearanceByCharacter().get(character.id);
      const placement = frame?.placements.find((p) => p.characterId === character.id);
      const own = frame
        ? {
            wardrobeChange: placement?.wardrobeChange ?? '',
            appearanceChange: placement?.appearanceChange ?? '',
          }
        : {
            wardrobeChange: scene?.wardrobeChange ?? '',
            appearanceChange: scene?.appearanceChange ?? '',
          };
      const sceneLayer = {
        wardrobeChange: look.wardrobeChange,
        appearanceChange: look.appearanceChange,
      };
      const variation = joinChanges(sceneLayer.wardrobeChange, frame ? own.wardrobeChange : '');
      const changeAt = look.change ? order.get(look.change.elementId) : undefined;
      const changeHere =
        span && look.change && changeAt !== undefined && changeAt >= span.first
          ? look.change
          : null;
      const carrying = look.holding.map(propName);
      return {
        holding: look.holding,
        carryable: props.map((p) => ({
          id: p.id,
          name: p.name,
          on: look.holding.includes(p.id),
        })),
        lookFrom: !frame
          ? 'Opens'
          : changeHere
            ? 'From here'
            : look.change
              ? `From frame ${frameOf(look.change.elementId)}`
              : 'Scene',
        changeHere,
        changeId: look.change?.elementId ?? null,
        carryHint: !frame
          ? 'As the scene opens. It stays with them, into the next scenes too, until a line has them let it go.'
          : changeHere
            ? `Changes here${changeHere.reason ? ': ' + changeHere.reason : ''}. Holds until the next change.`
            : 'Picking something up or handing it over here holds from this frame on.',
        changes: changesOf(direction, character.id, order).map((c) => ({
          elementId: c.elementId,
          text: `Frame ${frameOf(c.elementId)}: ${c.reason ?? 'look changes'} · ${
            c.holding.length > 0 ? 'carrying ' + c.holding.map(propName).join(', ') : 'empty-handed'
          }`,
        })),
        sheet: isSheet(look, character),
        image: look.image,
        drawing: this.store.picturing().has(`look:${character.id}:${look.change?.elementId ?? ''}`),
        summary: on
          ? [
              wardrobe?.label,
              variation,
              carrying.length > 0 ? 'carrying ' + carrying.join(', ') : '',
            ]
              .filter(Boolean)
              .join(' · ')
          : 'Off screen',
        character,
        on,
        open: opened.has(character.id),
        wardrobeId,
        wardrobeSource: source,
        outfit: wardrobe ? [wardrobe.label, wardrobe.description].filter(Boolean).join(': ') : '',
        scene: sceneLayer,
        own,
        edits: [source === this.here(), own.wardrobeChange, own.appearanceChange].filter(Boolean)
          .length,
        wardrobeFrames: this.store.framesWithLook(character.id, 'wardrobeId'),
        wardrobeChangeFrames: this.store.framesWithLook(character.id, 'wardrobeChange'),
        appearanceFrames: this.store.framesWithLook(character.id, 'appearanceChange'),
      };
    });
  });

  protected readonly props = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    const span = this.frame() ? this.store.selectedFrame() : null;
    const held = span ? heldIn(direction, span, this.store.elementOrder()) : undefined;
    return this.store.propsInScene().map((prop) => ({
      id: prop.id,
      name: prop.name,
      on: inViewOf(direction, this.frame(), prop.id, held).value,
      state: this.store.propStateByProp().get(prop.id)?.state.join(', ') ?? '',
    }));
  });

  protected readonly extras = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    return this.store.extrasInScene().map((extra) => ({
      id: extra.id,
      name: extra.name,
      description: extra.description ?? '',
      on: extraOnScreenOf(direction, this.frame(), extra.id).value,
    }));
  });

  protected readonly otherExtras = computed(() => {
    const inScene = new Set(this.store.extrasInScene().map((e) => e.id));
    return this.store.extras().filter((e) => !inScene.has(e.id));
  });

  protected addExtra(event: Event): void {
    const select = event.target as HTMLSelectElement;
    const id = select.value;
    select.value = '';
    if (id === 'new') {
      this.store.libraryCollapsed.set(false);
      this.store.directorLibraryTab.set('extras');
    } else if (id) {
      this.store.addExtraToScene(id);
    }
  }

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected tagClass(source: LookSource): string {
    return source === 'frame' ? 'here' : source;
  }

  protected list(numbers: readonly number[]): string {
    return frameList(numbers);
  }

  protected note(numbers: readonly number[], what: string): string {
    if (this.frame() || numbers.length === 0) {
      return '';
    }
    return `${frameList(numbers)} add${numbers.length === 1 ? 's' : ''} further ${what} changes`;
  }

  protected placeholder(example: string): string {
    return this.frame()
      ? `What changes in frame ${this.store.selectedFrameNumber()}? ${example}`
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

  protected toggleHolding(characterId: string, holding: readonly string[], propId: string): void {
    const next = holding.includes(propId)
      ? holding.filter((id) => id !== propId)
      : [...holding, propId];
    this.store.setHolding(characterId, next, this.frameKey());
  }

  protected set(characterId: string, field: LookField, value: string | null): void {
    this.store.setLook(characterId, field, value || null, this.frameKey());
  }

  protected promote(characterId: string, field: LookField): void {
    const frameKey = this.frameKey();
    if (frameKey) {
      this.store.promoteLook(characterId, field, frameKey);
    }
  }

  protected openInLibrary(characterId: string): void {
    this.store.libraryCollapsed.set(false);
    this.store.directorLibraryTab.set('cast');
    this.store.directorLibraryEntry.set(characterId);
  }
}
