import {
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  viewChild,
} from '@angular/core';
import { WorldEdge } from '../../lib/gen/Admin/Theplot/theplot_world_models_pb';
import { heading, hue } from './labels';
import { isPhysical } from './world';
import { WorldBeatSpeech } from './world-beat-speech';
import { WorldChange } from './world-change';
import { WorldSession } from './world-session';
import { WorldViews } from './world-views';

@Component({
  selector: 'app-world-now',
  imports: [WorldBeatSpeech, WorldChange, WorldViews],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (session.world(); as world) {
      @let scene = world.reply.scenes[session.scene()];
      <section class="rounded-2xl border border-gray-200 bg-white shadow-sm">
        <header class="border-b border-gray-100 px-4 py-3">
          <p
            class="flex items-center gap-2 text-xs font-medium tracking-wide text-gray-500 uppercase"
          >
            <span class="h-2.5 w-2.5 rounded-full" [style.background]="swatch()"></span>
            Scene {{ session.scene() + 1 }}
          </p>
          <h2 class="mt-0.5 font-semibold text-gray-900">{{ scene?.title }}</h2>
          <p class="font-mono text-xs text-gray-500">{{ heading(world, session.scene()) }}</p>
        </header>

        <app-world-views class="block border-b border-gray-100 px-4 py-3" />

        <div class="px-4 py-3">
          <h3 class="text-xs font-semibold tracking-wide text-gray-500 uppercase">
            {{ isOpening() ? 'As the scene opens' : 'This beat' }}
          </h3>
          @if (isOpening()) {
            <p class="mt-1 text-sm text-gray-600">
              The world as it stands before the first beat: what earlier scenes left here, and the
              fixtures this place always had.
            </p>
          } @else {
            <blockquote
              class="mt-1 border-l-2 border-indigo-400 pl-3 text-sm leading-6 text-gray-900"
            >
              @if (beat()?.speaker) {
                <span class="font-semibold tracking-wide uppercase">{{ beat()?.speaker }}</span
                ><br />
              }
              {{ beat()?.text }}
            </blockquote>
            <app-world-beat-speech class="mt-2 block" />
          }

          <h3 class="mt-4 text-xs font-semibold tracking-wide text-gray-500 uppercase">
            Changes to the place
            <span class="ml-1 font-normal text-gray-400 normal-case">{{
              placeChanges().length
            }}</span>
          </h3>
          <div class="mt-1.5 space-y-1.5">
            @for (change of placeChanges(); track $index) {
              <app-world-change [change]="change" />
            } @empty {
              <p class="text-sm text-gray-500">None. The place stands as it was.</p>
            }
          </div>
          @if (action().length) {
            <h3 class="mt-4 text-xs font-semibold tracking-wide text-gray-500 uppercase">
              Action
              <span class="ml-1 font-normal text-gray-400 normal-case"
                >who and what moved; not a change to the place</span
              >
            </h3>
            <div class="mt-1.5 space-y-1.5 opacity-75">
              @for (change of action(); track $index) {
                <app-world-change [change]="change" />
              }
            </div>
          }
        </div>

        <div class="border-t border-gray-100">
          <h3 class="px-4 pt-3 text-xs font-semibold tracking-wide text-gray-500 uppercase">
            Script
          </h3>
          <ol #script class="relative max-h-[28rem] overflow-y-auto px-2 py-2 text-sm">
            @for (line of lines(); track line.moment) {
              <li>
                <button
                  type="button"
                  class="flex w-full items-start gap-2 rounded-lg px-2 py-1.5 text-left"
                  [class]="line.moment === session.moment() ? 'bg-indigo-50' : 'hover:bg-gray-50'"
                  [attr.data-moment]="line.moment"
                  [attr.aria-current]="line.moment === session.moment() ? 'step' : null"
                  (click)="session.go(line.moment)"
                >
                  <span
                    class="w-5 shrink-0 pt-px text-right text-xs tabular-nums"
                    [class.text-indigo-600]="line.moment === session.moment()"
                    [class.text-gray-400]="line.moment !== session.moment()"
                    >{{ line.number }}</span
                  >
                  <span
                    class="min-w-0 flex-1 line-clamp-2"
                    [class.text-gray-900]="line.moment === session.moment()"
                    [class.text-gray-600]="line.moment !== session.moment()"
                    [class.italic]="line.number === '·'"
                    >{{ line.text }}</span
                  >
                  @if (line.changes) {
                    <span
                      class="shrink-0 rounded-full bg-emerald-50 px-1.5 text-[11px] text-emerald-700 tabular-nums ring-1 ring-emerald-200"
                      [title]="line.changes + ' changes'"
                      >{{ line.changes }}</span
                    >
                  }
                </button>
              </li>
            }
          </ol>
        </div>
      </section>
    }
  `,
})
export class WorldNow {
  protected readonly session = inject(WorldSession);
  protected readonly heading = heading;

  private readonly script = viewChild<ElementRef<HTMLElement>>('script');

  protected readonly isOpening = computed(
    () => (this.session.world()?.moments[this.session.moment()]?.beat ?? -1) < 0,
  );

  protected readonly beat = computed(() => {
    const world = this.session.world();
    const m = world?.moments[this.session.moment()];
    return m && m.beat >= 0 ? world.reply.scenes[m.scene].beats[m.beat] : undefined;
  });

  protected readonly swatch = computed(() => {
    const world = this.session.world();
    return hue(world?.locations.get(this.session.sceneLocation() ?? '')?.hue ?? 0);
  });

  protected readonly placeChanges = computed(() => {
    const world = this.session.world();
    return world
      ? this.session.changes().filter((c) => isPhysical(world, c.after ?? c.before!))
      : [];
  });

  /** Where the beat and the camera are show on the stage, so they are left out. */
  protected readonly action = computed(() => {
    const world = this.session.world();
    return world
      ? this.session
          .changes()
          .filter(
            (c) =>
              c.edge !== WorldEdge.BEAT_AT &&
              c.edge !== WorldEdge.CAMERA &&
              !isPhysical(world, c.after ?? c.before!),
          )
      : [];
  });

  protected readonly lines = computed(() => {
    const world = this.session.world();
    if (!world) {
      return [];
    }
    const index = this.session.scene();
    const start = world.sceneStarts[index];
    const counts = this.session.counts();
    const scene = world.reply.scenes[index];
    return [
      { moment: start, number: '·', text: 'Opening', changes: counts[start] },
      ...scene.beats.map((beat, i) => ({
        moment: start + 1 + i,
        number: `${i + 1}`,
        text: beat.speaker ? `${beat.speaker}: ${beat.text}` : beat.text,
        changes: counts[start + 1 + i],
      })),
    ];
  });

  constructor() {
    afterRenderEffect(() => {
      const moment = this.session.moment();
      const list = this.script()?.nativeElement;
      const item = list?.querySelector<HTMLElement>(`[data-moment="${moment}"]`);
      if (!list || !item) {
        return;
      }
      if (item.offsetTop < list.scrollTop) {
        list.scrollTop = item.offsetTop - 8;
      } else if (item.offsetTop + item.offsetHeight > list.scrollTop + list.clientHeight) {
        list.scrollTop = item.offsetTop + item.offsetHeight - list.clientHeight + 8;
      }
    });
  }
}
