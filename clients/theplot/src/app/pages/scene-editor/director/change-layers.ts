import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * A look described in layers that add up: what the library says, what the scene changes, and what
 * this frame adds on top. Only the last layer is written here; the ones above it read through.
 */
@Component({
  selector: 'app-change-layers',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-layers' },
  template: `
    <div class="dr-field-head">
      <span class="dr-kicker">{{ title() }}</span>
      <span class="flex-1"></span>
      @if (text()) {
        <button type="button" class="dr-link is-quiet" (click)="clear.emit()">clear</button>
      }
    </div>
    <div class="dr-layer-stack">
      @if (base()) {
        <div class="dr-layer">
          <span class="dr-layer-tag is-library">{{ baseTag() }}</span>
          <span class="dr-layer-text is-faint">{{ base() }}</span>
        </div>
      }
      @if (sceneText()) {
        <div class="dr-layer">
          <span class="dr-layer-tag is-scene">+ Scene</span>
          <span class="dr-layer-text">{{ sceneText() }}</span>
        </div>
      }
      <div class="dr-layer is-here">
        <span class="dr-layer-tag is-here">+ {{ hereTag() }}</span>
        <textarea
          rows="2"
          [attr.aria-label]="title() + ', ' + hereTag()"
          [placeholder]="placeholder()"
          maxlength="2000"
          [value]="text()"
          (change)="edit.emit($any($event.target).value.trim())"
        ></textarea>
      </div>
    </div>
    @if (hint()) {
      <span class="dr-hint">{{ hint() }}</span>
    }
    @if (canPromote()) {
      <button type="button" class="dr-link self-start" (click)="promote.emit()">
        Move this change to all frames
      </button>
    }
    @if (frameNote()) {
      <div class="dr-note">
        <span class="flex-1">{{ frameNote() }}</span>
        <button type="button" class="dr-link is-quiet" (click)="clearFrames.emit()">
          remove them
        </button>
      </div>
    }
  `,
})
export class ChangeLayers {
  readonly title = input.required<string>();
  readonly base = input('');
  readonly baseTag = input('Library');
  /** Shown above a frame's own layer. */
  readonly sceneText = input('');
  /** "Frame 3" or "Scene". */
  readonly hereTag = input.required<string>();
  readonly text = input('');
  readonly placeholder = input('');
  readonly hint = input('');
  readonly canPromote = input(false);
  /** In the scene scope: which frames add their own changes. */
  readonly frameNote = input('');

  /** Not "change": the textarea's own change event bubbles out of the host. */
  readonly edit = output<string>();
  readonly clear = output<void>();
  readonly promote = output<void>();
  readonly clearFrames = output<void>();
}
