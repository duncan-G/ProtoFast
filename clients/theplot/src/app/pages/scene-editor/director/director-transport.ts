import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { spanAt } from '../../../direction/frames';
import { SceneElement } from '../../../stories/model/scene-element';
import { plural } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

/** Until the browser has read a line's audio, its length is guessed from its words. */
function estimateMs(element: SceneElement): number {
  const length = (element.text ?? '').length;
  return element.type === 'Dialogue' ? Math.max(1400, length * 70) : Math.max(1800, length * 60);
}

function clock(ms: number): string {
  const seconds = Math.round(ms / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

/** Plays the scene's lines in order, with every line as a segment of the timeline. */
@Component({
  selector: 'app-director-transport',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-transport' },
  template: `
    <button
      type="button"
      class="dr-play"
      [title]="playingScene() ? 'Stop' : 'Play scene'"
      [attr.aria-label]="playingScene() ? 'Stop' : 'Play scene'"
      [disabled]="!playingScene() && !hasAudio()"
      (click)="toggle()"
    >
      {{ playingScene() ? '❚❚' : '▶' }}
    </button>
    <div class="dr-transport-title">
      <span>{{ title() }}</span>
      <span class="dr-transport-time">{{ time() }}</span>
    </div>
    <div class="dr-timeline">
      @for (segment of segments(); track segment.id) {
        <button
          type="button"
          class="dr-segment"
          [attr.data-state]="segment.state"
          [class.is-played]="segment.played"
          [class.is-current]="segment.current"
          [class.is-frame-start]="segment.frameStart"
          [style.flex-grow]="segment.ms"
          [title]="segment.title"
          [attr.aria-label]="segment.title"
          (click)="playFrom(segment.index)"
        ></button>
      }
    </div>
    @if (store.linesNeedingAudio().length > 0) {
      <span class="dr-transport-missing">{{ need() }}</span>
      <button
        type="button"
        class="dr-link"
        (click)="store.generateAudio(store.linesNeedingAudio())"
      >
        Generate
      </button>
    }
  `,
})
export class DirectorTransport {
  protected readonly store = inject(SceneEditorStore);

  protected readonly playingScene = computed(() => this.store.player.context() === 'scene');
  protected readonly hasAudio = computed(() =>
    [...this.store.cueByElement().values()].some((c) => c.audio !== null),
  );

  private readonly lines = computed(() => {
    const cues = this.store.cueByElement();
    const durations = this.store.player.durations();
    return this.store.spokenElements().map((element) => {
      const url = cues.get(element.id)?.audio?.url;
      return { element, ms: (url && durations.get(url)) || estimateMs(element) };
    });
  });

  private readonly currentIndex = computed(() => {
    const playing = this.store.player.playing();
    return playing === null ? -1 : this.lines().findIndex((l) => l.element.id === playing);
  });

  protected readonly segments = computed(() => {
    const spans = this.store.frameSpans();
    const index = new Map(this.store.elements().map((e, i) => [e.id, i]));
    const states = this.store.audioStates();
    const current = this.currentIndex();
    const scene = this.playingScene();
    let previousFrame: unknown = null;
    return this.lines().map((line, i) => {
      const frame = spanAt(spans, index.get(line.element.id) ?? -1);
      const frameStart = i > 0 && frame !== previousFrame;
      previousFrame = frame;
      const state = states.get(line.element.id) ?? 'none';
      return {
        id: line.element.id,
        index: i,
        ms: line.ms,
        state,
        frameStart,
        current: i === current,
        played: scene && current >= 0 && i < current,
        title: `Line ${i + 1} · ${
          state === 'none' ? 'no audio' : state === 'stale' ? 'out of date' : state
        }`,
      };
    });
  });

  protected readonly title = computed(() => {
    const playing = this.store.player.playing();
    if (playing === null) {
      return 'Play scene';
    }
    const at = this.store.elements().findIndex((e) => e.id === playing);
    const span = spanAt(this.store.frameSpans(), at);
    return span ? `Playing · Frame ${this.store.frameSpans().indexOf(span) + 1}` : 'Playing';
  });

  protected readonly time = computed(() => {
    const total = this.lines().reduce((t, l) => t + l.ms, 0);
    const current = this.currentIndex();
    const elapsed =
      this.playingScene() && current >= 0
        ? this.lines()
            .slice(0, current)
            .reduce((t, l) => t + l.ms, 0) + this.store.player.position()
        : 0;
    return `${clock(elapsed)} / ${clock(total)}`;
  });

  protected need(): string {
    const count = this.store.linesNeedingAudio().length;
    return `${plural(count, 'line')} need${count === 1 ? 's' : ''} audio`;
  }

  protected toggle(): void {
    if (this.playingScene()) {
      this.store.player.stop();
    } else {
      this.playFrom(0);
    }
  }

  protected playFrom(index: number): void {
    this.store.playLines(
      this.lines()
        .slice(index)
        .map((l) => l.element.id),
      'scene',
    );
  }
}
