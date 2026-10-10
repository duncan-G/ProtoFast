import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  input,
  linkedSignal,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { PictureRefinement } from '../../../direction/model/picture-refinement';
import { RefineReference } from '../refine-reference';
import { Autofocus } from './autofocus';

/**
 * A picture tile that opens the full picture in a modal over the page, or for a refinable picture,
 * a box to say what to change in it, beside a searchable pick of another picture to take it from.
 */
@Component({
  selector: 'app-picture-preview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Autofocus],
  host: { class: 'dr-picture is-image' },
  template: `
    <button
      type="button"
      class="dr-picture-open"
      [attr.aria-label]="'Enlarge ' + alt()"
      (click)="open()"
    >
      <img [src]="url()" [alt]="alt()" />
    </button>
    @if (refinable()) {
      <button
        type="button"
        class="dr-picture-refine"
        [attr.aria-label]="'Refine ' + alt()"
        title="Refine: say what to change and keep the rest"
        (click)="open(true)"
      >
        <svg viewBox="0 0 16 16" aria-hidden="true">
          <path d="M10.5 2.5l3 3-8 8H2.5v-3z" />
        </svg>
      </button>
    }
    <dialog
      #dialog
      class="dr-lightbox"
      [class.is-refining]="refining()"
      [attr.aria-label]="refining() ? 'Refine ' + alt() : alt()"
      (click)="dialog.close()"
    >
      @if (refining()) {
        <form class="dr-refine" (click)="$event.stopPropagation()" (submit)="submit($event)">
          <div class="dr-refine-head">
            <div class="dr-refine-title">
              <span class="dr-serif">Refine picture</span>
              <span class="dr-hint"
                >Say what to change; the rest of the picture stays as it is.</span
              >
            </div>
            <button type="submit" class="dr-btn is-dark" [disabled]="busy() || !draft().trim()">
              @if (busy()) {
                <span class="spinner" aria-hidden="true"></span>
              }
              {{ busy() ? 'Refining…' : 'Refine' }}
            </button>
            <button type="button" class="dr-link" (click)="dialog.close()">Close</button>
          </div>
          <div class="dr-refine-body">
            <div class="dr-refine-main">
              <textarea
                class="dr-input"
                rows="3"
                maxlength="2000"
                aria-label="What to change"
                [placeholder]="
                  chosen()
                    ? 'What to change. e.g. Put her in the coat from the reference'
                    : 'What to change. e.g. Make the radio red and turn Mara’s head toward the window'
                "
                [value]="draft()"
                [disabled]="busy()"
                (input)="draft.set($any($event.target).value)"
                (keydown.control.enter)="submit($event)"
                (keydown.meta.enter)="submit($event)"
                drAutofocus
              ></textarea>
              <div class="dr-refine-stage">
                <img [src]="url()" [alt]="alt()" [class.is-working]="busy()" />
              </div>
            </div>
            <section class="dr-refine-refs" aria-label="Reference picture">
              <div class="dr-refine-refs-head">
                <span class="dr-kicker">Reference</span>
                @if (chosen(); as chosen) {
                  <span class="dr-refine-refs-chosen">{{ chosen.label }}</span>
                  <button
                    type="button"
                    class="dr-link"
                    [disabled]="busy()"
                    (click)="referenceId.set(null)"
                  >
                    Clear
                  </button>
                }
              </div>
              <span class="dr-hint">Optional: a picture to take the change from.</span>
              <input
                type="search"
                class="dr-input"
                placeholder="Search by name or frame"
                aria-label="Search reference pictures"
                [value]="query()"
                (input)="query.set($any($event.target).value)"
                (keydown.enter)="$event.preventDefault()"
              />
              <div class="dr-refine-refs-list">
                @for (reference of matches(); track reference.assetId) {
                  <button
                    type="button"
                    class="dr-refine-ref"
                    [class.is-on]="reference.assetId === chosen()?.assetId"
                    [attr.aria-pressed]="reference.assetId === chosen()?.assetId"
                    [title]="reference.detail || reference.label"
                    [disabled]="busy()"
                    (click)="toggle(reference)"
                  >
                    <img [src]="reference.url" alt="" loading="lazy" />
                    <span class="dr-refine-ref-label">{{ reference.label }}</span>
                    <span class="dr-refine-ref-kind">{{ reference.kind }}</span>
                  </button>
                } @empty {
                  <p class="dr-hint">
                    {{
                      references().length
                        ? 'No pictures match.'
                        : 'No other pictures in the scene yet.'
                    }}
                  </p>
                }
              </div>
            </section>
          </div>
        </form>
      } @else {
        <img [src]="url()" [alt]="alt()" />
        <button type="button" class="dr-lightbox-close" aria-label="Close">✕</button>
      }
    </dialog>
  `,
})
export class PicturePreview {
  readonly url = input.required<string>();
  readonly alt = input('');
  readonly refinable = input(false);
  /** The picture is being drawn or refined. */
  readonly busy = input(false);
  /** Pictures the change may be taken from; this one is left out. */
  readonly references = input<readonly RefineReference[]>([]);
  /** What to change in the picture; the host edits it. */
  readonly refine = output<PictureRefinement>();

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  protected readonly refining = signal(false);
  /** Cleared when the picture changes: refined, or another picture shown here. */
  protected readonly draft = linkedSignal({ source: this.url, computation: () => '' });
  protected readonly referenceId = linkedSignal<string, string | null>({
    source: this.url,
    computation: () => null,
  });
  protected readonly query = signal('');
  private readonly offered = computed(() => this.references().filter((r) => r.url !== this.url()));
  protected readonly chosen = computed(
    () => this.offered().find((r) => r.assetId === this.referenceId()) ?? null,
  );
  protected readonly matches = computed(() => {
    const words = this.query().toLowerCase().split(/\s+/).filter(Boolean);
    return this.offered().filter((r) => {
      const text = `${r.label} ${r.kind} ${r.detail}`.toLowerCase();
      return words.every((w) => text.includes(w));
    });
  });

  open(refine = false): void {
    this.refining.set(refine && this.refinable());
    this.dialog().nativeElement.showModal();
  }

  protected submit(event: Event): void {
    event.preventDefault();
    const instructions = this.draft().trim();
    if (instructions && !this.busy()) {
      this.refine.emit({ instructions, referenceAssetId: this.chosen()?.assetId ?? null });
    }
  }

  protected toggle(reference: RefineReference): void {
    this.referenceId.set(reference.assetId === this.referenceId() ? null : reference.assetId);
  }
}
