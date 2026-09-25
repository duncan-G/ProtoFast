import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { transitionText } from './format';
import { LabelChips } from './label-chips';
import { RowActions } from './row-actions';
import { SceneEditorStore } from './scene-editor-store';
import { SceneRow } from './scene-row';

@Component({
  selector: 'app-transition-row',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LabelChips, RowActions],
  template: `
    @if (!editing()) {
      <div class="se-grid se-transition" (click)="store.editingId.set(row().element.id)">
        <div class="se-kicker">Transition</div>
        <div class="se-transition-line">
          @if (label(); as label) {
            <span class="se-transition-label">{{ label }}</span>
          } @else {
            <span class="se-transition-label se-missing">NO TRANSITION — PICK ONE</span>
          }
        </div>
      </div>
    } @else {
      <div class="se-grid py-1">
        <div class="se-kicker se-editing-label">Transition</div>
        <div class="se-card rise" (keydown.escape)="store.editingId.set(null)">
          <div class="se-card-head">
            <span class="se-kicker">How we move on</span>
            <app-row-actions
              (up)="store.moveElement(row().element.id, -1)"
              (down)="store.moveElement(row().element.id, 1)"
              (remove)="store.removeElement(row().element.id)"
              (done)="store.editingId.set(null)"
            />
          </div>
          <app-label-chips
            [labels]="store.transitions()"
            [selected]="row().element.transition"
            noun="transition"
            placeholder="e.g. WHIP PAN TO"
            [script]="true"
            [add]="addTransition"
            (pick)="store.updateElement(row().element.id, { transition: $event })"
          />
        </div>
      </div>
    }
  `,
})
export class TransitionRow {
  protected readonly store = inject(SceneEditorStore);

  readonly row = input.required<SceneRow>();
  readonly editing = input(false);

  protected readonly label = computed(() => {
    const transition = this.row().element.transition;
    return transition ? transitionText(transition) : null;
  });

  protected readonly addTransition = (label: string) => this.store.addTransition(label);
}
