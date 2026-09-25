import {
  afterNextRender,
  afterRenderEffect,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { SceneElementType } from '../../stories/model/scene-element-type';
import { blockLength, canDrop } from '../../stories/scene-blocks';
import { TheplotLogo } from '../../shared/theplot-logo';
import { BeatRow } from './beat-row';
import { ContainerMenu } from './container-menu';
import { EditorTheme } from './editor-theme';
import { slugline } from './format';
import { HeadingRow } from './heading-row';
import { InsertMenu } from './insert-menu';
import { LibraryPanel } from './library-panel';
import { OverviewPanel } from './overview-panel';
import { SceneEditorStore } from './scene-editor-store';
import { ScenePlace } from './scene-place';
import { SceneRow } from './scene-row';
import { TransitionRow } from './transition-row';

const THEME_KEY = 'theplot.editor.theme';
const OVERVIEW_MIN_WIDTH = 1200;

/** Loads in the browser only, like the dashboard: the real API needs the session cookie. */
@Component({
  selector: 'app-scene-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BeatRow,
    ContainerMenu,
    HeadingRow,
    InsertMenu,
    LibraryPanel,
    OverviewPanel,
    RouterLink,
    TheplotLogo,
    TransitionRow,
  ],
  providers: [SceneEditorStore],
  host: { '(window:resize)': 'width.set($any($event.target).innerWidth)' },
  templateUrl: './scene-editor.html',
})
export class SceneEditor {
  protected readonly store = inject(SceneEditorStore);
  private readonly route = inject(ActivatedRoute);

  protected readonly theme = signal<EditorTheme>('dark');
  protected readonly width = signal(1440);
  private readonly overviewChoice = signal<boolean | null>(null);
  protected readonly showOverview = computed(
    () => this.overviewChoice() ?? this.width() >= OVERVIEW_MIN_WIDTH,
  );

  /** A gap index, 0…rows. */
  protected readonly insertAt = signal<number | null>(null);
  protected readonly dragFrom = signal<number | null>(null);
  protected readonly dropAt = signal<number | null>(null);
  private readonly dragLength = computed(() => {
    const from = this.dragFrom();
    return from === null ? 0 : blockLength(this.store.elements(), from);
  });
  protected readonly dragLabel = computed(() => {
    const beats = this.dragLength() - 1;
    return beats > 0 ? `Move location + ${beats} beat${beats === 1 ? '' : 's'} here` : 'Move here';
  });

  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly titleField = viewChild<ElementRef<HTMLInputElement>>('titleField');
  private shownSceneId: string | null = null;

  protected readonly sceneNumber = computed(() => (this.store.place()?.index ?? 0) + 1);
  protected readonly sceneCount = computed(() => this.store.place()?.container.scenes.length ?? 1);

  protected readonly nextSlug = computed(() => {
    const next = this.store.next()?.summary;
    const location = next?.openingLocationId
      ? this.store.locationById().get(next.openingLocationId)
      : undefined;
    if (!location) {
      return '';
    }
    return slugline(location) + (next?.openingTimeOfDay ? ` — ${next.openingTimeOfDay}` : '');
  });

  protected readonly addAfterHint = computed(() => {
    const place = this.store.place();
    const next = this.store.next();
    if (!place) {
      return '';
    }
    const number = place.index + 2;
    return next && next.container.id === place.container.id
      ? `Becomes Scene ${number} · “${next.summary.title}” moves to ${number + 1}`
      : `Becomes Scene ${number} of ${place.container.label}`;
  });

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      this.width.set(window.innerWidth);
      try {
        const saved = localStorage.getItem(THEME_KEY);
        if (saved === 'light' || saved === 'dark') {
          this.theme.set(saved);
        }
      } catch {
        // Storage can be blocked.
      }
      this.route.paramMap
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe(
          (params) => void this.store.open(params.get('storyId') ?? '', params.get('sceneId')),
        );
    });

    afterRenderEffect(() => {
      const id = this.store.scene()?.id ?? null;
      if (id !== this.shownSceneId) {
        this.shownSceneId = id;
        this.scroller()?.nativeElement.scrollTo({ top: 0 });
        this.insertAt.set(null);
      }
      if (this.store.titleRequest()) {
        const field = this.titleField()?.nativeElement;
        if (field) {
          field.focus();
          field.select();
          this.store.titleRequest.set(false);
        }
      }
    });
  }

  protected toggleTheme(): void {
    const theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(theme);
    try {
      localStorage.setItem(THEME_KEY, theme);
    } catch {
      // Storage can be blocked.
    }
  }

  protected toggleOverview(): void {
    this.overviewChoice.set(!this.showOverview());
  }

  protected placeLabel(place: ScenePlace): string {
    const scene = `SCENE ${place.index + 1}`;
    return place.container.id === this.store.place()?.container.id
      ? scene
      : `${place.container.label.toUpperCase()} · ${scene}`;
  }

  protected insert(at: number, type: SceneElementType): void {
    const atEnd = at >= this.store.elements().length;
    this.store.insertElement(at, type);
    this.insertAt.set(null);
    if (atEnd) {
      setTimeout(() => {
        const scroller = this.scroller()?.nativeElement;
        scroller?.scrollTo({ top: scroller.scrollHeight, behavior: 'smooth' });
      });
    }
  }

  protected jump(elementId: string): void {
    const row = document.getElementById(`row-${elementId}`);
    this.scroller()?.nativeElement.scrollTo({
      top: (row?.offsetTop ?? 0) - 20,
      behavior: 'smooth',
    });
  }

  // ─── drag to reorder ───────────────────────────────────────────────────

  protected onDragStart(event: DragEvent, row: SceneRow): void {
    event.stopPropagation();
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', row.element.id);
      const element = document.getElementById(`row-${row.element.id}`);
      if (element) {
        event.dataTransfer.setDragImage(element, 24, 18);
      }
    }
    // After the browser has taken its drag image, so the row isn't faded in it.
    setTimeout(() => {
      this.dragFrom.set(row.index);
      this.dropAt.set(null);
      this.store.editingId.set(null);
      this.insertAt.set(null);
    });
  }

  protected onDragOver(event: DragEvent, index: number): void {
    if (this.dragFrom() === null) {
      return;
    }
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.dropAt.set(event.clientY < rect.top + rect.height / 2 ? index : index + 1);
  }

  protected onEndDragOver(event: DragEvent): void {
    if (this.dragFrom() === null) {
      return;
    }
    event.preventDefault();
    this.dropAt.set(this.store.elements().length);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    const from = this.dragFrom();
    const at = this.dropAt();
    if (from !== null && at !== null) {
      this.store.dropBlock(from, at);
    }
    this.endDrag();
  }

  protected endDrag(): void {
    this.dragFrom.set(null);
    this.dropAt.set(null);
  }

  protected isDragged(index: number): boolean {
    const from = this.dragFrom();
    return from !== null && index >= from && index < from + this.dragLength();
  }

  protected showsDrop(at: number): boolean {
    const from = this.dragFrom();
    return from !== null && this.dropAt() === at && canDrop(this.store.elements(), from, at);
  }
}
