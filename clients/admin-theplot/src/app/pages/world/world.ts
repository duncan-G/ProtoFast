import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { WorldModelBuildStatus } from '../../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { errorMessage, TheplotAdminApi } from '../../theplot-admin';
import { BUILD_STATUS_CLASS, BUILD_STATUS_LABEL } from '../../world-models/format';
import { worldOf } from '../../world/world';
import { WorldCast } from '../../world/world-cast';
import { WorldConflicts } from '../../world/world-conflicts';
import { WorldInspector } from '../../world/world-inspector';
import { WorldMap } from '../../world/world-map';
import { WorldNow } from '../../world/world-now';
import { WorldSession, type WorldTab } from '../../world/world-session';
import { WorldStage } from '../../world/world-stage';
import { WorldTimeline } from '../../world/world-timeline';

const TABS: { id: WorldTab; label: string }[] = [
  { id: 'place', label: 'Place' },
  { id: 'map', label: 'Map' },
  { id: 'cast', label: 'Cast' },
  { id: 'conflicts', label: 'Conflicts' },
];

/** The moment, tab and pinned place live in the query string, so a view can be shared. */
@Component({
  selector: 'app-world',
  imports: [
    RouterLink,
    WorldCast,
    WorldConflicts,
    WorldInspector,
    WorldMap,
    WorldNow,
    WorldStage,
    WorldTimeline,
  ],
  providers: [WorldSession],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown)': 'key($event)' },
  template: `
    <div class="relative left-1/2 w-[calc(100vw-2rem)] max-w-[90rem] -translate-x-1/2">
      <nav class="text-sm text-gray-500">
        <a routerLink="/stories" class="text-indigo-600 hover:underline">Stories</a>
        <span class="mx-1">/</span>
        <a [routerLink]="['/stories', id()]" class="text-indigo-600 hover:underline">{{
          title() || 'Story'
        }}</a>
        <span class="mx-1">/</span>
        World
      </nav>

      @if (error()) {
        <p class="mt-6 rounded-lg border border-red-200 bg-red-50 p-4 text-red-800">
          {{ error() }}
        </p>
      }

      @if (session.world(); as world) {
        @let build = world.reply.latestBuild;
        <header class="mt-3 flex flex-wrap items-end gap-x-4 gap-y-2">
          <div class="min-w-0">
            <h1 class="text-2xl font-bold text-gray-900">
              The world of {{ world.reply.storyTitle }}
            </h1>
            <p class="mt-1 text-sm text-gray-600">
              {{ world.reply.locations.length }} places · {{ world.reply.parts.length }} parts ·
              {{ world.reply.objects.length }} things ·
              {{ world.reply.characters.length }} characters, across
              {{ world.reply.scenes.length }} scenes and
              {{ world.moments.length - world.reply.scenes.length }} beats ·
              {{ world.facts.length }} facts
            </p>
          </div>
          @if (build) {
            <div class="ml-auto flex items-center gap-2 text-sm">
              <span [class]="statusClass[build.status]">{{ statusLabel[build.status] }}</span>
              <span class="text-gray-500"
                >{{ build.scenesDone }} of {{ build.scenesTotal }} scenes read</span
              >
              <a [routerLink]="['/world-models', build.id]" class="text-indigo-600 hover:underline"
                >Build</a
              >
            </div>
          }
        </header>

        @if (!world.facts.length) {
          <div
            class="mt-6 rounded-2xl border border-dashed border-gray-300 bg-white p-10 text-center"
          >
            <p class="font-medium text-gray-900">No world yet</p>
            <p class="mt-1 text-sm text-gray-600">
              @if (!build) {
                The writer has not built this story's world model. They start one with the World
                button in the scene editor.
              } @else if (active()) {
                A build is still going. Reload when it finishes.
              } @else {
                The last build left no facts.
                @if (build.failure) {
                  It failed: {{ build.failure }}
                }
              }
            </p>
          </div>
        } @else {
          <div class="sticky top-0 z-30 mt-4 pt-2">
            <app-world-timeline />
          </div>

          <div class="mt-4 grid gap-4 lg:grid-cols-[minmax(0,24rem)_minmax(0,1fr)]">
            <app-world-now class="block self-start" />

            <section class="min-w-0">
              <div class="flex items-center gap-1 border-b border-gray-200" role="tablist">
                @for (item of tabs; track item.id) {
                  <button
                    type="button"
                    role="tab"
                    class="-mb-px border-b-2 px-3 py-2 text-sm"
                    [class]="
                      session.tab() === item.id
                        ? 'border-indigo-600 font-medium text-indigo-700'
                        : 'border-transparent text-gray-600 hover:text-gray-900'
                    "
                    [attr.aria-selected]="session.tab() === item.id"
                    (click)="session.tab.set(item.id)"
                  >
                    {{ item.label }}
                    @if (item.id === 'conflicts' && world.reply.conflicts.length) {
                      <span class="ml-1 rounded-full bg-amber-100 px-1.5 text-xs text-amber-800">{{
                        world.reply.conflicts.length
                      }}</span>
                    }
                  </button>
                }
                <span class="ml-auto hidden text-xs text-gray-400 sm:inline"
                  >← → beat · Shift+← → scene · Space play</span
                >
              </div>
              <div class="mt-4">
                @switch (session.tab()) {
                  @case ('place') {
                    <app-world-stage />
                  }
                  @case ('map') {
                    <app-world-map />
                  }
                  @case ('cast') {
                    <app-world-cast />
                  }
                  @case ('conflicts') {
                    <app-world-conflicts />
                  }
                }
              </div>
            </section>
          </div>

          <app-world-inspector />
        }
      } @else if (!error()) {
        <p class="mt-6 text-sm text-gray-500">Loading the world…</p>
      }
    </div>
  `,
})
export class StoryWorld {
  protected readonly session = inject(WorldSession);
  private readonly api = inject(TheplotAdminApi);
  private readonly router = inject(Router);

  readonly id = input.required<string>();
  readonly m = input<string>();
  readonly tab = input<string>();
  readonly place = input<string>();

  protected readonly tabs = TABS;
  protected readonly statusLabel = BUILD_STATUS_LABEL;
  protected readonly statusClass = BUILD_STATUS_CLASS;
  protected readonly error = signal('');
  protected readonly title = computed(() => this.session.world()?.reply.storyTitle ?? '');
  protected readonly active = computed(() => {
    const status = this.session.world()?.reply.latestBuild?.status;
    return (
      status === WorldModelBuildStatus.QUEUED ||
      status === WorldModelBuildStatus.RUNNING ||
      status === WorldModelBuildStatus.RETRYING
    );
  });

  constructor() {
    afterNextRender(() => void this.load());
    effect(() => {
      if (!this.session.world()) {
        return;
      }
      const tab = this.session.tab();
      void this.router.navigate([], {
        queryParams: {
          m: this.session.moment() || null,
          tab: tab === 'place' ? null : tab,
          place: this.session.pinned(),
        },
        replaceUrl: true,
      });
    });
  }

  protected key(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    if (
      !this.session.world() ||
      event.altKey ||
      event.ctrlKey ||
      event.metaKey ||
      target?.closest('input, select, textarea, [contenteditable="true"]')
    ) {
      return;
    }
    if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
      const delta = event.key === 'ArrowRight' ? 1 : -1;
      this.session.pause();
      if (event.shiftKey) {
        this.session.stepScene(delta);
      } else {
        this.session.step(delta);
      }
      event.preventDefault();
    } else if (event.key === ' ' && !target?.closest('button')) {
      this.session.toggle();
      event.preventDefault();
    }
  }

  private async load(): Promise<void> {
    try {
      const reply = await this.api.worldModels.getWorld({ storyId: this.id() });
      const world = worldOf(reply);
      this.session.world.set(world);
      this.session.go(Number(this.m()) || 0);
      const tab = TABS.find((t) => t.id === this.tab());
      if (tab) {
        this.session.tab.set(tab.id);
      }
      const place = this.place();
      if (place && world.locations.has(place)) {
        this.session.pinned.set(place);
      }
    } catch (err) {
      this.error.set(errorMessage(err));
    }
  }
}
