import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { usd } from '../engine/format';
import { beatRuns, placeFrom, placeName, viewLabel } from './labels';
import { viewKey } from './world';
import { WorldPictures } from './world-pictures';
import { WorldSession } from './world-session';

@Component({
  selector: 'app-world-views',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (session.world(); as world) {
      @let views = session.views();
      <h3 class="text-xs font-semibold tracking-wide text-gray-500 uppercase">
        {{ views.length === 1 ? 'View' : 'Views' }}
        <span class="ml-1 font-normal text-gray-400 normal-case"
          >{{ views.length }} in this scene</span
        >
      </h3>
      @if (session.view(); as view) {
        @let picture = pictures.pictureOf(view);
        @let state = pictures.stateOf(view);
        @let src = picture ? pictures.urlOf(picture) : undefined;
        <figure class="mt-1.5">
          <div
            class="relative aspect-[3/2] overflow-hidden rounded-lg bg-gray-100 ring-1 ring-gray-200"
          >
            @if (src) {
              <a [href]="src" target="_blank" rel="noopener" title="Open full size">
                <img
                  class="h-full w-full object-cover"
                  [src]="src"
                  [alt]="'The place from ' + viewLabel(world, view)"
                />
              </a>
            } @else {
              <p
                class="flex h-full items-center justify-center p-4 text-center text-xs text-gray-500"
              >
                @if (state.picturing) {
                  Muse is picturing the place…
                } @else if (picture) {
                  Loading the picture…
                } @else {
                  Not pictured yet
                }
              </p>
            }
            @if (src && state.picturing) {
              <p
                class="absolute inset-x-0 bottom-0 bg-white/85 px-2 py-1 text-center text-xs text-gray-700"
              >
                Picturing again…
              </p>
            }
          </div>
          <figcaption class="mt-1.5 text-xs text-gray-700">
            <span class="text-gray-400">From</span> {{ placeName(world, view.camera) }}
            @if (view.toward) {
              <span class="text-gray-400">looking toward</span>
              {{ placeFrom(world, view.toward, view.camera?.locationId ?? '') }}
            }
            <span class="block text-gray-400"
              >Beats {{ beatRuns(world, view.moments) }}
              @if (view.state) {
                · after the place changed
              }
            </span>
          </figcaption>
        </figure>

        <div class="mt-2 flex flex-wrap items-center gap-2">
          <button
            type="button"
            class="rounded-lg border border-gray-300 bg-white px-2.5 py-1 text-xs text-gray-700 hover:bg-gray-50 disabled:opacity-50"
            [disabled]="state.picturing"
            title="Muse pictures the place from this camera, with no one in it, drawing on this place's other pictures"
            (click)="pictures.picture(view)"
          >
            @if (state.picturing) {
              Picturing…
            } @else {
              {{ picture ? 'Picture again' : 'Picture this view' }}
            }
          </button>
          @if (picture) {
            <span class="text-[11px] text-gray-500">Muse · {{ usd(picture.costUsdMicros) }}</span>
          }
        </div>
        @if (state.error) {
          <p
            class="mt-2 rounded-lg border border-red-200 bg-red-50 px-2 py-1.5 text-xs text-red-800"
          >
            {{ state.error }}
          </p>
        }
        @if (picture) {
          <details class="mt-1 text-[11px]">
            <summary class="cursor-pointer text-indigo-600">Prompt</summary>
            <pre class="mt-1 whitespace-pre-wrap text-gray-700">{{ picture.prompt }}</pre>
            @if (picture.notes) {
              <p class="mt-2 font-medium text-gray-600">Muse's notes</p>
              <p class="whitespace-pre-wrap text-gray-700">{{ picture.notes }}</p>
            }
          </details>
        }

        @if (views.length > 1) {
          <ol class="mt-3 grid grid-cols-3 gap-2">
            @for (other of views; track key(other)) {
              @let thumb = pictures.pictureOf(other);
              @let thumbSrc = thumb ? pictures.urlOf(thumb) : undefined;
              <li>
                <button
                  type="button"
                  class="block w-full text-left"
                  [title]="viewLabel(world, other) + ' · beats ' + beatRuns(world, other.moments)"
                  [attr.aria-current]="other === view ? 'true' : null"
                  (click)="session.go(other.moments[0])"
                >
                  <span
                    class="block aspect-[3/2] overflow-hidden rounded-md bg-gray-100"
                    [class]="
                      other === view
                        ? 'ring-2 ring-indigo-500'
                        : 'ring-1 ring-gray-200 hover:ring-gray-400'
                    "
                  >
                    @if (thumbSrc) {
                      <img class="h-full w-full object-cover" [src]="thumbSrc" alt="" />
                    } @else {
                      <span
                        class="flex h-full items-center justify-center text-[10px] text-gray-400"
                        >not pictured</span
                      >
                    }
                  </span>
                  <span class="mt-0.5 block truncate text-[11px] text-gray-600">{{
                    viewLabel(world, other)
                  }}</span>
                </button>
              </li>
            }
          </ol>
        }
      } @else {
        <p class="mt-1 text-sm text-gray-500">
          The build gave this scene no camera, so the world has no view of it.
        </p>
      }
    }
  `,
})
export class WorldViews {
  protected readonly session = inject(WorldSession);
  protected readonly pictures = inject(WorldPictures);
  protected readonly usd = usd;
  protected readonly beatRuns = beatRuns;
  protected readonly placeFrom = placeFrom;
  protected readonly placeName = placeName;
  protected readonly viewLabel = viewLabel;
  protected readonly key = viewKey;

  constructor() {
    effect(() => {
      for (const view of this.session.views()) {
        const picture = this.pictures.pictureOf(view);
        if (picture) {
          void this.pictures.load(picture);
        }
      }
    });
  }
}
