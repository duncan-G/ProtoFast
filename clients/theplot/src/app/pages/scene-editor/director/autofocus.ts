import { afterNextRender, Directive, ElementRef, inject } from '@angular/core';

/** Focuses the element once it is on the page. */
@Directive({ selector: '[drAutofocus]' })
export class Autofocus {
  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef);
    afterNextRender(() => host.nativeElement.focus());
  }
}
