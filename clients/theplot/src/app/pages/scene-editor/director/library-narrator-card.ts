import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Narrator } from '../../../stories/model/narrator';
import { plural } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

/** A narration voice picked from the provider's library; the first is the story's default. */
@Component({
  selector: 'app-library-narrator-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-card is-open is-flat' },
  template: `
    <div class="dr-narrator-head">
      <span class="dr-voice-name flex-1">{{ narrator().name }}</span>
      @if (isDefault()) {
        <span class="dr-default">Default</span>
      } @else {
        <button
          type="button"
          class="dr-btn is-small"
          [disabled]="!narrator().id"
          (click)="store.makeDefaultNarrator(narrator().id)"
        >
          Make default
        </button>
      }
    </div>
    @if (!narrator().voice.voiceId) {
      <span class="dr-hint">
        No speech voice. Add a narrator from the voice library and make it the default.
      </span>
    } @else if (narrator().voice.description) {
      <span class="dr-voice-about">{{ narrator().voice.description }}</span>
    }
    <div class="dr-row-actions">
      <span class="dr-hint flex-1">{{ usage() }}</span>
      @if (!isDefault()) {
        <button type="button" class="dr-danger" (click)="store.removeNarrator(narrator().id)">
          Remove
        </button>
      }
    </div>
  `,
})
export class LibraryNarratorCard {
  protected readonly store = inject(SceneEditorStore);

  readonly narrator = input.required<Narrator>();
  readonly index = input.required<number>();

  protected readonly isDefault = computed(() => this.index() === 0);

  protected readonly usage = computed(() => {
    const ids = new Set(this.store.narrators().map((n) => n.id));
    const cues = this.store.cueByElement();
    const lines = this.store
      .spokenElements()
      .filter((e) => e.type !== 'Dialogue')
      .map((e) => cues.get(e.id)?.narratorId ?? null);
    const scene = (this.store.place()?.index ?? 0) + 1;
    if (this.isDefault()) {
      const count = lines.filter((id) => id === null || !ids.has(id)).length;
      return `Narrates ${plural(count, 'line')} in Scene ${scene}`;
    }
    const count = lines.filter((id) => id === this.narrator().id).length;
    return `Picked on ${plural(count, 'line')} in Scene ${scene}`;
  });
}
