import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Visual stand-in for artwork that hasn't been produced yet. Renders a dashed
 * frame with the description of the intended image so designers know exactly
 * what asset to drop in.
 */
@Component({
  selector: 'app-image-placeholder',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block h-full w-full' },
  template: `
    <figure
      class="flex h-full w-full flex-col items-center justify-center gap-4 rounded-2xl border-2 border-dashed border-slate-700 bg-slate-900/60 p-8 text-center"
    >
      <svg
        class="h-10 w-10 shrink-0 text-slate-600"
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
      <figcaption class="max-w-2xl text-sm leading-relaxed text-slate-400">
        <span class="mb-1 block text-xs font-semibold tracking-[0.2em] text-slate-500 uppercase">
          Image placeholder
        </span>
        {{ description() }}
      </figcaption>
    </figure>
  `,
})
export class ImagePlaceholder {
  readonly description = input.required<string>();
}
