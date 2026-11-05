import {
  afterNextRender,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Icon } from '../shared/icon';

let nextId = 0;

/** A list in full, in a panel at the side; Escape or the backdrop closes it. */
@Component({
  selector: 'app-side-sheet',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <dialog
      #dialog
      class="side-sheet"
      [class.is-wide]="wide()"
      [attr.aria-labelledby]="id"
      (close)="closed.emit()"
      (click)="closeOnBackdrop($event)"
    >
      <div class="side-sheet-head">
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0">
            <h2 class="side-sheet-title" [id]="id">
              {{ heading() }}
              @if (count() !== null) {
                <span class="badge">{{ count() }}</span>
              }
            </h2>
            @if (subtitle()) {
              <p class="quiet mt-0.5 text-[13px]">{{ subtitle() }}</p>
            }
          </div>
          <button
            type="button"
            class="btn btn-icon btn-raised h-10 min-h-10 w-10"
            aria-label="Close"
            (click)="close()"
          >
            <app-icon name="close" [size]="15" />
          </button>
        </div>
        <ng-content select="[sheet-tools]" />
      </div>
      <div class="side-sheet-body">
        <ng-content />
      </div>
      <ng-content select="[sheet-foot]" />
    </dialog>
  `,
})
export class SideSheet {
  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');

  readonly heading = input.required<string>();
  readonly count = input<number | null>(null);
  readonly subtitle = input('');
  readonly wide = input(false, { transform: booleanAttribute });
  readonly closed = output<void>();

  protected readonly id = `side-sheet-${nextId++}`;

  constructor() {
    afterNextRender(() => this.dialog().nativeElement.showModal());
  }

  close(): void {
    this.dialog().nativeElement.close();
  }

  protected closeOnBackdrop(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.close();
    }
  }
}
