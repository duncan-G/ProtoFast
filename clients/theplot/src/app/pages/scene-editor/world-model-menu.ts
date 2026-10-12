import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  input,
  PLATFORM_ID,
  signal,
} from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { describeError } from '../../documents/document-api';
import {
  isBuildActive,
  WorldModelBuild,
  WorldModelBuildState,
} from '../../world-model/world-model-build';
import { WorldModelApi } from '../../world-model/world-model-api';

const POLL_MS = 5_000;

const STATE_LABEL: Record<WorldModelBuildState, string> = {
  queued: 'Waiting to start',
  running: 'Building',
  retrying: 'Trying again',
  finished: 'Built',
  failed: 'Failed',
  cancelled: 'Stopped',
};

/**
 * The "World" button in the editor header: starts a build of the story's location world model,
 * follows it while it runs, and shows what the last one found. The build runs in the background,
 * so the popover polls while one is active.
 */
@Component({
  selector: 'app-world-model-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'se-world-menu',
    '(document:mousedown)': 'onDocumentDown($event)',
    '(keydown.escape)': 'open.set(false)',
  },
  template: `
    <button
      type="button"
      class="se-btn"
      aria-haspopup="dialog"
      [attr.aria-expanded]="open()"
      [attr.aria-pressed]="open()"
      [title]="active() ? 'World model: ' + progressLabel() : 'World model'"
      (click)="toggle()"
    >
      @if (active()) {
        <span class="spinner h-3 w-3" aria-hidden="true"></span>
      } @else {
        ◎
      }
      <span class="se-btn-label">World</span>
    </button>
    @if (open()) {
      <div class="se-pop se-world-pop" role="dialog" aria-label="World model">
        <span class="se-kicker">Location world model</span>
        <p class="se-world-blurb">
          What each location contains, how it changes and how places connect, read from the beats.
        </p>
        @if (build(); as build) {
          <div class="se-world-status">
            <span class="se-world-state" [attr.data-state]="build.state">{{
              stateLabel[build.state]
            }}</span>
            @if (active()) {
              <span class="se-world-meta">{{ progressLabel() }}</span>
            } @else if (build.state === 'finished') {
              <span class="se-world-meta"
                >{{ build.partCount }} parts · {{ build.objectCount }} objects ·
                {{ build.observationCount }} observations</span
              >
            }
          </div>
          @if (active()) {
            <div class="se-world-bar" [class.is-waiting]="build.scenesTotal === 0">
              <div class="se-world-bar-fill" [style.width.%]="percent()"></div>
            </div>
          }
          @if (build.message) {
            <p class="se-world-message">{{ build.message }}</p>
          }
          <p class="se-world-meta">
            {{ build.state === 'finished' ? 'Cost' : 'Spent so far' }} {{ usd(build.costUsd) }}
          </p>
        } @else if (!loading()) {
          <p class="se-world-message">This story has not been read yet.</p>
        }
        @if (problem()) {
          <p class="se-error">{{ problem() }}</p>
        }
        <div class="flex gap-1.5">
          @if (active()) {
            <button type="button" class="se-btn se-btn-danger" [disabled]="busy()" (click)="stop()">
              Stop
            </button>
          } @else {
            <button
              type="button"
              class="se-btn se-btn-primary"
              [disabled]="busy() || loading()"
              (click)="start()"
            >
              {{ build() ? 'Build again' : 'Build world model' }}
            </button>
          }
        </div>
      </div>
    }
  `,
})
export class WorldModelMenu {
  private readonly api = inject(WorldModelApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly storyId = input.required<string>();

  protected readonly stateLabel = STATE_LABEL;
  protected readonly open = signal(false);
  protected readonly build = signal<WorldModelBuild | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly problem = signal('');
  protected readonly active = computed(() => isBuildActive(this.build()));
  protected readonly percent = computed(() => {
    const build = this.build();
    return build && build.scenesTotal > 0
      ? Math.round((build.scenesDone / build.scenesTotal) * 100)
      : 0;
  });
  protected readonly progressLabel = computed(() => {
    const build = this.build();
    if (!build) {
      return '';
    }
    return build.state === 'queued' || build.scenesTotal === 0
      ? STATE_LABEL[build.state]
      : `${build.scenesDone} of ${build.scenesTotal} scenes`;
  });

  private pollTimer: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopPolling());
    // The real API needs the session cookie, so nothing is asked during SSR.
    effect(() => {
      const storyId = this.storyId();
      if (this.isBrowser && storyId) {
        void this.load(storyId);
      }
    });
  }

  protected toggle(): void {
    this.open.update((open) => !open);
  }

  protected onDocumentDown(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  protected async start(): Promise<void> {
    await this.run(() => this.api.startBuild(this.storyId()), 'The build could not be started.');
  }

  protected async stop(): Promise<void> {
    const build = this.build();
    if (build) {
      await this.run(() => this.api.cancelBuild(build.id), 'The build could not be stopped.');
    }
  }

  protected usd(amount: number): string {
    if (amount === 0) {
      return '$0';
    }
    return amount < 0.01 ? `$${amount.toFixed(4)}` : `$${amount.toFixed(2)}`;
  }

  private async run(call: () => Promise<WorldModelBuild>, fallback: string): Promise<void> {
    this.busy.set(true);
    this.problem.set('');
    try {
      this.take(await call());
    } catch (err) {
      this.problem.set(describeError(err, fallback));
    } finally {
      this.busy.set(false);
    }
  }

  private async load(storyId: string): Promise<void> {
    this.stopPolling();
    this.build.set(null);
    this.loading.set(true);
    this.problem.set('');
    try {
      const build = await this.api.getLatestBuild(storyId);
      if (this.storyId() === storyId) {
        this.take(build);
      }
    } catch (err) {
      this.problem.set(describeError(err, 'The world model could not be looked up.'));
    } finally {
      this.loading.set(false);
    }
  }

  private take(build: WorldModelBuild | null): void {
    this.build.set(build);
    this.stopPolling();
    if (isBuildActive(build) && this.isBrowser) {
      this.pollTimer = setTimeout(() => void this.poll(), POLL_MS);
    }
  }

  /** A failed poll is simply tried again on the next tick. */
  private async poll(): Promise<void> {
    this.pollTimer = null;
    const storyId = this.storyId();
    try {
      const build = await this.api.getLatestBuild(storyId);
      if (this.storyId() === storyId) {
        this.take(build);
        return;
      }
    } catch {
      // Kept as it was; the next tick asks again.
    }
    if (isBuildActive(this.build())) {
      this.pollTimer = setTimeout(() => void this.poll(), POLL_MS);
    }
  }

  private stopPolling(): void {
    if (this.pollTimer !== null) {
      clearTimeout(this.pollTimer);
      this.pollTimer = null;
    }
  }
}
