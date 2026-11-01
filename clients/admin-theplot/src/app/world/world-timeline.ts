import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { heading, hue } from './labels';
import { WorldSession } from './world-session';

const DENSITY_CAP = 6;

@Component({
  selector: 'app-world-timeline',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (session.world(); as world) {
      <section
        class="rounded-2xl border border-gray-200 bg-white/95 px-4 pt-3 pb-3 shadow-sm backdrop-blur"
      >
        <div
          class="relative cursor-pointer touch-none select-none"
          role="slider"
          tabindex="0"
          aria-label="Story time"
          [attr.aria-valuemin]="0"
          [attr.aria-valuemax]="session.last()"
          [attr.aria-valuenow]="session.moment()"
          (pointerdown)="press($event)"
          (pointermove)="drag($event)"
          (pointerup)="release($event)"
          (pointercancel)="release($event)"
        >
          <svg
            class="block h-7 w-full"
            [attr.viewBox]="'0 0 ' + total() + ' ' + cap"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <path [attr.d]="density()" class="fill-gray-300" />
          </svg>
          <div class="mt-0.5 flex h-6 w-full overflow-hidden rounded-md">
            @for (segment of segments(); track segment.index) {
              <div
                class="h-full border-r border-white last:border-r-0"
                [style.flex]="segment.size + ' 1 0'"
                [style.background]="segment.color"
                [class.opacity-45]="segment.index !== session.scene()"
                [title]="segment.title"
              ></div>
            }
          </div>
          <div
            class="pointer-events-none absolute -top-1 bottom-0 w-0.5 -translate-x-1/2 bg-indigo-600"
            [style.left.%]="playhead()"
          >
            <span
              class="absolute -top-1 left-1/2 h-2.5 w-2.5 -translate-x-1/2 rounded-full bg-indigo-600 ring-2 ring-white"
            ></span>
          </div>
        </div>

        <div class="mt-3 flex flex-wrap items-center gap-x-4 gap-y-2">
          <div class="flex items-center gap-1">
            <button
              type="button"
              [class]="button"
              title="Previous scene (Shift+←)"
              aria-label="Previous scene"
              (click)="session.stepScene(-1)"
            >
              <svg viewBox="0 0 16 16" class="h-3.5 w-3.5 fill-current" aria-hidden="true">
                <rect x="3" y="3" width="2" height="10" />
                <path d="M13 3 6 8l7 5z" />
              </svg>
            </button>
            <button
              type="button"
              [class]="button"
              title="Previous beat (←)"
              aria-label="Previous beat"
              (click)="session.step(-1)"
            >
              <svg viewBox="0 0 16 16" class="h-3.5 w-3.5 fill-current" aria-hidden="true">
                <path d="M11 3 4 8l7 5z" />
              </svg>
            </button>
            <button
              type="button"
              class="min-w-[4.5rem] rounded-lg bg-indigo-600 px-3 py-1 text-sm font-medium text-white hover:bg-indigo-700"
              [title]="session.playing() ? 'Pause (Space)' : 'Play (Space)'"
              (click)="session.toggle()"
            >
              {{ session.playing() ? 'Pause' : 'Play' }}
            </button>
            <button
              type="button"
              [class]="button"
              title="Next beat (→)"
              aria-label="Next beat"
              (click)="session.step(1)"
            >
              <svg viewBox="0 0 16 16" class="h-3.5 w-3.5 fill-current" aria-hidden="true">
                <path d="m5 3 7 5-7 5z" />
              </svg>
            </button>
            <button
              type="button"
              [class]="button"
              title="Next scene (Shift+→)"
              aria-label="Next scene"
              (click)="session.stepScene(1)"
            >
              <svg viewBox="0 0 16 16" class="h-3.5 w-3.5 fill-current" aria-hidden="true">
                <path d="m3 3 7 5-7 5z" />
                <rect x="11" y="3" width="2" height="10" />
              </svg>
            </button>
          </div>

          <div class="min-w-0 flex-1 text-sm">
            <span class="font-semibold text-gray-900"
              >Scene {{ session.scene() + 1 }}
              <span class="font-normal text-gray-400"
                >of {{ world.reply.scenes.length }}</span
              ></span
            >
            <span class="mx-1.5 text-gray-300">·</span>
            <span class="text-gray-900">{{ world.reply.scenes[session.scene()]?.title }}</span>
            <span class="mx-1.5 text-gray-300">·</span>
            <span class="font-mono text-xs text-gray-600">{{
              heading(world, session.scene())
            }}</span>
            <span class="mx-1.5 text-gray-300">·</span>
            <span class="text-gray-600">{{ beatLabel() }}</span>
          </div>

          <label class="flex items-center gap-1.5 text-xs text-gray-600">
            <input
              type="checkbox"
              class="rounded border-gray-300"
              [checked]="session.skipQuiet()"
              (change)="session.skipQuiet.set($any($event.target).checked)"
            />
            Play skips beats that change nothing
          </label>
        </div>
      </section>
    }
  `,
})
export class WorldTimeline {
  protected readonly session = inject(WorldSession);
  protected readonly heading = heading;
  protected readonly cap = DENSITY_CAP;
  protected readonly button =
    'grid h-7 w-8 place-items-center rounded-lg border border-gray-300 text-xs text-gray-700 hover:bg-gray-50';

  private dragging = false;

  protected readonly total = computed(() => this.session.world()?.moments.length ?? 1);

  protected readonly playhead = computed(
    () => ((this.session.moment() + 0.5) / this.total()) * 100,
  );

  protected readonly segments = computed(() => {
    const world = this.session.world();
    if (!world) {
      return [];
    }
    return world.reply.scenes.map((scene, index) => {
      const location = world.locations.get(scene.locationId);
      return {
        index,
        size: scene.beats.length + 1,
        color: location ? hue(location.hue, 0.78, 0.11) : '#d1d5db',
        title: `Scene ${index + 1} · ${scene.title}\n${heading(world, index)}\n${scene.beats.length} beats`,
      };
    });
  });

  protected readonly density = computed(() =>
    this.session
      .counts()
      .map((count, i) => {
        const h = Math.min(count, DENSITY_CAP);
        return h ? `M${i + 0.1} ${DENSITY_CAP}v${-h}h0.8v${h}z` : '';
      })
      .join(''),
  );

  protected readonly beatLabel = computed(() => {
    const world = this.session.world();
    const m = world?.moments[this.session.moment()];
    if (!world || !m) {
      return '';
    }
    const beats = world.reply.scenes[m.scene].beats.length;
    return m.beat < 0 ? `Opening (${beats} beats)` : `Beat ${m.beat + 1} of ${beats}`;
  });

  protected press(event: PointerEvent): void {
    this.dragging = true;
    (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
    this.session.pause();
    this.seek(event);
  }

  protected drag(event: PointerEvent): void {
    if (this.dragging) {
      this.seek(event);
    }
  }

  protected release(event: PointerEvent): void {
    this.dragging = false;
    (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
  }

  private seek(event: PointerEvent): void {
    const box = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const share = (event.clientX - box.left) / box.width;
    this.session.go(Math.floor(share * this.total()));
  }
}
