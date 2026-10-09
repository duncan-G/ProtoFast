import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { overrideCount } from '../../../direction/inheritance';
import { plural } from '../format';
import { InspectorScope } from '../inspector-scope';
import { InspectorTab } from '../inspector-tab';
import { SceneEditorStore } from '../scene-editor-store';
import { InspectorCastTab } from './inspector-cast-tab';
import { InspectorLinesTab } from './inspector-lines-tab';
import { InspectorPlaceTab } from './inspector-place-tab';
import { InspectorShotTab } from './inspector-shot-tab';

const TABS: { tab: InspectorTab; label: string }[] = [
  { tab: 'shot', label: 'Shot' },
  { tab: 'cast', label: 'Cast & props' },
  { tab: 'place', label: 'Place' },
  { tab: 'lines', label: 'Lines' },
];

const NO_PICTURES = 'Picture generation isn’t available yet';

/** Edits the selected panel, or the defaults every panel of the scene follows. */
@Component({
  selector: 'app-director-inspector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [InspectorCastTab, InspectorLinesTab, InspectorPlaceTab, InspectorShotTab],
  host: { class: 'dr-inspector', 'aria-label': 'Inspector' },
  template: `
    <div class="dr-inspector-top">
      <div class="dr-scope" role="tablist" aria-label="Edit">
        @for (scope of scopes(); track scope.value) {
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="store.inspectorScope() === scope.value"
            (click)="store.inspectorScope.set(scope.value)"
          >
            {{ scope.label }}
          </button>
        }
      </div>

      @if (scene()) {
        <div class="dr-inspector-title is-scene">
          <span class="dr-serif">All panels</span>
          <span class="dr-hint"
            >Scene defaults. Every panel follows these unless it overrides a field.</span
          >
        </div>
      } @else if (store.selectedPanel(); as span) {
        <div class="dr-inspector-title">
          <span class="dr-badge is-big">{{ store.selectedPanelNumber() }}</span>
          <span class="dr-serif">Panel {{ store.selectedPanelNumber() }}</span>
          <span class="dr-inspector-of"
            >of {{ store.panelSpans().length }} · {{ panelLines().length }} line{{
              panelLines().length === 1 ? '' : 's'
            }}</span
          >
          <span class="flex-1"></span>
          <button
            type="button"
            class="dr-step"
            aria-label="Previous panel"
            [disabled]="store.selectedPanelNumber() <= 1"
            (click)="store.stepPanel(-1)"
          >
            ‹
          </button>
          <button
            type="button"
            class="dr-step"
            aria-label="Next panel"
            [disabled]="store.selectedPanelNumber() >= store.panelSpans().length"
            (click)="store.stepPanel(1)"
          >
            ›
          </button>
        </div>
      }

      @if (scene()) {
        <div class="dr-media" [class.is-collapsed]="!summaryOpen()">
          <button
            type="button"
            class="dr-media-toggle"
            [attr.aria-expanded]="summaryOpen()"
            (click)="summaryOpen.set(!summaryOpen())"
          >
            <span class="dr-kicker">Generation</span>
            @if (!summaryOpen()) {
              <span class="dr-media-summary"
                >Images 0/{{ store.panelSpans().length }} · Audio {{ readyLines(allLines()) }}/{{
                  allLines().length
                }}</span
              >
            }
            <span class="flex-1"></span>
            <span class="dr-chev" aria-hidden="true">{{ summaryOpen() ? '▴' : '▾' }}</span>
          </button>
          @if (summaryOpen()) {
            <div class="dr-thumbs">
              @for (span of store.panelSpans(); track span.panel.firstElementId; let i = $index) {
                <button
                  type="button"
                  class="dr-thumb"
                  [title]="'Panel ' + (i + 1) + ' · no picture yet'"
                  (click)="store.selectPanel(span.panel.firstElementId)"
                ></button>
              }
            </div>
            @if (store.panelSpans().length > 0) {
              <div class="dr-legend">
                <span
                  ><span class="dr-legend-key"></span>{{ store.panelSpans().length }} missing</span
                >
              </div>
            }
            <div class="dr-media-row">
              <span class="dr-media-label">Images</span>
              <span class="flex-1"
                >0 of {{ plural(store.panelSpans().length, 'panel') }} ready</span
              >
              <button type="button" class="dr-link" disabled [title]="noPictures">Generate</button>
            </div>
            <div class="dr-media-row">
              <span class="dr-media-label">Audio</span>
              <span class="flex-1"
                >{{ readyLines(allLines()) }} of {{ plural(allLines().length, 'line') }} ready</span
              >
              <button
                type="button"
                class="dr-link"
                [disabled]="allLines().length === 0"
                (click)="generate(allLines())"
              >
                {{ audioAction(allLines()) }}
              </button>
            </div>
          }
        </div>
      } @else if (store.selectedPanel()) {
        <div class="dr-media is-panel">
          <div class="dr-picture" [title]="noPictures">No image</div>
          <span class="dr-media-label">Image</span>
          <span class="dr-media-value">Missing</span>
          <button type="button" class="dr-link" disabled [title]="noPictures">Generate</button>
          <span class="dr-media-label">Audio</span>
          <span
            class="dr-media-value"
            [class.is-warn]="readyLines(panelLines()) < panelLines().length"
            [title]="
              readyLines(panelLines()) + ' of ' + plural(panelLines().length, 'line') + ' ready'
            "
            >{{ readyLines(panelLines()) }} of {{ panelLines().length }} ready</span
          >
          <button
            type="button"
            class="dr-link"
            [class.is-done]="panelLines().length > 0 && missingAudio(panelLines()) === 0"
            [disabled]="panelLines().length === 0"
            (click)="generate(panelLines())"
          >
            {{ audioAction(panelLines(), 'Regenerate') }}
          </button>
          <button
            type="button"
            class="dr-pill-btn"
            [disabled]="!playingPanel() && readyLines(panelLines()) === 0"
            (click)="playPanel()"
          >
            {{ playingPanel() ? '❚❚ Stop' : '▶ Play panel' }}
          </button>
        </div>
      }

      <div class="dr-tabs" role="tablist">
        @for (t of tabs; track t.tab) {
          <button
            type="button"
            role="tab"
            [attr.aria-selected]="store.inspectorTab() === t.tab"
            (click)="store.inspectorTab.set(t.tab)"
          >
            {{ t.label }}
          </button>
        }
      </div>
    </div>

    <div class="dr-inspector-body">
      @if (!scene() && !store.selectedPanel()) {
        <div class="dr-empty">
          <p>No panels yet. Let the model split the scene, or picture it all in one panel.</p>
          <div class="dr-row-actions">
            <button
              type="button"
              class="dr-btn"
              [disabled]="store.isDirecting({ kind: 'panels' })"
              (click)="store.direct({ kind: 'panels' })"
            >
              Suggest panels
            </button>
            <button
              type="button"
              class="dr-btn"
              [disabled]="store.elements().length === 0"
              (click)="store.coverRow(0)"
            >
              One panel
            </button>
          </div>
        </div>
      } @else {
        @switch (store.inspectorTab()) {
          @case ('shot') {
            <app-inspector-shot-tab />
          }
          @case ('cast') {
            <app-inspector-cast-tab />
          }
          @case ('place') {
            <app-inspector-place-tab />
          }
          @case ('lines') {
            <app-inspector-lines-tab />
          }
        }
      }
    </div>

    <div class="dr-inspector-foot">
      @if (scene()) {
        <button
          type="button"
          class="dr-btn"
          title="The model splits the scene into panels; panels you edited keep their rows"
          [disabled]="store.isDirecting({ kind: 'panels' })"
          (click)="store.direct({ kind: 'panels' })"
        >
          @if (store.isDirecting({ kind: 'panels' })) {
            <span class="spinner" aria-hidden="true"></span>
          }
          Direct all panels
        </button>
        <span class="flex-1"></span>
        <span class="dr-hint">{{ plural(overrides(), 'panel override') }}</span>
      } @else if (store.selectedPanel(); as span) {
        <button
          type="button"
          class="dr-btn"
          [disabled]="
            !span.panel.id || store.isDirecting({ kind: 'panel', panelId: span.panel.id })
          "
          [title]="span.panel.id ? 'Have the model direct this panel' : 'Saving…'"
          (click)="store.direct({ kind: 'panel', panelId: span.panel.id })"
        >
          @if (store.isDirecting({ kind: 'panel', panelId: span.panel.id })) {
            <span class="spinner" aria-hidden="true"></span>
          }
          Suggest panel
        </button>
        <span class="flex-1"></span>
        <button
          type="button"
          class="dr-danger"
          [disabled]="span.first === 0"
          [title]="span.first === 0 ? 'The first panel has nothing before it' : ''"
          (click)="store.mergePanelUp(span.first)"
        >
          Join with previous
        </button>
      }
    </div>
  `,
})
export class DirectorInspector {
  protected readonly store = inject(SceneEditorStore);

  protected readonly tabs = TABS;
  protected readonly noPictures = NO_PICTURES;

  /** The All panels generation summary, folded away until asked for. */
  protected readonly summaryOpen = signal(false);
  protected readonly scene = computed(() => this.store.inspectorScope() === 'scene');
  protected readonly scopes = computed<{ value: InspectorScope; label: string }[]>(() => [
    { value: 'panel', label: `Panel ${this.store.selectedPanelNumber() || ''}`.trim() },
    { value: 'scene', label: 'All panels' },
  ]);

  protected readonly allLines = computed(() => this.store.spokenElements().map((e) => e.id));
  protected readonly panelLines = computed(() => {
    const span = this.store.selectedPanel();
    if (!span) {
      return [];
    }
    return this.store
      .elements()
      .slice(span.first, span.last + 1)
      .filter((e) => this.store.audioStates().has(e.id))
      .map((e) => e.id);
  });
  protected readonly playingPanel = computed(
    () =>
      this.store.player.context() !== null &&
      this.store.player.context() === this.store.selectedPanel()?.panel.firstElementId,
  );
  protected readonly overrides = computed(() =>
    this.store.panelSpans().reduce((n, s) => n + overrideCount(s.panel), 0),
  );

  protected plural(count: number, word: string): string {
    return plural(count, word);
  }

  protected readyLines(ids: readonly string[]): number {
    return ids.filter((id) => this.store.audioStates().get(id) === 'ready').length;
  }

  protected missingAudio(ids: readonly string[]): number {
    return ids.filter((id) => this.store.linesNeedingAudio().includes(id)).length;
  }

  protected audioAction(ids: readonly string[], regenerate = 'Regenerate all'): string {
    const need = this.missingAudio(ids);
    return need > 0 ? `Generate ${need}` : ids.length > 0 ? regenerate : 'Generate';
  }

  protected generate(ids: readonly string[]): void {
    const need = ids.filter((id) => this.store.linesNeedingAudio().includes(id));
    void this.store.generateAudio(need.length > 0 ? need : ids, need.length === 0);
  }

  protected playPanel(): void {
    const span = this.store.selectedPanel();
    if (this.playingPanel()) {
      this.store.player.stop();
    } else if (span) {
      this.store.playLines(this.panelLines(), span.panel.firstElementId);
    }
  }
}
