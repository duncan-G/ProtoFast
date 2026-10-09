import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Narrator } from '../../../stories/model/narrator';
import { VoiceProfile } from '../../../stories/model/voice-profile';
import { plural } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

/** One voice that reads action and narration; the first is the story's default. */
@Component({
  selector: 'app-library-narrator-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-card is-open is-flat' },
  template: `
    <div class="dr-narrator-head">
      <input
        class="dr-bare-input"
        aria-label="Narrator name"
        maxlength="255"
        [value]="narrator().name"
        (change)="rename($any($event.target).value)"
      />
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
    <label class="dr-field">
      <span class="dr-kicker">Voice</span>
      <textarea
        class="dr-input is-soft"
        rows="2"
        placeholder="How it sounds, e.g. warm, low, unhurried"
        maxlength="1000"
        [value]="narrator().voice.description"
        (change)="voice({ description: $any($event.target).value.trim() })"
      ></textarea>
    </label>
    <label class="dr-field">
      <span class="dr-kicker">Speech voice</span>
      <select
        class="dr-input"
        (focus)="store.loadVoices()"
        (mousedown)="store.loadVoices()"
        (change)="voice({ voiceId: $any($event.target).value })"
      >
        <option value="" [selected]="!narrator().voice.voiceId">Provider default</option>
        @for (v of voices(); track v.id) {
          <option [value]="v.id" [selected]="v.id === narrator().voice.voiceId">
            {{ v.name }}
          </option>
        }
      </select>
    </label>
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

  /** The saved voice id stays choosable before the provider's list has loaded. */
  protected readonly voices = computed(() => {
    const listed = this.store.voices() ?? [];
    const own = this.narrator().voice.voiceId;
    return own && !listed.some((v) => v.id === own) ? [{ id: own, name: own }, ...listed] : listed;
  });

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

  protected rename(name: string): void {
    const next = name.trim();
    if (next && next !== this.narrator().name) {
      this.store.updateNarrator(this.index(), { name: next });
    }
  }

  protected voice(patch: Partial<VoiceProfile>): void {
    this.store.updateNarrator(this.index(), { voice: { ...this.narrator().voice, ...patch } });
  }
}
