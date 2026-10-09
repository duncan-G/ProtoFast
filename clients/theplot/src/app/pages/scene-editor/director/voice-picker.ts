import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  input,
  OnDestroy,
  OnInit,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { SpeechVoice } from '../../../direction/model/speech-voice';
import { VoiceQuery } from '../../../direction/model/voice-query';
import { SceneEditorStore } from '../scene-editor-store';

const SEARCH_DELAY_MS = 300;

/** Values are ElevenLabs' voice library labels; see `direction/voice-filters.ts`. */
const FILTERS: {
  key: 'gender' | 'age' | 'accent';
  label: string;
  options: { value: string; label: string }[];
}[] = [
  {
    key: 'gender',
    label: 'Gender',
    options: [
      { value: '', label: 'Any gender' },
      { value: 'male', label: 'Male' },
      { value: 'female', label: 'Female' },
      { value: 'neutral', label: 'Neutral' },
    ],
  },
  {
    key: 'age',
    label: 'Age',
    options: [
      { value: '', label: 'Any age' },
      { value: 'young', label: 'Young' },
      { value: 'middle_aged', label: 'Middle aged' },
      { value: 'old', label: 'Old' },
    ],
  },
  {
    key: 'accent',
    label: 'Accent',
    options: [
      { value: '', label: 'Any accent' },
      { value: 'american', label: 'American' },
      { value: 'british', label: 'British' },
      { value: 'australian', label: 'Australian' },
      { value: 'canadian', label: 'Canadian' },
      { value: 'irish', label: 'Irish' },
      { value: 'scottish', label: 'Scottish' },
      { value: 'indian', label: 'Indian' },
    ],
  },
];

/** The speech provider's voice library in a modal, searched and auditioned; picking one emits it. */
@Component({
  selector: 'app-voice-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #dialog
      class="dr-voice-picker"
      [attr.aria-label]="title()"
      (close)="closed.emit()"
      (click)="onBackdrop($event)"
    >
      <div class="dr-voice-picker-head">
        <span class="dr-kicker flex-1">{{ title() }}</span>
        <button type="button" class="dr-link is-quiet" (click)="dialog.close()">Done</button>
      </div>
      <input
        class="dr-input"
        type="search"
        placeholder="Search voices"
        aria-label="Search voices"
        [value]="query().search"
        (input)="search($any($event.target).value)"
      />
      <div class="dr-voice-filters">
        @for (filter of filters; track filter.key) {
          <select
            class="dr-input"
            [attr.aria-label]="filter.label"
            (change)="refine(filter.key, $any($event.target).value)"
          >
            @for (option of filter.options; track option.value) {
              <option [value]="option.value" [selected]="option.value === query()[filter.key]">
                {{ option.label }}
              </option>
            }
          </select>
        }
      </div>
      @let voices = results();
      @if (voices === null) {
        <p class="dr-hint">Loading voices…</p>
      } @else {
        <ul class="dr-voice-list">
          @for (voice of voices; track voice.id) {
            <li class="dr-voice">
              <div class="dr-narrator-head">
                <button
                  type="button"
                  class="dr-voice-play"
                  [disabled]="!voice.previewUrl"
                  [attr.aria-label]="(playing() === voice.id ? 'Stop ' : 'Play ') + voice.name"
                  (click)="preview(voice)"
                >
                  {{ playing() === voice.id ? '■' : '▶' }}
                </button>
                <span class="dr-voice-name flex-1">{{ voice.name }}</span>
                @if (chosen().has(voice.id)) {
                  <span class="dr-hint">{{ chosenLabel() }}</span>
                } @else {
                  <button type="button" class="dr-btn is-small" (click)="picked.emit(voice)">
                    {{ action() }}
                  </button>
                }
              </div>
              @if (voice.traits) {
                <span class="dr-hint">{{ voice.traits }}</span>
              }
              @if (voice.description) {
                <span class="dr-voice-about">{{ voice.description }}</span>
              }
            </li>
          } @empty {
            <li class="dr-empty">No voices match.</li>
          }
        </ul>
      }
    </dialog>
  `,
})
export class VoicePicker implements OnInit, OnDestroy {
  private readonly store = inject(SceneEditorStore);
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  readonly title = input('Pick a voice');
  readonly action = input('Pick');
  readonly chosenLabel = input('Picked');
  /** Voice ids already taken here; they show `chosenLabel` instead of `action`. */
  readonly chosen = input<ReadonlySet<string>>(new Set());
  /** Where the filters start, e.g. from a character's sheet. */
  readonly initial = input<Partial<VoiceQuery>>({});

  readonly picked = output<SpeechVoice>();
  readonly closed = output<void>();

  protected readonly filters = FILTERS;
  protected readonly query = signal<VoiceQuery>({
    search: '',
    gender: '',
    age: '',
    accent: '',
    narration: false,
  });
  /** Null while a search runs. */
  protected readonly results = signal<SpeechVoice[] | null>(null);
  protected readonly playing = signal<string | null>(null);

  private audio: HTMLAudioElement | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private request = 0;

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  ngOnInit(): void {
    this.query.update((q) => ({ ...q, ...this.initial() }));
    void this.run();
  }

  ngOnDestroy(): void {
    clearTimeout(this.searchTimer);
    this.audio?.pause();
  }

  protected search(search: string): void {
    this.query.update((q) => ({ ...q, search }));
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.run(), SEARCH_DELAY_MS);
  }

  protected refine(key: 'gender' | 'age' | 'accent', value: string): void {
    this.query.update((q) => ({ ...q, [key]: value }));
    clearTimeout(this.searchTimer);
    void this.run();
  }

  /** A click on the dialog itself, not its content, landed on the backdrop. */
  protected onBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.dialog().nativeElement.close();
    }
  }

  protected preview(voice: SpeechVoice): void {
    this.audio?.pause();
    if (this.playing() === voice.id) {
      this.playing.set(null);
      return;
    }
    const audio = new Audio(voice.previewUrl);
    audio.onended = () => this.playing.set(null);
    this.audio = audio;
    this.playing.set(voice.id);
    audio.play().catch(() => this.playing.set(null));
  }

  private async run(): Promise<void> {
    const request = ++this.request;
    this.results.set(null);
    const voices = await this.store.searchVoices({
      ...this.query(),
      search: this.query().search.trim(),
    });
    if (request === this.request) {
      this.results.set(voices);
    }
  }
}
