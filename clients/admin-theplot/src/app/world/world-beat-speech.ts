import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  Injector,
  type OnDestroy,
  signal,
  viewChild,
} from '@angular/core';
import { usd } from '../engine/format';
import { errorMessage, TheplotAdminApi } from '../theplot-admin';
import { WorldSession } from './world-session';

interface BeatSpeech {
  url: string;
  voice: string;
  cost: bigint;
}

interface SpeechState {
  speech?: BeatSpeech;
  speaking?: boolean;
  error?: string;
}

/** Nothing is stored server side, so what was spoken lasts while the page is open. */
@Component({
  selector: 'app-world-beat-speech',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (beatId()) {
      @let state = current();
      <button
        type="button"
        class="rounded-lg border border-gray-300 bg-white px-2.5 py-1 text-xs text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        [disabled]="state.speaking"
        title="ElevenLabs reads this beat: dialogue in its speaker's voice, the rest in the narrator's"
        (click)="speak()"
      >
        @if (state.speaking) {
          Speaking…
        } @else {
          {{ state.speech ? 'Speak again' : 'Speak' }}
        }
      </button>

      @if (state.error) {
        <p class="mt-2 rounded-lg border border-red-200 bg-red-50 px-2 py-1.5 text-xs text-red-800">
          {{ state.error }}
        </p>
      }

      @if (state.speech; as speech) {
        <div class="mt-2">
          <audio #player class="h-8 w-full" controls [src]="speech.url"></audio>
          <p class="mt-0.5 text-[11px] text-gray-500">
            {{ speech.voice }} · {{ usd(speech.cost) }}
          </p>
        </div>
      }
    }
  `,
})
export class WorldBeatSpeech implements OnDestroy {
  private readonly api = inject(TheplotAdminApi);
  private readonly session = inject(WorldSession);
  private readonly injector = inject(Injector);
  private readonly player = viewChild<ElementRef<HTMLAudioElement>>('player');
  private readonly states = signal<ReadonlyMap<string, SpeechState>>(new Map());

  protected readonly usd = usd;

  protected readonly beatId = computed(() => {
    const world = this.session.world();
    const m = world?.moments[this.session.moment()];
    return m && m.beat >= 0 ? world.reply.scenes[m.scene].beats[m.beat].id : '';
  });

  protected readonly current = computed<SpeechState>(() => this.states().get(this.beatId()) ?? {});

  protected async speak(): Promise<void> {
    const storyId = this.session.world()?.reply.storyId;
    const beatId = this.beatId();
    if (!storyId || !beatId) {
      return;
    }
    this.patch(beatId, { speaking: true, error: '' });
    try {
      const reply = await this.api.worldModels.speakBeat({ storyId, beatId });
      this.revoke(this.states().get(beatId)?.speech?.url);
      const url = URL.createObjectURL(
        new Blob([reply.audio as Uint8Array<ArrayBuffer>], { type: reply.contentType }),
      );
      this.patch(beatId, { speech: { url, voice: reply.voiceName, cost: reply.costUsdMicros } });
      afterNextRender(
        () => {
          const player = this.player()?.nativeElement;
          if (player && this.beatId() === beatId) {
            player.play().catch(() => undefined);
          }
        },
        { injector: this.injector },
      );
    } catch (err) {
      this.patch(beatId, { error: errorMessage(err) });
    } finally {
      this.patch(beatId, { speaking: false });
    }
  }

  ngOnDestroy(): void {
    for (const state of this.states().values()) {
      this.revoke(state.speech?.url);
    }
  }

  private patch(beatId: string, change: Partial<SpeechState>): void {
    this.states.update((all) => new Map(all).set(beatId, { ...all.get(beatId), ...change }));
  }

  private revoke(url: string | undefined): void {
    if (url) {
      URL.revokeObjectURL(url);
    }
  }
}
