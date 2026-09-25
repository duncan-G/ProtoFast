import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { SceneElementType } from '../../stories/model/scene-element-type';

const OPTIONS: { type: SceneElementType; label: string; glyph: string }[] = [
  { type: 'Action', label: 'Action', glyph: '¶' },
  { type: 'Dialogue', label: 'Dialogue', glyph: '“' },
  { type: 'Description', label: 'Description', glyph: '◇' },
  { type: 'Narration', label: 'Narration', glyph: '~' },
  { type: 'Heading', label: 'Location', glyph: '⌖' },
  { type: 'Transition', label: 'Transition', glyph: '→' },
];

@Component({
  selector: 'app-insert-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'se-options' },
  template: `
    @for (option of options; track option.type) {
      <button type="button" class="se-btn" (click)="pick.emit(option.type)">
        <span class="se-option-glyph" aria-hidden="true">{{ option.glyph }}</span
        >{{ option.label }}
      </button>
    }
    @if (cancellable()) {
      <button type="button" class="se-btn se-btn-text text-[12.5px]" (click)="cancel.emit()">
        Cancel
      </button>
    }
  `,
})
export class InsertMenu {
  readonly cancellable = input(false);
  readonly pick = output<SceneElementType>();
  readonly cancel = output();

  protected readonly options = OPTIONS;
}
