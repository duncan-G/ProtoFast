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
import { TheplotLogo } from '../../shared/theplot-logo';
import { ContainerMenu } from './container-menu';
import { EditorTheme } from './editor-theme';
import { LibraryPanel } from './library-panel';
import { OverviewPanel } from './overview-panel';
import { SceneEditorStore } from './scene-editor-store';
import { StoryView } from './story/story-view';

const THEME_KEY = 'theplot.editor.theme';
const OVERVIEW_MIN_WIDTH = 1200;
/** Keep in step with the 767px breakpoint in scene-editor.css. */
const DRAWER_MAX_WIDTH = 767;

/** Loads in the browser only, like the dashboard: the real API needs the session cookie. */
@Component({
  selector: 'app-scene-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ContainerMenu, LibraryPanel, OverviewPanel, RouterLink, StoryView, TheplotLogo],
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

  private readonly titleField = viewChild<ElementRef<HTMLInputElement>>('titleField');

  protected readonly sceneNumber = computed(() => (this.store.place()?.index ?? 0) + 1);
  protected readonly sceneCount = computed(() => this.store.place()?.container.scenes.length ?? 1);

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
      this.overviewChoice.set(false);
    }
  }

  protected closeDrawers(): void {
    this.store.libraryOpen.set(false);
    this.overviewChoice.set(false);
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
