import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { ReferenceTarget } from '../../../stories/model/reference-target';
import { SceneEditorStore } from '../scene-editor-store';

const HINTS: Record<ReferenceTarget['kind'], string> = {
  character: 'A reference sheet drawn from the sheet; frames keep their face and build from it.',
  location: 'An empty establishing shot; frames here keep its layout from it.',
  prop: 'The prop on its own; frames keep its look from it.',
};

/** A library entry's picture, which every frame showing it is composed from. */
@Component({
  selector: 'app-library-picture',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-library-picture' },
  template: `
    @if (image(); as image) {
      <a class="dr-picture is-image" [href]="image.url" target="_blank" rel="noopener">
        <img [src]="image.url" [alt]="'Picture of ' + name()" />
      </a>
    } @else {
      <div class="dr-picture">
        {{ state() === 'generating' ? 'Drawing…' : 'No image' }}
      </div>
    }
    <div class="dr-library-picture-side">
      <span class="dr-kicker">Picture</span>
      <span class="dr-hint" [class.is-warn]="state() === 'stale'">{{ status() }}</span>
      <button
        type="button"
        class="dr-link self-start"
        [class.is-working]="state() === 'generating'"
        [disabled]="state() === 'generating'"
        (click)="store.generateLibraryImages([target()], state() === 'ready')"
      >
        {{ action() }}
      </button>
    </div>
  `,
})
export class LibraryPicture {
  protected readonly store = inject(SceneEditorStore);

  readonly target = input.required<ReferenceTarget>();
  readonly name = input('');

  protected readonly image = computed(
    () => this.store.libraryImages().get(this.target().id)?.image,
  );
  protected readonly state = computed(() => this.store.libraryImageState(this.target().id));
  protected readonly status = computed(() =>
    this.state() === 'stale' ? 'Out of date: the sheet changed since.' : HINTS[this.target().kind],
  );
  protected readonly action = computed(() => {
    switch (this.state()) {
      case 'generating':
        return 'Drawing…';
      case 'ready':
        return 'Regenerate';
      case 'stale':
        return 'Update';
      default:
        return 'Generate';
    }
  });
}
