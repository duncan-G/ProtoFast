import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { Framing, framingOf } from '../../../direction/inheritance';
import { DIRECTION_VOCABULARY, labelText } from '../../../direction/model/direction-vocabulary';
import { emptyDirection } from '../../../direction/model/scene-direction';
import { SceneEditorStore } from '../scene-editor-store';

const GROUPS: { key: Framing; title: string; labels: readonly string[] }[] = [
  { key: 'shot', title: 'Shot size', labels: DIRECTION_VOCABULARY.shots },
  { key: 'angle', title: 'Angle', labels: DIRECTION_VOCABULARY.angles },
  { key: 'movement', title: 'Movement', labels: DIRECTION_VOCABULARY.movements },
];

/** The camera for the selected panel, or the scene's default for every panel. */
@Component({
  selector: 'app-inspector-shot-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-tab-body' },
  template: `
    @for (group of groups(); track group.key) {
      <div class="dr-field">
        <div class="dr-field-head">
          <span class="dr-kicker">{{ group.title }}</span>
          <span class="flex-1"></span>
          @if (panel()) {
            @if (group.overridden) {
              <span class="dr-override"
                >override ·
                <button type="button" class="dr-link is-quiet" (click)="reset(group.key)">
                  reset
                </button></span
              >
            } @else {
              <span class="dr-inherited">from scene</span>
            }
          }
        </div>
        <div class="dr-pills" role="radiogroup" [attr.aria-label]="group.title">
          @for (label of group.labels; track label) {
            <button
              type="button"
              class="dr-pill"
              role="radio"
              [attr.aria-checked]="label === group.value"
              (click)="store.setFraming(group.key, label, panelKey())"
            >
              {{ text(label) }}
            </button>
          }
        </div>
      </div>
    }
    @if (panel(); as panel) {
      <div class="dr-field">
        <span class="dr-kicker">What’s visible</span>
        <textarea
          class="dr-input"
          rows="2"
          placeholder="e.g. MARA presses, BOLT deflects"
          maxlength="2000"
          [value]="panel.beat"
          (change)="store.updatePanel(panel.firstElementId, { beat: $any($event.target).value })"
        ></textarea>
        <div class="dr-hold">
          <span>Hold</span>
          <input
            class="dr-input dr-input-mono"
            type="number"
            min="1"
            step="250"
            placeholder="auto"
            aria-label="Hold, in milliseconds"
            [value]="panel.holdMs ?? ''"
            (change)="
              store.updatePanel(panel.firstElementId, {
                holdMs: +$any($event.target).value || null,
              })
            "
          />
          <span class="dr-hint">ms, for panels without lines</span>
        </div>
      </div>
    }
  `,
})
export class InspectorShotTab {
  protected readonly store = inject(SceneEditorStore);

  protected readonly panel = computed(() =>
    this.store.inspectorScope() === 'panel' ? (this.store.selectedPanel()?.panel ?? null) : null,
  );
  protected readonly panelKey = computed(() => this.panel()?.firstElementId ?? null);

  protected readonly groups = computed(() => {
    const direction = this.store.direction() ?? emptyDirection('');
    return GROUPS.map((group) => ({ ...group, ...framingOf(direction, this.panel(), group.key) }));
  });

  protected text(label: string): string {
    return labelText(label);
  }

  protected reset(key: Framing): void {
    const panelKey = this.panelKey();
    if (panelKey) {
      this.store.updatePanel(panelKey, { [key]: null });
    }
  }
}
