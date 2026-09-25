import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { mentionTarget, segmentText } from '../../stories/mentions';
import { SceneElementMention } from '../../stories/model/scene-element-mention';
import { SceneEditorStore } from './scene-editor-store';

/** Mentions draw as chips without their `@`. */
@Component({
  selector: 'app-mention-text',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (run of runs(); track $index) {
      <span [class]="run.className" [style.--hue]="run.hue" [attr.title]="run.title">{{
        run.text
      }}</span>
    }
  `,
})
export class MentionText {
  private readonly store = inject(SceneEditorStore);

  readonly text = input.required<string>();
  readonly mentions = input.required<SceneElementMention[]>();

  protected readonly runs = computed(() => {
    const characters = this.store.characterById();
    const locations = this.store.locationById();
    return segmentText({ text: this.text(), mentions: this.mentions() }).map((segment) => {
      if (!segment.mention) {
        return { text: segment.text, className: '', hue: null, title: null };
      }
      const target = mentionTarget(segment.mention);
      const text = segment.text.slice(1);
      switch (target.kind) {
        case 'character': {
          const character = characters.get(target.id);
          return {
            text,
            className: 'se-ref se-ref-character',
            hue: character?.hue ?? null,
            title: character?.kind ?? null,
          };
        }
        case 'location': {
          const location = locations.get(target.id);
          return {
            text,
            className: 'se-ref se-ref-location',
            hue: location?.hue ?? null,
            title: 'Location',
          };
        }
        case 'prop':
          return { text, className: 'se-ref se-ref-prop', hue: null, title: 'Prop' };
      }
    });
  });
}
