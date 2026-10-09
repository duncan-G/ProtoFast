import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  inject,
  output,
  signal,
} from '@angular/core';
import { FrameSpan, spanAt } from '../../../direction/frames';
import { tokenize } from '../../../direction/performance';
import { isSpoken } from '../../../direction/direction-changes';
import { SceneElement } from '../../../stories/model/scene-element';
import { SceneElementType } from '../../../stories/model/scene-element-type';
import { blockLength, canDrop } from '../../../stories/scene-blocks';
import { AudioState } from '../audio-state';
import { slugline, transitionText } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

interface ScriptRow {
  element: SceneElement;
  index: number;
  span: FrameSpan | null;
  number: number;
  isStart: boolean;
  isEnd: boolean;
  /** The row before it is in the same frame, so a split can go here. */
  inner: boolean;
}

const LABELS: Record<SceneElementType, string> = {
  Heading: 'Place',
  Action: 'Action',
  Description: 'Description',
  Narration: 'Narration',
  Dialogue: 'Dialogue',
  Transition: 'Transition',
};

const MORE: { type: SceneElementType; label: string }[] = [
  { type: 'Narration', label: 'Narration' },
  { type: 'Description', label: 'Description' },
  { type: 'Heading', label: 'Location' },
  { type: 'Transition', label: 'Transition' },
];

/**
 * The scene as the reader will get it, row by row, with the frames bracketed in the gutter. A row
 * picks its frame; the gap above a row splits its frame there or joins it to the one before.
 */
@Component({
  selector: 'app-director-script',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet],
  host: { class: 'dr-scroll', '(document:mousedown)': 'onDocumentDown($event)' },
  template: `
    <div class="dr-page" [class.is-scene]="store.inspectorScope() === 'scene'">
      @for (row of rows(); track row.element.id) {
        @if (row.index > 0 && row.span && dragFrom() === null) {
          @if (row.isStart) {
            <button
              type="button"
              class="dr-gap dr-gap-join"
              title="Join this frame to the one before"
              [disabled]="isDirecting(row) || isDirecting(rows()[row.index - 1])"
              (click)="store.mergeFrameUp(row.index)"
            >
              <span class="dr-gap-line"></span><span>Join frames</span>
            </button>
          } @else if (row.inner) {
            <button
              type="button"
              class="dr-gap dr-gap-split"
              [class.is-on]="isOn(row)"
              title="Start a new frame at this row"
              [disabled]="isDirecting(row)"
              (click)="store.splitFrameAt(row.index)"
            >
              <span class="dr-gap-rail"></span><span class="dr-gap-line"></span
              ><span>Split here</span>
            </button>
          }
        }
        @if (showsDrop(row.index)) {
          <div class="dr-drop">
            <span>{{ dragLabel() }}</span>
          </div>
        }
        <div
          class="dr-row"
          role="button"
          tabindex="0"
          [id]="'row-' + row.element.id"
          [class.is-on]="isOn(row)"
          [class.is-playing]="store.player.playing() === row.element.id"
          [class.is-start]="row.isStart"
          [class.is-end]="row.isEnd"
          [class.is-loose]="!row.span"
          [class.is-dragged]="isDragged(row.index)"
          [class.is-directing]="isDirecting(row)"
          [title]="row.span ? '' : 'No frame pictures this row — click to add one'"
          (click)="pick(row)"
          (keydown.enter)="pick(row)"
          (dragover)="onDragOver($event, row.index)"
          (drop)="onDrop($event)"
        >
          <div class="dr-bracket">
            @if (row.isStart) {
              <span class="dr-badge">{{ row.number }}</span>
            } @else if (!row.span) {
              <span class="dr-badge is-loose" aria-hidden="true">+</span>
            }
          </div>
          <div
            class="dr-handle"
            draggable="true"
            title="Drag to reorder"
            (click)="$event.stopPropagation()"
            (dragstart)="onDragStart($event, row)"
            (dragend)="endDrag()"
          >
            ⋮
          </div>
          <div class="dr-type">
            <span>{{ label(row.element.type) }}</span>
            @if (row.element.type === 'Action' || row.element.type === 'Narration') {
              @let narrator = cueOf(row.element.id)?.narratorId ?? '';
              <select
                class="dr-narrator"
                title="Narrator"
                aria-label="Narrator"
                [class.is-set]="narrator !== ''"
                (click)="$event.stopPropagation()"
                (change)="store.setNarrator(row.element.id, $any($event.target).value || null)"
              >
                <option value="" [selected]="narrator === ''">
                  {{ defaultNarrator() }} (default)
                </option>
                @for (n of otherNarrators(); track n.id) {
                  <option [value]="n.id" [selected]="n.id === narrator">{{ n.name }}</option>
                }
              </select>
            }
          </div>
          <div class="dr-text">
            @switch (row.element.type) {
              @case ('Heading') {
                <span class="dr-heading">{{ heading(row.element) }}</span>
              }
              @case ('Transition') {
                <span class="dr-transition">{{ transition(row.element) }}</span>
              }
              @case ('Dialogue') {
                <div class="dr-dialogue">
                  @let speaker = speakerOf(row.element);
                  <span class="dr-speaker hue-text" [style.--hue]="speaker?.hue ?? null">{{
                    speaker?.name?.toUpperCase() ?? 'UNKNOWN'
                  }}</span>
                  @if (row.element.parenthetical) {
                    <span class="dr-paren">({{ row.element.parenthetical }})</span>
                  }
                  <div class="dr-line">
                    <ng-container
                      [ngTemplateOutlet]="line"
                      [ngTemplateOutletContext]="{ $implicit: row.element }"
                    />
                  </div>
                </div>
              }
              @default {
                <div class="dr-action" [class.is-description]="row.element.type === 'Description'">
                  <ng-container
                    [ngTemplateOutlet]="line"
                    [ngTemplateOutletContext]="{ $implicit: row.element }"
                  />
                </div>
              }
            }
          </div>
          <div class="dr-audio">
            @if (spoken(row.element)) {
              @let state = audioOf(row.element.id);
              @let playing = store.player.playing() === row.element.id;
              <button
                type="button"
                class="dr-audio-btn"
                [attr.data-state]="playing ? 'playing' : state"
                [title]="audioTitle(state, playing)"
                [attr.aria-label]="audioTitle(state, playing)"
                (click)="$event.stopPropagation(); audio(row.element.id, state, playing)"
              >
                {{ audioIcon(state, playing) }}
              </button>
            }
          </div>
        </div>
      }
      <div class="dr-end-drop" (dragover)="onEndDragOver($event)" (drop)="onDrop($event)">
        @if (showsDrop(store.elements().length)) {
          <div class="dr-drop">
            <span>{{ dragLabel() }}</span>
          </div>
        }
      </div>

      <div class="dr-continue">
        <span class="dr-continue-kicker">Continue</span>
        <button type="button" class="dr-continue-btn" (click)="write.emit('Action')">Action</button>
        <button type="button" class="dr-continue-btn" (click)="write.emit('Dialogue')">
          Dialogue
        </button>
        <span class="dr-anchor">
          <button
            type="button"
            class="dr-continue-more"
            [attr.aria-expanded]="moreOpen()"
            (click)="moreOpen.set(!moreOpen())"
          >
            More ▾
          </button>
          @if (moreOpen()) {
            <div class="dr-menu dr-menu-up" role="menu">
              @for (option of more; track option.type) {
                <button
                  type="button"
                  class="dr-menu-item"
                  role="menuitem"
                  (click)="moreOpen.set(false); write.emit(option.type)"
                >
                  {{ option.label }}
                </button>
              }
            </div>
          }
        </span>
        <span class="dr-continue-hint">Opens the story mode to write it</span>
      </div>
    </div>

    <ng-template #line let-element>
      @let cue = cueOf(element.id);
      @if (cue) {
        @for (token of tokens(cue.performance); track $index) {
          @if (token.kind === 'tag') {
            <span class="dr-cue">{{ token.text }}</span>
          } @else {
            <span>{{ token.text }} </span>
          }
        }
      } @else {
        {{ plain(element.text) }}
      }
    </ng-template>
  `,
})
export class DirectorScript {
  protected readonly store = inject(SceneEditorStore);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly write = output<SceneElementType>();

  protected readonly more = MORE;
  protected readonly moreOpen = signal(false);
  protected readonly dragFrom = signal<number | null>(null);
  protected readonly dropAt = signal<number | null>(null);

  protected readonly rows = computed<ScriptRow[]>(() => {
    const spans = this.store.frameSpans();
    return this.store.elements().map((element, index) => {
      const span = spanAt(spans, index);
      return {
        element,
        index,
        span,
        number: span ? spans.indexOf(span) + 1 : 0,
        isStart: span?.first === index,
        isEnd: span?.last === index,
        inner: span !== null && span.first < index,
      };
    });
  });

  protected readonly defaultNarrator = computed(
    () => this.store.narrators()[0]?.name ?? 'Narrator',
  );
  protected readonly otherNarrators = computed(() =>
    this.store
      .narrators()
      .slice(1)
      .filter((n) => n.id),
  );

  private readonly dragLength = computed(() => {
    const from = this.dragFrom();
    return from === null ? 0 : blockLength(this.store.elements(), from);
  });
  protected readonly dragLabel = computed(() => {
    const beats = this.dragLength() - 1;
    return beats > 0 ? `Move location + ${beats} beat${beats === 1 ? '' : 's'} here` : 'Move here';
  });

  protected label(type: SceneElementType): string {
    return LABELS[type];
  }

  protected isOn(row: ScriptRow): boolean {
    return (
      row.span !== null &&
      (this.store.inspectorScope() === 'scene' || row.span === this.store.selectedFrame())
    );
  }

  protected isDirecting(row: ScriptRow): boolean {
    return this.store.isDirecting(
      row.span ? { kind: 'frame', frameId: row.span.frame.id } : { kind: 'frames' },
    );
  }

  protected pick(row: ScriptRow): void {
    if (row.span) {
      this.store.selectFrame(row.span.frame.firstElementId);
    } else {
      this.store.coverRow(row.index);
    }
  }

  protected cueOf(elementId: string) {
    return this.store.cueByElement().get(elementId) ?? null;
  }

  protected tokens(performance: string) {
    return tokenize(performance);
  }

  protected plain(text: string | null): string {
    return (text ?? '').replace(/@/g, '');
  }

  protected spoken(element: SceneElement): boolean {
    return isSpoken(element.type);
  }

  protected speakerOf(element: SceneElement) {
    return element.speakerId ? (this.store.characterById().get(element.speakerId) ?? null) : null;
  }

  protected heading(element: SceneElement): string {
    const location = element.locationId ? this.store.locationById().get(element.locationId) : null;
    const slug = location ? slugline(location) : 'NO LOCATION';
    return element.timeOfDay ? `${slug} — ${element.timeOfDay.toUpperCase()}` : slug;
  }

  protected transition(element: SceneElement): string {
    return transitionText(element.transition ?? '');
  }

  protected audioOf(elementId: string): AudioState {
    return this.store.audioStates().get(elementId) ?? 'none';
  }

  protected audioIcon(state: AudioState, playing: boolean): string {
    if (playing) {
      return '❚❚';
    }
    return { generating: '…', none: '+', stale: '↻', ready: '▶' }[state];
  }

  protected audioTitle(state: AudioState, playing: boolean): string {
    if (playing) {
      return 'Stop';
    }
    return {
      generating: 'Speaking this line…',
      none: 'Generate audio for this line',
      stale: 'Changed since its audio was made — click to speak it again',
      ready: 'Play this line',
    }[state];
  }

  protected audio(elementId: string, state: AudioState, playing: boolean): void {
    if (playing) {
      this.store.player.stop();
    } else if (state === 'ready') {
      this.store.playLines([elementId], elementId);
    } else if (state !== 'generating') {
      void this.store.generateAudio([elementId]);
    }
  }

  // ─── drag to reorder ───────────────────────────────────────────────────

  protected onDragStart(event: DragEvent, row: ScriptRow): void {
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
    });
  }

  protected onDragOver(event: DragEvent, index: number): void {
    if (this.dragFrom() === null) {
      return;
    }
    event.preventDefault();
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    this.dropAt.set(event.clientY < rect.top + rect.height / 2 ? index : index + 1);
  }

  protected onEndDragOver(event: DragEvent): void {
    if (this.dragFrom() !== null) {
      event.preventDefault();
      this.dropAt.set(this.store.elements().length);
    }
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

  protected onDocumentDown(event: MouseEvent): void {
    const menu = this.host.nativeElement.querySelector('.dr-continue .dr-anchor');
    if (this.moreOpen() && !menu?.contains(event.target as Node)) {
      this.moreOpen.set(false);
    }
  }
}
