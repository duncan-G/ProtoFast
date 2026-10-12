import { computed, Injectable, type OnDestroy, signal } from '@angular/core';
import {
  castAt,
  changeCounts,
  changesAt,
  holdingAt,
  stageOf,
  type World,
  type WorldRef,
} from './world';

export type WorldTab = 'place' | 'map' | 'cast' | 'conflicts';

const PLAY_MS = 1100;

/** What the world page is looking at: the moment, the place in focus and the inspected node. */
@Injectable()
export class WorldSession implements OnDestroy {
  readonly world = signal<World | null>(null);
  readonly moment = signal(0);
  /** A place the operator pinned; null follows the scene. */
  readonly pinned = signal<string | null>(null);
  readonly inspected = signal<WorldRef | null>(null);
  readonly tab = signal<WorldTab>('place');
  readonly playing = signal(false);
  readonly skipQuiet = signal(true);

  private timer: ReturnType<typeof setInterval> | null = null;

  readonly last = computed(() => Math.max((this.world()?.moments.length ?? 1) - 1, 0));
  readonly scene = computed(() => this.world()?.moments[this.moment()]?.scene ?? 0);
  readonly sceneLocation = computed(
    () => this.world()?.reply.scenes[this.scene()]?.locationId || null,
  );
  readonly placeId = computed(() => this.pinned() ?? this.sceneLocation());
  readonly holding = computed(() => {
    const world = this.world();
    return world ? holdingAt(world, this.moment()) : [];
  });
  readonly changes = computed(() => {
    const world = this.world();
    return world ? changesAt(world, this.moment()) : [];
  });
  readonly counts = computed(() => {
    const world = this.world();
    return world ? changeCounts(world) : [];
  });
  readonly stage = computed(() => {
    const world = this.world();
    const place = this.placeId();
    return world && place ? stageOf(world, place, this.moment(), this.holding()) : null;
  });
  readonly cast = computed(() => {
    const world = this.world();
    return world ? castAt(world, this.moment(), this.holding()) : [];
  });

  go(moment: number): void {
    this.moment.set(Math.min(Math.max(Math.round(moment), 0), this.last()));
  }

  step(delta: number): void {
    this.go(this.moment() + delta);
  }

  /** To the next or previous scene's opening; back first rewinds to this scene's opening. */
  stepScene(delta: number): void {
    const world = this.world();
    if (!world) {
      return;
    }
    const start = world.sceneStarts[this.scene()];
    const target = delta < 0 && this.moment() > start ? this.scene() : this.scene() + delta;
    this.go(world.sceneStarts[Math.min(Math.max(target, 0), world.sceneStarts.length - 1)] ?? 0);
  }

  focus(locationId: string | null): void {
    this.pinned.set(locationId === this.sceneLocation() ? null : locationId);
    this.tab.set('place');
  }

  inspect(ref: WorldRef | null): void {
    this.inspected.set(ref);
  }

  toggle(): void {
    if (this.playing()) {
      this.pause();
      return;
    }
    if (this.moment() >= this.last()) {
      this.go(0);
    }
    this.playing.set(true);
    this.timer = setInterval(() => this.advance(), PLAY_MS);
  }

  pause(): void {
    this.playing.set(false);
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  ngOnDestroy(): void {
    this.pause();
  }

  private advance(): void {
    const world = this.world();
    if (!world || this.moment() >= this.last()) {
      this.pause();
      return;
    }
    let next = this.moment() + 1;
    if (this.skipQuiet()) {
      const counts = this.counts();
      while (next < this.last() && counts[next] === 0 && world.moments[next].beat >= 0) {
        next++;
      }
    }
    this.go(next);
  }
}
