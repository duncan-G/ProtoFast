import {
  afterNextRender,
  booleanAttribute,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  Injector,
  input,
  signal,
} from '@angular/core';
import { Icon } from './icon';

export interface MenuItem {
  label: string;
  danger?: boolean;
  run: () => void;
}

/** The ⋯ button: the actions too rare or too final to sit on the page. */
@Component({
  selector: 'app-more-menu',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'more-menu',
    '(document:click)': 'outside($event)',
    '(keydown.escape)': 'close(true)',
  },
  template: `
    <button
      type="button"
      class="btn btn-icon btn-raised"
      [class.btn-xs]="small()"
      [attr.aria-label]="label()"
      [attr.aria-expanded]="open()"
      (click)="toggle()"
    >
      <app-icon name="more" [size]="16" [weight]="2.6" />
    </button>
    @if (open()) {
      <div class="menu-list" role="group" [attr.aria-label]="label()">
        @for (item of items(); track item.label) {
          <button type="button" [class.is-danger]="item.danger" (click)="pick(item)">
            {{ item.label }}
          </button>
        }
      </div>
    }
  `,
})
export class MoreMenu {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  readonly label = input.required<string>();
  readonly items = input.required<readonly MenuItem[]>();
  readonly small = input(false, { transform: booleanAttribute });

  protected readonly open = signal(false);

  protected toggle(): void {
    this.open.set(!this.open());
    if (this.open()) {
      afterNextRender(
        () => this.host.nativeElement.querySelector<HTMLElement>('.menu-list button')?.focus(),
        { injector: this.injector },
      );
    }
  }

  protected pick(item: MenuItem): void {
    this.close(true);
    item.run();
  }

  protected outside(event: MouseEvent): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  protected close(refocus: boolean): void {
    if (!this.open()) {
      return;
    }
    this.open.set(false);
    if (refocus) {
      this.host.nativeElement.querySelector<HTMLElement>('button')?.focus();
    }
  }
}
