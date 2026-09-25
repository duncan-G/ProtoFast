import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { AvatarShape } from '../../stories/model/avatar-shape';

const PATHS: Record<AvatarShape, string> = {
  Circle: 'M50 0a50 50 0 1 1 0 100a50 50 0 1 1 0-100Z',
  Square: 'M9 0h82q9 0 9 9v82q0 9-9 9H9q-9 0-9-9V9q0-9 9-9Z',
  Squircle: 'M50 0C90 0 100 10 100 50S90 100 50 100 0 90 0 50 10 0 50 0Z',
  Teardrop: 'M50 0a50 50 0 0 1 50 50a50 50 0 0 1-50 50H10q-10 0-10-10V50A50 50 0 0 1 50 0Z',
  Pill: 'M32 16h36a34 34 0 0 1 0 68H32a34 34 0 0 1 0-68Z',
  Diamond: 'M50 0 100 50 50 100 0 50Z',
  Triangle: 'M50 3 99 93H1Z',
  Pentagon: 'M50 1 99 37 80 96H20L1 37Z',
  Hexagon: 'M50 0 94 25v50L50 100 6 75V25Z',
  Octagon: 'M30 0h40l30 30v40l-30 30H30L0 70V30Z',
  Star: 'M50 2 64 33 98 37 72 60 80 95 50 77 20 95 28 60 2 37 36 33Z',
  Shield: 'M50 0 96 14v34c0 28-19 45-46 52C23 93 4 76 4 48V14Z',
};

/** Initials offset in % of size, for shapes whose visual centre sits off the box's. */
const NUDGE: Partial<Record<AvatarShape, number>> = {
  Triangle: 16,
  Pentagon: 5,
  Star: 8,
  Shield: -3,
};
const TIGHT = new Set<AvatarShape>(['Diamond', 'Triangle', 'Star']);

@Component({
  selector: 'app-avatar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'avatar',
    '[class.is-empty]': 'hue() === null && !outline()',
    '[class.is-outline]': 'outline()',
    '[style.--hue]': 'hue()',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
    '[style.font-size.px]': 'fontSize()',
    'aria-hidden': 'true',
  },
  template: `
    <svg viewBox="-4 -4 108 108"><path [attr.d]="path()" /></svg>
    @if (initials()) {
      <span [style.top.%]="nudge()">{{ initials() }}</span>
    }
  `,
})
export class Avatar {
  readonly shape = input.required<AvatarShape>();
  readonly hue = input<number | null>(null);
  readonly initials = input('');
  readonly size = input(24);
  readonly outline = input(false);

  protected readonly path = computed(() => PATHS[this.shape()]);
  protected readonly fontSize = computed(
    () => this.size() * (TIGHT.has(this.shape()) ? 0.33 : 0.4),
  );
  protected readonly nudge = computed(() => NUDGE[this.shape()] ?? null);
}
