import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { mentionTarget, segmentText } from '../../stories/mentions';
import { SceneElementMention } from '../../stories/model/scene-element-mention';
import { SceneEditorStore } from './scene-editor-store';

/**
 * Mentions draw as chips without their `@`; tags highlight their text. `marks` keeps every
 * character in place, for the highlight layer behind a textarea.
 */
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
  readonly marks = input(false);

  protected readonly runs = computed(() => {
    const characters = this.store.characterById();
    const locations = this.store.locationById();
    const props = this.store.propById();
    const marks = this.marks();
    return segmentText({ text: this.text(), mentions: this.mentions() }).map((segment) => {
      const mention = segment.mention;
      if (!mention) {
        return { text: segment.text, className: '', hue: null, title: null };
      }
      const target = mentionTarget(mention);
      const character = target.kind === 'character' ? characters.get(target.id) : undefined;
      const location = target.kind === 'location' ? locations.get(target.id) : undefined;
      const hue = character?.hue ?? location?.hue ?? null;
      if (marks) {
        const style = mention.isTag ? 'se-mark se-mark-tag' : 'se-mark';
        return {
          text: segment.text,
          className: hue === null ? `${style} se-mark-plain` : style,
          hue,
          title: null,
        };
      }
      if (mention.isTag) {
        const name = character?.name ?? location?.name ?? props.get(target.id)?.name ?? '';
        return {
          text: segment.text,
          className: hue === null ? 'se-tag se-tag-plain' : 'se-tag',
          hue,
          title: name,
        };
      }
      const text = segment.text.slice(1);
      switch (target.kind) {
        case 'character':
          return {
            text,
            className: 'se-ref se-ref-character',
            hue,
            title: character?.kind ?? null,
          };
        case 'location':
          return { text, className: 'se-ref se-ref-location', hue, title: 'Location' };
        case 'prop':
          return { text, className: 'se-ref se-ref-prop', hue: null, title: 'Prop' };
      }
    });
  });
}
