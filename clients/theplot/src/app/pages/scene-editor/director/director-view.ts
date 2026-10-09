import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  output,
} from '@angular/core';
import { OverviewPanel } from '../overview-panel';
import { SceneEditorStore } from '../scene-editor-store';
import { SceneElementType } from '../../../stories/model/scene-element-type';
import { DirectorInspector } from './director-inspector';
import { DirectorLibrary } from './director-library';
import { DirectorScript } from './director-script';
import { DirectorToolbar } from './director-toolbar';
import { DirectorTransport } from './director-transport';

const LIBRARY_MIN_WIDTH = 1280;

/**
 * Director mode: the story library on the left, the scene read as panels in the middle with the
 * player under it, and the inspector for the selected panel or the whole scene on the right.
 */
@Component({
  selector: 'app-director-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    DirectorInspector,
    DirectorLibrary,
    DirectorScript,
    DirectorToolbar,
    DirectorTransport,
    OverviewPanel,
  ],
  host: { class: 'dr-view' },
  template: `
    <app-director-library />
    <section class="dr-center" aria-label="Scene">
      <app-director-toolbar />
      <app-director-script (write)="write.emit($event)" />
      <app-director-transport />
    </section>
    @if (overview()) {
      <app-overview-panel class="dr-overview" (jump)="jump.emit($event)" />
    } @else {
      <app-director-inspector />
    }
  `,
})
export class DirectorView {
  private readonly store = inject(SceneEditorStore);

  readonly overview = input(false);
  readonly jump = output<string>();
  /** A new line to write in story mode. */
  readonly write = output<SceneElementType>();

  constructor() {
    afterNextRender(() =>
      this.store.directorLibraryOpen.set(window.innerWidth >= LIBRARY_MIN_WIDTH),
    );
  }
}
