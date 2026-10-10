import { ChangeDetectionStrategy, Component } from '@angular/core';
import { StoryEditor } from './story-editor';
import { StoryHints } from './story-hints';
import { StoryScript } from './story-script';
import { StoryToolbar } from './story-toolbar';

/** Story mode's centre column, built on the director view's frame so switching barely moves. */
@Component({
  selector: 'app-story-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StoryHints, StoryScript, StoryToolbar],
  providers: [StoryEditor],
  host: { class: 'dr-center st-view', 'aria-label': 'Scene' },
  template: `
    <app-story-toolbar />
    <app-story-script />
    <app-story-hints />
  `,
})
export class StoryView {}
