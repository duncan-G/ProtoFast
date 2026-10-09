import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** A picture tile that opens the full picture in a modal over the page. */
@Component({
  selector: 'app-picture-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'dr-picture is-image' },
  template: `
    <button
      type="button"
      class="dr-picture-open"
      [attr.aria-label]="'Enlarge ' + alt()"
      (click)="dialog.showModal()"
    >
      <img [src]="url()" [alt]="alt()" />
    </button>
    <dialog #dialog class="dr-lightbox" [attr.aria-label]="alt()" (click)="dialog.close()">
      <img [src]="url()" [alt]="alt()" />
      <button type="button" class="dr-lightbox-close" aria-label="Close">✕</button>
    </dialog>
  `,
})
export class PicturePreview {
  readonly url = input.required<string>();
  readonly alt = input('');
}
