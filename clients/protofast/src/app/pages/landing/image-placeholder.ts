import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Visual stand-in for artwork that hasn't been produced yet — the Angular
 * counterpart of the design canvas's `<image-slot>`. Renders a dashed frame
 * carrying the description of the intended image so designers know exactly what
 * asset to drop in. Styled from the Nocturne tokens so the holes in the page
 * still read as part of the page.
 *
 * Class lists are written out in full per branch rather than assembled from
 * fragments — Tailwind only sees class names that appear literally in source.
 */
@Component({
  selector: 'app-image-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block h-full w-full' },
  template: `
    <figure
      [class]="
        shape() === 'circle'
          ? 'flex h-full w-full items-center justify-center rounded-full border border-dashed border-[var(--color-neutral-700)] bg-[color-mix(in_srgb,var(--color-surface)_60%,transparent)]'
          : 'flex h-full w-full flex-col items-center justify-center gap-3 rounded-[var(--radius-lg)] border border-dashed border-[var(--color-neutral-700)] bg-[color-mix(in_srgb,var(--color-surface)_60%,transparent)] p-4 text-center'
      "
      [attr.aria-label]="captioned() ? null : 'Image placeholder: ' + description()"
    >
      <svg
        [class]="
          shape() === 'circle'
            ? 'h-4 w-4 shrink-0 text-[var(--color-neutral-600)]'
            : 'h-9 w-9 shrink-0 text-[var(--color-neutral-600)]'
        "
        xmlns="http://www.w3.org/2000/svg"
        fill="none"
        viewBox="0 0 24 24"
        stroke-width="1.5"
        stroke="currentColor"
        aria-hidden="true"
      >
        <path
          stroke-linecap="round"
          stroke-linejoin="round"
          d="m2.25 15.75 5.159-5.159a2.25 2.25 0 0 1 3.182 0l5.159 5.159m-1.5-1.5 1.409-1.409a2.25 2.25 0 0 1 3.182 0l2.909 2.909m-18 3.75h16.5a1.5 1.5 0 0 0 1.5-1.5V6a1.5 1.5 0 0 0-1.5-1.5H3.75A1.5 1.5 0 0 0 2.25 6v12a1.5 1.5 0 0 0 1.5 1.5Zm10.5-11.25h.008v.008h-.008V8.25Zm.375 0a.375.375 0 1 1-.75 0 .375.375 0 0 1 .75 0Z"
        />
      </svg>

      <!-- A circle slot is an avatar-sized hole, and some rect slots (a 34px logo
           strip) are just as tight — neither has room for the caption, so the
           description moves to the frame's aria-label above. -->
      @if (captioned()) {
        <figcaption
          class="mt-0 max-w-2xl text-[13px] leading-relaxed text-[var(--color-neutral-500)]"
        >
          <span
            class="mb-1 block font-mono text-[10px] tracking-[0.14em] text-[var(--color-neutral-600)] uppercase"
          >
            Image placeholder
          </span>
          {{ description() }}
        </figcaption>
      }
    </figure>
  `,
})
export class ImagePlaceholder {
  readonly description = input.required<string>();
  readonly shape = input<'rect' | 'circle'>('rect');
  /** Set false for slots too short to hold the caption. Circles never show one. */
  readonly showCaption = input(true);

  protected readonly captioned = computed(() => this.shape() !== 'circle' && this.showCaption());
}
