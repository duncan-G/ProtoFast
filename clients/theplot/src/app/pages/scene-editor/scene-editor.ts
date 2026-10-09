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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { SceneElementType } from '../../stories/model/scene-element-type';
import { TheplotLogo } from '../../shared/theplot-logo';
import { ContainerMenu } from './container-menu';
import { DirectorView } from './director/director-view';
import { EditorMode } from './editor-mode';
import { EditorTheme } from './editor-theme';
import { LibraryPanel } from './library-panel';
import { LibraryRail } from './library-rail';
import { OverviewPanel } from './overview-panel';
import { SceneEditorStore } from './scene-editor-store';
import { StoryView } from './story/story-view';

const THEME_KEY = 'theplot.editor.theme';
const OVERVIEW_MIN_WIDTH = 1200;
const LIBRARY_MIN_WIDTH = 1280;
/** Keep in step with the 767px breakpoint in scene-editor.css. */
const DRAWER_MAX_WIDTH = 767;

/** Loads in the browser only, like the dashboard: the real API needs the session cookie. */
@Component({
  selector: 'app-scene-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ContainerMenu,
    DirectorView,
    LibraryPanel,
    LibraryRail,
    OverviewPanel,
    RouterLink,
    StoryView,
    TheplotLogo,
  ],
  providers: [SceneEditorStore],
  host: { '(window:resize)': 'width.set($any($event.target).innerWidth)' },
  templateUrl: './scene-editor.html',
})
export class SceneEditor {
  protected readonly store = inject(SceneEditorStore);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly theme = signal<EditorTheme>('dark');
  protected readonly width = signal(1440);
  private readonly overviewChoice = signal<boolean | null>(null);
  protected readonly showOverview = computed(
    () => this.overviewChoice() ?? this.width() >= OVERVIEW_MIN_WIDTH,
  );

  private readonly titleField = viewChild<ElementRef<HTMLInputElement>>('titleField');

  protected readonly directing = computed(() => this.store.mode() === 'director');
  /** In director mode the overview takes the inspector's place while it is open. */
  protected readonly directorOverview = signal(false);

  protected readonly sceneNumber = computed(() => (this.store.place()?.index ?? 0) + 1);
  protected readonly sceneCount = computed(() => this.store.place()?.container.scenes.length ?? 1);

  constructor() {
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      this.width.set(window.innerWidth);
      this.store.libraryCollapsed.set(window.innerWidth < LIBRARY_MIN_WIDTH);
      try {
        const saved = localStorage.getItem(THEME_KEY);
        if (saved === 'light' || saved === 'dark') {
          this.theme.set(saved);
        }
      } catch {
        // Storage can be blocked.
      }
      this.route.queryParamMap
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe((params) =>
          this.store.mode.set(params.get('mode') === 'director' ? 'director' : 'story'),
        );
      this.route.paramMap
        .pipe(takeUntilDestroyed(destroyRef))
        .subscribe(
          (params) => void this.store.open(params.get('storyId') ?? '', params.get('sceneId')),
        );
    });

    afterRenderEffect(() => {
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

  /** The mode lives in the URL, so a director-mode scene can be linked. */
  protected setMode(mode: EditorMode): void {
    this.store.mode.set(mode);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { mode: mode === 'director' ? 'director' : null },
      queryParamsHandling: 'merge',
      replaceUrl: true,
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
    const open = !this.showOverview();
    this.overviewChoice.set(open);
    if (open) {
      this.store.libraryOpen.set(false);
    }
  }

  protected toggleLibrary(): void {
    const open = !this.store.libraryOpen();
    this.store.libraryOpen.set(open);
    if (open) {
      this.store.libraryCollapsed.set(false);
      this.overviewChoice.set(false);
    }
  }

  protected closeDrawers(): void {
    this.store.libraryOpen.set(false);
    this.overviewChoice.set(false);
  }

  /** Director mode reads the script; a new line is written in story mode. */
  protected writeInStory(type: SceneElementType): void {
    this.setMode('story');
    this.store.insertElement(this.store.elements().length, type);
  }

  protected jump(elementId: string): void {
    if (this.width() <= DRAWER_MAX_WIDTH) {
      this.overviewChoice.set(false);
    }
    document
      .getElementById(`row-${elementId}`)
      ?.scrollIntoView({ block: 'center', behavior: 'smooth' });
  }
}
