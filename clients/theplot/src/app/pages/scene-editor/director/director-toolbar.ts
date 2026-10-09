import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  signal,
} from '@angular/core';
import { DirectTarget } from '../../../direction/model/direct-target';
import { plural, slugline } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

type Menu = 'suggest' | 'generate' | 'direct';

const SUGGESTIONS: { label: string; hint: string; target: DirectTarget }[] = [
  {
    label: 'Frames',
    hint: 'Split, shots, who is on screen',
    target: { kind: 'frames' },
  },
  {
    label: 'Lines',
    hint: 'Delivery tags, gaps, cut-offs',
    target: { kind: 'cues' },
  },
  {
    label: 'Looks',
    hint: 'Wardrobe, appearance, places',
    target: { kind: 'continuity' },
  },
  {
    label: 'Story day',
    hint: 'Which day this happens on',
    target: { kind: 'storyDay' },
  },
];

/** The scene's slug and counts, and what the model and the speech provider can do for all of it. */
@Component({
  selector: 'app-director-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'dr-toolbar',
    '(document:mousedown)': 'onDocumentDown($event)',
  },
  template: `
    <span class="dr-slug">{{ slug() }}</span>
    <span class="dr-toolbar-meta">
      <label class="dr-day"
        >Day
        <input
          type="number"
          min="1"
          aria-label="Story day"
          placeholder="?"
          [value]="store.direction()?.storyDay ?? ''"
          (change)="store.setStoryDay(+$any($event.target).value || null)"
      /></label>
      · {{ counts() }}
    </span>
    <span class="flex-1"></span>

    <div class="dr-layout" role="group" aria-label="Read the scene as">
      <button
        type="button"
        [attr.aria-pressed]="store.directorLayout() === 'script'"
        (click)="store.directorLayout.set('script')"
      >
        Script
      </button>
      <button
        type="button"
        [attr.aria-pressed]="store.directorLayout() === 'frames'"
        (click)="store.directorLayout.set('frames')"
      >
        Frames
      </button>
    </div>

    <div class="dr-anchor">
      <button
        type="button"
        class="dr-btn"
        title="Have the model suggest part of the direction"
        [class.is-working]="suggesting()"
        [attr.aria-expanded]="open() === 'suggest'"
        (click)="toggle('suggest')"
      >
        Suggest ▾
      </button>
      @if (open() === 'suggest') {
        <div class="dr-menu" role="menu">
          <span class="dr-menu-kicker">Model · this scene</span>
          @for (item of suggestions; track item.label) {
            <button
              type="button"
              class="dr-menu-item"
              role="menuitem"
              [class.is-working]="store.isDirecting(item.target)"
              [disabled]="!store.canDirect(item.target)"
              (click)="suggest(item.target)"
            >
              <span class="flex-1">{{ item.label }}</span>
              <span class="dr-menu-meta">
                {{ store.isDirecting(item.target) ? 'Writing…' : item.hint }}
              </span>
            </button>
          }
        </div>
      }
    </div>

    <div class="dr-anchor">
      <button
        type="button"
        class="dr-btn"
        [attr.aria-expanded]="open() === 'generate'"
        (click)="toggle('generate')"
      >
        Generate ▾
      </button>
      @if (open() === 'generate') {
        <div class="dr-menu" role="menu">
          <span class="dr-menu-kicker">Audio · whole scene</span>
          <button
            type="button"
            class="dr-menu-item"
            role="menuitem"
            [disabled]="store.linesNeedingAudio().length === 0"
            (click)="generate(store.linesNeedingAudio(), false)"
          >
            <span class="flex-1">Missing &amp; out of date</span>
            <span class="dr-menu-meta">{{ beats(store.linesNeedingAudio().length) }}</span>
          </button>
          <button
            type="button"
            class="dr-menu-item"
            role="menuitem"
            [disabled]="spokenIds().length === 0"
            (click)="generate(spokenIds(), true)"
          >
            <span class="flex-1">Regenerate everything</span>
            <span class="dr-menu-meta">{{ beats(spokenIds().length) }}</span>
          </button>
          <span class="dr-menu-rule"></span>
          <span class="dr-menu-kicker">Images · all frames</span>
          <button
            type="button"
            class="dr-menu-item"
            role="menuitem"
            title="Pictures the scene's cast, places and props first when they have none, then each frame in order"
            [disabled]="store.framesNeedingImages().length === 0"
            (click)="picture(store.framesNeedingImages(), false)"
          >
            <span class="flex-1">Missing &amp; out of date</span>
            <span class="dr-menu-meta">{{ framesLabel(store.framesNeedingImages().length) }}</span>
          </button>
          <button
            type="button"
            class="dr-menu-item"
            role="menuitem"
            [disabled]="frameIds().length === 0"
            (click)="picture(frameIds(), true)"
          >
            <span class="flex-1">Regenerate everything</span>
            <span class="dr-menu-meta">{{ framesLabel(frameIds().length) }}</span>
          </button>
        </div>
      }
    </div>

    <div class="dr-split dr-anchor">
      <button
        type="button"
        class="dr-split-main"
        title="The model finds who and what is in the scene, then writes the story day, looks, lines and frames; your own edits stay"
        [class.is-working]="directingScene()"
        [disabled]="!canDirectScene()"
        (click)="store.direct({ kind: 'scene' })"
      >
        {{ directingScene() ? 'Directing…' : 'Direct scene' }}
      </button>
      <button
        type="button"
        class="dr-split-more"
        aria-label="More ways to direct"
        [attr.aria-expanded]="open() === 'direct'"
        (click)="toggle('direct')"
      >
        ▾
      </button>
      @if (open() === 'direct') {
        <div class="dr-menu dr-menu-end" role="menu">
          <button
            type="button"
            class="dr-menu-item"
            role="menuitem"
            [disabled]="!canDirectScene()"
            (click)="redo()"
          >
            <span class="flex-1">Redo from scratch</span>
            <span class="dr-menu-meta">replaces your edits</span>
          </button>
        </div>
      }
    </div>
  `,
})
export class DirectorToolbar {
  protected readonly store = inject(SceneEditorStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly open = signal<Menu | null>(null);
  protected readonly suggestions = SUGGESTIONS;

  protected readonly directingScene = computed(() => this.store.isDirecting({ kind: 'scene' }));
  protected readonly canDirectScene = computed(() => this.store.canDirect({ kind: 'scene' }));
  protected readonly suggesting = computed(() =>
    SUGGESTIONS.some((item) => this.store.isDirecting(item.target)),
  );
  protected readonly spokenIds = computed(() => this.store.spokenElements().map((e) => e.id));

  protected readonly slug = computed(() => {
    const heading = this.store.elements().find((e) => e.type === 'Heading');
    const location = heading?.locationId ? this.store.locationById().get(heading.locationId) : null;
    if (!location) {
      return 'NO LOCATION';
    }
    return slugline(location) + (heading?.timeOfDay ? ` — ${heading.timeOfDay.toUpperCase()}` : '');
  });

  protected readonly counts = computed(() => {
    const beats = this.store.elements().filter((e) => e.type !== 'Heading').length;
    return `${this.frames()} · ${plural(beats, 'beat')}`;
  });

  protected readonly frames = computed(() => plural(this.store.frameSpans().length, 'frame'));

  protected readonly frameIds = computed(() =>
    this.store.frameSpans().flatMap((s) => (s.frame.id ? [s.frame.id] : [])),
  );

  protected beats(count: number): string {
    return plural(count, 'beat');
  }

  protected framesLabel(count: number): string {
    return plural(count, 'frame');
  }

  protected toggle(menu: Menu): void {
    this.open.set(this.open() === menu ? null : menu);
  }

  protected suggest(target: DirectTarget): void {
    this.open.set(null);
    void this.store.direct(target);
  }

  protected generate(elementIds: readonly string[], regenerate: boolean): void {
    this.open.set(null);
    void this.store.generateAudio(elementIds, regenerate);
  }

  protected picture(frameIds: readonly string[], regenerate: boolean): void {
    this.open.set(null);
    void this.store.generateFrameImages(frameIds, regenerate, true);
  }

  protected redo(): void {
    this.open.set(null);
    void this.store.direct({ kind: 'scene' }, true);
  }

  protected onDocumentDown(event: MouseEvent): void {
    if (this.open() !== null && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(null);
    }
  }
}
