import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { hue, KIND_HINT, KIND_LABEL, KIND_SWATCH, partName } from './labels';
import { KIND_ORDER, type StageTile } from './world';
import { WorldCharacterChip } from './world-character-chip';
import { WorldObjectChip } from './world-object-chip';
import { WorldPlaceLink } from './world-place-link';
import { WorldSession } from './world-session';

@Component({
  selector: 'app-world-stage',
  imports: [NgTemplateOutlet, WorldCharacterChip, WorldObjectChip, WorldPlaceLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (session.stage(); as stage) {
      <div
        class="rounded-2xl border-2 p-4"
        [style.borderColor]="tint(0.86, 0.06)"
        [style.background]="tint(0.985, 0.012)"
      >
        <header class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <span class="h-3 w-3 rounded-full" [style.background]="tint(0.72, 0.12)"></span>
          <button
            type="button"
            class="text-lg font-semibold text-gray-900 hover:underline"
            title="This place's history"
            (click)="session.inspect({ kind: 'location', id: stage.location.id })"
          >
            {{ stage.location.name }}
          </button>
          <span class="text-xs text-gray-500">{{
            stage.location.exterior ? 'Exterior' : 'Interior'
          }}</span>
          @if (stage.inside) {
            <span class="text-xs text-gray-500"
              >inside <app-world-place [place]="{ locationId: stage.inside.id, partId: '' }"
            /></span>
          }
          <span class="ml-auto text-xs text-gray-500">
            @if (here()) {
              The scene is here
            } @else if (visited()) {
              The scene is elsewhere; this is how the place was left
            } @else {
              The story has not been here yet
            }
          </span>
          @if (session.pinned()) {
            <button
              type="button"
              class="rounded-lg border border-gray-300 bg-white px-2 py-0.5 text-xs text-gray-700 hover:bg-gray-50"
              (click)="session.focus(null)"
            >
              Back to the scene
            </button>
          }
        </header>

        @if (stage.location.description) {
          <div class="mt-2 max-w-3xl text-sm leading-6 text-gray-700">
            <p>{{ stage.location.description }}</p>
            @if (stage.location.contents) {
              <details class="mt-1">
                <summary class="cursor-pointer text-xs text-indigo-600">What is in it</summary>
                <p class="mt-1 whitespace-pre-line">{{ stage.location.contents }}</p>
              </details>
            }
          </div>
        } @else {
          <p class="mt-2 text-xs text-gray-400">Not described yet; the next build describes it.</p>
        }

        @if (stage.whole.action || !isEmpty(stage.whole)) {
          <div class="mt-3">
            <ng-container
              [ngTemplateOutlet]="tileView"
              [ngTemplateOutletContext]="{ $implicit: stage.whole, whole: true }"
            />
          </div>
        }

        @if (stage.tiles.length) {
          <div class="mt-3 flex flex-wrap items-stretch gap-2">
            @for (tile of stage.tiles; track tile.part!.id; let last = $last) {
              <ng-container
                [ngTemplateOutlet]="tileView"
                [ngTemplateOutletContext]="{ $implicit: tile, whole: false }"
              />
              @if (!last && ordered(tile, stage.tiles[$index + 1])) {
                <span class="self-center text-gray-300" aria-hidden="true">›</span>
              }
            }
          </div>
        } @else {
          <p class="mt-3 text-xs text-gray-500">No beat has named a part of this place.</p>
        }

        @if (stage.departures.length) {
          <div class="mt-4 flex flex-wrap items-center gap-2 text-xs text-gray-600">
            <span class="font-medium text-gray-500">Just left:</span>
            @for (departure of stage.departures; track departure.fact.key) {
              <span class="inline-flex items-center gap-1">
                @if (departure.object) {
                  <app-world-object [id]="departure.object.id" [gone]="true" />
                } @else if (departure.character) {
                  <app-world-character [id]="departure.character.id" [gone]="true" />
                }
                @if (departure.to) {
                  → <app-world-place [place]="departure.to" />
                }
              </span>
            }
          </div>
        }

        @if (stage.within.length) {
          <div class="mt-4 flex flex-wrap items-center gap-2 text-xs text-gray-600">
            <span class="font-medium text-gray-500">Places inside:</span>
            @for (inner of stage.within; track inner.id) {
              <app-world-place [place]="{ locationId: inner.id, partId: '' }" />
            }
          </div>
        }
      </div>

      <div class="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-gray-500">
        @for (kind of kinds; track kind) {
          <span class="inline-flex items-center gap-1" [title]="kindHint[kind]">
            <span class="h-2.5 w-2.5 rounded-sm" [class]="kindSwatch[kind]"></span>
            {{ kindLabel[kind] }}
          </span>
        }
        <span class="inline-flex items-center gap-1">
          <span class="h-2.5 w-4 rounded-sm outline-2 outline-offset-1 outline-emerald-500"></span>
          changed at this beat
        </span>
        <span class="inline-flex items-center gap-1">
          <span class="h-2.5 w-4 rounded-sm border border-dashed border-gray-400"></span>
          not mentioned in this scene yet
        </span>
        <span class="inline-flex items-center gap-1">
          <span class="h-2.5 w-4 rounded-sm ring-2 ring-indigo-500"></span>
          where the beat happens
        </span>
        <span class="inline-flex items-center gap-1">
          <span class="rounded-full bg-gray-800 px-1.5 text-[10px] font-medium text-white"
            >camera</span
          >
          where the camera stands
        </span>
      </div>
    } @else {
      <p
        class="rounded-2xl border border-dashed border-gray-300 bg-white p-8 text-center text-sm text-gray-500"
      >
        This scene has no location. Pick a place on the map to see it.
      </p>
    }

    <ng-template #tileView let-tile let-whole="whole">
      <div
        class="min-w-[10rem] rounded-xl border bg-white/90 p-2.5 transition"
        [class.flex-1]="!whole"
        [class.border-gray-200]="!tile.action"
        [class.border-transparent]="tile.action"
        [class.ring-2]="tile.action"
        [class.ring-indigo-500]="tile.action"
        [class.border-dashed]="whole && !tile.action"
      >
        <div class="flex items-center gap-1.5">
          @if (whole) {
            <span class="text-xs text-gray-500"
              >Anywhere in {{ session.stage()?.location?.name }}</span
            >
          } @else {
            <button
              type="button"
              class="text-left text-xs font-semibold text-gray-800 hover:underline"
              [class.italic]="!tile.part.name"
              [title]="tile.part.description || 'This part’s history'"
              (click)="session.inspect({ kind: 'part', id: tile.part.id })"
            >
              {{ partName(tile.part) }}
            </button>
          }
          @if (tile.action || tile.camera) {
            <span class="ml-auto flex gap-1">
              @if (tile.camera) {
                <span
                  class="rounded-full bg-gray-800 px-1.5 text-[10px] font-medium text-white"
                  title="The camera stands here"
                  >camera</span
                >
              }
              @if (tile.action) {
                <span class="rounded-full bg-indigo-600 px-1.5 text-[10px] font-medium text-white"
                  >beat</span
                >
              }
            </span>
          }
        </div>
        @if (tile.camera?.other; as toward) {
          <p class="mt-1 text-[11px] text-gray-600">
            <span class="text-gray-400">camera looks toward</span>
            <app-world-place [place]="toward" />
          </p>
        }

        @if (tile.objects.length) {
          <div class="mt-1.5 flex flex-wrap gap-1">
            @for (item of tile.objects; track item.entity.id) {
              <app-world-object
                [id]="item.entity.id"
                [fact]="item.fact"
                [states]="item.states"
                [changed]="item.changed || hasChangedState(item.states)"
                [carried]="item.carried"
              />
            }
          </div>
        }
        @if (tile.characters.length) {
          <div class="mt-1.5 flex flex-wrap gap-1">
            @for (item of tile.characters; track item.entity.id) {
              <app-world-character
                [id]="item.entity.id"
                [fact]="item.fact"
                [changed]="item.changed"
                [carried]="item.carried"
              />
            }
          </div>
        }
        @if (tile.ways.length) {
          <ul class="mt-1.5 space-y-0.5 text-[11px] text-gray-600">
            @for (way of tile.ways; track way.fact.key) {
              <li class="rounded px-1" [class.bg-emerald-50]="way.changed">
                <span class="text-gray-400">⇄</span>
                <app-world-place [place]="way.to" />
                @if (way.via) {
                  <span class="text-gray-400"> through {{ way.via.name }}</span>
                }
              </li>
            }
          </ul>
        }
        @if (tile.children.length) {
          <div class="mt-2 flex flex-wrap gap-2">
            @for (child of tile.children; track child.part.id) {
              <ng-container
                [ngTemplateOutlet]="tileView"
                [ngTemplateOutletContext]="{ $implicit: child, whole: false }"
              />
            }
          </div>
        }
        @if (!whole && isEmpty(tile)) {
          <p class="mt-1 text-[11px] text-gray-400 italic">nothing known here</p>
        }
      </div>
    </ng-template>
  `,
})
export class WorldStage {
  protected readonly session = inject(WorldSession);
  protected readonly partName = partName;
  protected readonly kinds = KIND_ORDER;
  protected readonly kindLabel = KIND_LABEL;
  protected readonly kindHint = KIND_HINT;
  protected readonly kindSwatch = KIND_SWATCH;

  protected readonly here = computed(
    () => this.session.stage()?.location.id === this.session.actionLocation(),
  );

  protected readonly visited = computed(() => {
    const world = this.session.world();
    const id = this.session.stage()?.location.id ?? '';
    const first = world?.firstVisit.get(id);
    return first !== undefined && first <= this.session.moment();
  });

  protected tint(lightness: number, chroma: number): string {
    return hue(this.session.stage()?.location.hue ?? 0, lightness, chroma);
  }

  protected ordered(a: StageTile, b: StageTile | undefined): boolean {
    return a.part?.position !== undefined && b?.part?.position !== undefined;
  }

  protected isEmpty(tile: StageTile): boolean {
    return (
      !tile.objects.length &&
      !tile.characters.length &&
      !tile.ways.length &&
      !tile.children.length &&
      !tile.camera
    );
  }

  protected hasChangedState(states: { changed: boolean }[]): boolean {
    return states.some((s) => s.changed);
  }
}
