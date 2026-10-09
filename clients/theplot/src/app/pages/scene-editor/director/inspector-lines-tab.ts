import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Cue } from '../../../direction/model/cue';
import { MAX_TAGS, tagsOf, tokenize } from '../../../direction/performance';
import { SceneElement } from '../../../stories/model/scene-element';
import { Avatar } from '../avatar';
import { initials } from '../format';
import { SceneEditorStore } from '../scene-editor-store';
import { Autofocus } from './autofocus';

const SUGGESTED = ['quietly', 'urgent', 'whispers', 'pause', 'laughs', 'sighs'];

type Token =
  | { kind: 'gap'; at: number }
  | { kind: 'tag'; text: string }
  | { kind: 'word'; text: string };

/** The line's words with a gap before each one and after the last, and its tags where they fall. */
function lineTokens(performance: string): Token[] {
  const out: Token[] = [{ kind: 'gap', at: 0 }];
  let words = 0;
  for (const token of tokenize(performance)) {
    if (token.kind === 'tag') {
      out.push({ kind: 'tag', text: token.text });
    } else {
      out.push({ kind: 'word', text: token.text });
      out.push({ kind: 'gap', at: ++words });
    }
  }
  return out;
}

/**
 * How each line is delivered: delivery cues placed between its words, the silence before it, and
 * whether the next line cuts it off. Action and narration pick their narrator here too.
 */
@Component({
  selector: 'app-inspector-lines-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Autofocus, Avatar],
  host: { class: 'dr-tab-body' },
  template: `
    <datalist id="dr-delivery-tags">
      @for (tag of store.deliveryTags(); track tag) {
        <option [value]="tag"></option>
      }
    </datalist>
    @for (line of lines(); track line.element.id) {
      <div class="dr-line-card">
        <div class="dr-line-head">
          @if (line.speaker; as speaker) {
            <app-avatar
              [shape]="store.kindOf(speaker).avatarShape"
              [hue]="speaker.hue"
              [initials]="initialsOf(speaker.name)"
              [size]="18"
            />
            <span class="dr-line-name hue-text" [style.--hue]="speaker.hue">{{
              speaker.name.toUpperCase()
            }}</span>
          } @else {
            <select
              class="dr-narrator is-inline"
              aria-label="Narrator"
              (change)="store.setNarrator(line.element.id, $any($event.target).value || null)"
            >
              <option value="" [selected]="!line.cue?.narratorId">
                {{ defaultNarrator() }} (default)
              </option>
              @for (n of otherNarrators(); track n.id) {
                <option [value]="n.id" [selected]="n.id === line.cue?.narratorId">
                  {{ n.name }}
                </option>
              }
            </select>
            <span class="dr-line-kind">{{ line.element.type }}</span>
          }
          <span class="flex-1"></span>
          <span class="dr-source">{{ source(line.cue) }}</span>
        </div>

        <div class="dr-tokens">
          @for (token of line.tokens; track $index) {
            @switch (token.kind) {
              @case ('gap') {
                <button
                  type="button"
                  class="dr-token-gap"
                  [class.is-active]="isPicking(line.element.id, token.at)"
                  title="Insert a cue here"
                  aria-label="Insert a cue here"
                  (click)="pick(line.element.id, token.at)"
                ></button>
                @if (isPicking(line.element.id, token.at)) {
                  <span class="dr-token-picker">
                    <input
                      #draft
                      list="dr-delivery-tags"
                      placeholder="cue…"
                      aria-label="Delivery cue"
                      maxlength="64"
                      (keydown.enter)="add(line.element.id, draft.value)"
                      (keydown.escape)="cancel()"
                      drAutofocus
                    />
                  </span>
                }
              }
              @case ('tag') {
                <button
                  type="button"
                  class="dr-cue is-removable"
                  title="Remove cue"
                  (click)="store.removeCueTag(line.element.id, token.text)"
                >
                  {{ token.text }} <span aria-hidden="true">×</span>
                </button>
              }
              @case ('word') {
                <span>{{ token.text }}</span>
              }
            }
          }
        </div>

        @if (picking()?.elementId === line.element.id) {
          <div class="dr-suggestions">
            @for (tag of suggested; track tag) {
              <button type="button" class="dr-suggestion" (click)="add(line.element.id, tag)">
                {{ tag }}
              </button>
            }
            <span class="flex-1"></span>
            <button type="button" class="dr-link is-quiet" (click)="cancel()">cancel</button>
          </div>
          @if (problem()) {
            <span class="dr-error">{{ problem() }}</span>
          }
        }

        <div class="dr-line-help">
          <span class="dr-hint">Click between words to place a cue · click a cue to remove</span>
          <span class="flex-1"></span>
          <button
            type="button"
            class="dr-link"
            [disabled]="store.isDirecting({ kind: 'cue', elementId: line.element.id })"
            (click)="store.direct({ kind: 'cue', elementId: line.element.id })"
          >
            Suggest
          </button>
        </div>

        <div class="dr-line-timing">
          <label class="dr-gap-field"
            >Gap
            <input
              class="dr-input dr-input-mono"
              type="number"
              step="100"
              [value]="line.cue?.gapBeforeMs ?? 0"
              (change)="
                store.updateCue(line.element.id, { gapBeforeMs: +$any($event.target).value || 0 })
              "
            />
            ms</label
          >
          <span class="flex-1"></span>
          <label class="dr-check">
            <input
              type="checkbox"
              [checked]="line.cue?.interrupted ?? false"
              (change)="
                store.updateCue(line.element.id, { interrupted: $any($event.target).checked })
              "
            />
            Cut off
          </label>
        </div>
      </div>
    } @empty {
      <p class="dr-empty">
        No lines in this {{ panel() ? 'panel' : 'scene' }}. Use <b>Hold</b> on the Shot tab to time
        it.
      </p>
    }
  `,
})
export class InspectorLinesTab {
  protected readonly store = inject(SceneEditorStore);

  protected readonly suggested = SUGGESTED;
  protected readonly picking = signal<{ elementId: string; at: number } | null>(null);
  protected readonly problem = signal('');

  protected readonly panel = computed(() =>
    this.store.inspectorScope() === 'panel' ? this.store.selectedPanel() : null,
  );
  protected readonly defaultNarrator = computed(
    () => this.store.narrators()[0]?.name ?? 'Narrator',
  );
  protected readonly otherNarrators = computed(() =>
    this.store
      .narrators()
      .slice(1)
      .filter((n) => n.id),
  );

  protected readonly lines = computed(() => {
    const span = this.panel();
    const elements = this.store.elements();
    const cues = this.store.cueByElement();
    const inRange = (element: SceneElement) => {
      const at = elements.indexOf(element);
      return !span || (at >= span.first && at <= span.last);
    };
    return this.store
      .spokenElements()
      .filter(inRange)
      .map((element) => {
        const cue = cues.get(element.id) ?? null;
        return {
          element,
          cue,
          speaker:
            element.type === 'Dialogue' && element.speakerId
              ? (this.store.characterById().get(element.speakerId) ?? null)
              : null,
          tokens: lineTokens(cue?.performance ?? (element.text ?? '').replace(/@/g, '')),
        };
      });
  });

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected source(cue: Cue | null): string {
    return !cue ? 'As written' : cue.edited ? 'By hand' : 'Directed';
  }

  protected isPicking(elementId: string, at: number): boolean {
    const picking = this.picking();
    return picking?.elementId === elementId && picking.at === at;
  }

  protected pick(elementId: string, at: number): void {
    this.problem.set('');
    this.picking.set(this.isPicking(elementId, at) ? null : { elementId, at });
  }

  protected cancel(): void {
    this.picking.set(null);
    this.problem.set('');
  }

  /** A tag the story does not know yet joins its vocabulary first. */
  protected async add(elementId: string, value: string): Promise<void> {
    const at = this.picking()?.at;
    const tag = value
      .trim()
      .replace(/^\[|\]$/g, '')
      .toLowerCase();
    if (at === undefined || !tag) {
      return;
    }
    if (!this.store.deliveryTags().includes(tag)) {
      const problem = await this.store.addDeliveryTag(tag);
      if (problem) {
        this.problem.set(problem);
        return;
      }
    }
    const cue = this.store.cueByElement().get(elementId);
    if (cue && tagsOf(cue.performance).length >= MAX_TAGS) {
      this.problem.set(`A line takes at most ${MAX_TAGS} cues.`);
      return;
    }
    if (!this.store.addCueTag(elementId, at, tag)) {
      this.problem.set('That cue is on the line already.');
      return;
    }
    this.cancel();
  }
}
