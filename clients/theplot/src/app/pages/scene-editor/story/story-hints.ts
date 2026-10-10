import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { BlockField } from './block-field';
import { blockLabel, fieldsOf } from './block-types';
import { StoryEditor } from './story-editor';

const STEP_LABELS: Partial<Record<BlockField, string>> = {
  speaker: 'Speaker',
  ext: 'Extension',
  paren: 'Parenthetical',
  text: 'Line',
  setting: 'Setting',
  place: 'Place',
  time: 'Time',
};

const IDLE: [string, string][] = [
  ['click', 'write in any block'],
  ['/', 'blocks'],
  ['@', 'mention'],
  ['⌥↑↓', 'move'],
];

/** The keys the cursor's field answers to, where the director view has its player. */
@Component({
  selector: 'app-story-hints',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'st-hints' },
  template: `
    <div class="st-hint-pill">
      @if (mode(); as mode) {
        <span class="st-hint-mode">{{ mode }}</span>
      }
      @if (steps().length) {
        <span class="st-hint-steps">
          @for (step of steps(); track step.field; let last = $last) {
            <span [class.is-on]="step.on">{{ step.label }}</span>
            @if (!last) {
              <span aria-hidden="true">›</span>
            }
          }
        </span>
      }
      @if (mode()) {
        <span class="st-hint-rule" aria-hidden="true"></span>
      }
      @for (hint of shownHints(); track hint[0]) {
        <span class="st-hint"
          ><kbd>{{ hint[0] }}</kbd
          >{{ hint[1] }}</span
        >
      }
    </div>
  `,
})
export class StoryHints {
  private readonly editor = inject(StoryEditor);

  protected readonly mode = computed(() => {
    const element = this.editor.activeElement();
    return element ? blockLabel(element.type) : '';
  });

  protected readonly steps = computed(() => {
    const element = this.editor.activeElement();
    const field = this.editor.active()?.field;
    if (!element || (element.type !== 'Dialogue' && element.type !== 'Heading')) {
      return [];
    }
    return fieldsOf(element.type).map((f) => ({
      field: f,
      label: STEP_LABELS[f] ?? f,
      on: f === field,
    }));
  });

  /** The step trail takes the room of the last hint. */
  protected readonly shownHints = computed(() =>
    this.steps().length ? this.hints().slice(0, 3) : this.hints(),
  );

  private readonly hints = computed<[string, string][]>(() => {
    const element = this.editor.activeElement();
    const field = this.editor.active()?.field;
    if (!element || !field) {
      return IDLE;
    }
    if (field === 'text' && this.editor.textMenu()) {
      return [
        ['↑↓', 'choose'],
        ['↵', 'insert'],
        ['Esc', 'dismiss'],
      ];
    }
    switch (field) {
      case 'speaker':
        return [
          ['type', 'filter or add new'],
          ['↵', 'pick'],
          ['Tab', 'change type'],
          ['⌫', 'cancel'],
        ];
      case 'ext':
        return [
          ['←→', 'choose'],
          ['↵', 'confirm'],
          ['⌫', 'back'],
          ['Tab', 'change type'],
        ];
      case 'paren':
        return [
          ['↵', 'to line'],
          ['⌫', 'back'],
          ['Tab', 'change type'],
        ];
      case 'setting':
      case 'time':
        return [
          ['←→', 'choose'],
          ['↵', 'confirm'],
          ['⇧Tab', 'back'],
        ];
      case 'place':
        return [
          ['type', 'filter or add new'],
          ['↵', 'pick'],
        ];
      case 'trans':
        return [
          ['↑↓', 'choose'],
          ['↵', 'insert'],
        ];
    }
    if (element.type === 'Dialogue') {
      return [
        ['@', 'mention'],
        ['↵', 'new action'],
        ['⌫', 'back'],
        ['Tab', 'change type'],
      ];
    }
    if (!element.text) {
      return [
        ['/', 'blocks'],
        ['Tab', 'change type'],
        ['⌫', 'remove'],
      ];
    }
    return [
      ['/', 'blocks'],
      ['@', 'mention'],
      ['↵', 'new block'],
      ['Tab', 'change type'],
    ];
  });
}
