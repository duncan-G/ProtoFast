import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { plural, slugline } from '../format';
import { SceneEditorStore } from '../scene-editor-store';

/** Sits where the director view's toolbar does: the scene's slug and counts, and the scene itself. */
@Component({
  selector: 'app-story-toolbar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-toolbar' },
  template: `
    @if (confirming()) {
      <span class="st-confirm" role="alert"
        >Delete “{{ store.scene()?.title }}” and everything written in it?</span
      >
      <span class="flex-1"></span>
      <button type="button" class="dr-btn st-btn-danger" (click)="delete()">Delete scene</button>
      <button type="button" class="dr-btn" (click)="confirming.set(false)">Keep</button>
    } @else {
      <span class="dr-slug">{{ slug() }}</span>
      <span class="dr-toolbar-meta">{{ counts() }}</span>
      <span class="flex-1"></span>
      <button type="button" class="dr-btn" (click)="store.addScene(false)">+ Scene before</button>
      <button
        type="button"
        class="dr-btn"
        [title]="store.canDeleteScene() ? 'Delete this scene' : 'A story keeps at least one scene'"
        [disabled]="!store.canDeleteScene()"
        (click)="confirming.set(true)"
      >
        Delete scene
      </button>
    }
  `,
})
export class StoryToolbar {
  protected readonly store = inject(SceneEditorStore);

  protected readonly confirming = signal(false);

  protected readonly slug = computed(() => {
    const heading = this.store.elements().find((e) => e.type === 'Heading');
    const location = heading?.locationId ? this.store.locationById().get(heading.locationId) : null;
    if (!location) {
      return 'NO LOCATION';
    }
    return slugline(location) + (heading?.timeOfDay ? ` — ${heading.timeOfDay.toUpperCase()}` : '');
  });

  protected readonly counts = computed(() => {
    const elements = this.store.elements();
    const beats = elements.filter((e) => e.type !== 'Heading').length;
    const lines = elements.filter((e) => e.type === 'Dialogue').length;
    return `${plural(beats, 'beat')} · ${plural(lines, 'line')}`;
  });

  protected async delete(): Promise<void> {
    this.confirming.set(false);
    await this.store.deleteScene();
  }
}
