import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import type { Paragraph, Result, SectionNode } from '../../../lib/gen/segmentation_pb';

/** The key-points payload, as `augment-key-points.schema.json` declares it. */
interface KeyPoints {
  paragraphId: string;
  points?: string[];
  terms?: { term: string; definition: string }[];
  question?: string;
}

/**
 * Per-paragraph output with its section path, and the reviewer's verdict.
 *
 * The section path is shown for every paragraph rather than only at section starts: an
 * augmentation is a claim about one paragraph, and the reader needs to know where that paragraph
 * sat without scrolling back to find out.
 */
@Component({
  selector: 'app-augmentations-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, RouterLink],
  template: `
    <app-shell>
      @if (result(); as current) {
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="min-w-0">
            <h1 class="truncate text-2xl font-semibold tracking-tight">Key points</h1>
            <p class="mt-1 text-sm text-[var(--color-neutral-500)]">
              {{ current.augmentations.length }} of {{ current.paragraphs.length }} paragraphs
            </p>
          </div>
          <a [routerLink]="['/app/runs', runId, 'tree']" class="btn btn-secondary">Back to the tree</a>
        </div>

        @if (current.augmentations.length === 0) {
          <p class="mt-10 text-sm text-[var(--color-neutral-500)]">
            This run produced no augmentations. Tick “Extract key points” when you upload a
            document to get them.
          </p>
        } @else {
          <ul class="mt-8 space-y-4">
            @for (item of items(); track item.paragraphId) {
              <li
                class="rounded-[var(--radius-lg)] border border-[var(--color-divider)] bg-[var(--color-surface)] p-4"
              >
                <div class="flex flex-wrap items-baseline justify-between gap-2">
                  <p class="text-xs text-[var(--color-neutral-500)]">{{ item.path }}</p>
                  <span class="chip" [class.chip-warn]="item.needsAttention">
                    {{ item.verdict }}
                  </span>
                </div>

                <p class="mt-2 text-sm leading-relaxed text-[var(--color-neutral-300)]">
                  {{ item.text }}
                </p>

                @if (item.points.length > 0) {
                  <ul class="mt-3 space-y-1.5 text-sm">
                    @for (point of item.points; track $index) {
                      <li class="flex gap-2">
                        <span class="text-[var(--color-accent-400)]" aria-hidden="true">•</span>
                        <span>{{ point }}</span>
                      </li>
                    }
                  </ul>
                } @else {
                  <p class="mt-3 text-sm text-[var(--color-neutral-600)]">
                    This paragraph makes no separable claims.
                  </p>
                }

                @if (item.terms.length > 0) {
                  <dl class="mt-3 space-y-1 text-xs">
                    @for (term of item.terms; track term.term) {
                      <div class="flex gap-2">
                        <dt class="shrink-0 font-medium text-[var(--color-neutral-300)]">
                          {{ term.term }}
                        </dt>
                        <dd class="min-w-0 text-[var(--color-neutral-500)]">{{ term.definition }}</dd>
                      </div>
                    }
                  </dl>
                }

                @if (item.question) {
                  <p class="mt-3 text-xs text-[var(--color-neutral-500)]">
                    Answers: <span class="italic">{{ item.question }}</span>
                  </p>
                }
              </li>
            }
          </ul>
        }
      } @else if (error(); as message) {
        <p class="text-sm text-[#f0a3a3]" role="alert">{{ message }}</p>
      } @else {
        <p class="text-sm text-[var(--color-neutral-500)]">Loading…</p>
      }
    </app-shell>
  `,
  styles: `
    .chip {
      border-radius: 999px;
      border: 1px solid var(--color-divider);
      padding: 0.0625rem 0.5rem;
      font-size: 0.75rem;
      color: var(--color-neutral-500);
    }
    .chip-warn {
      border-color: #7a3a3a;
      color: #f0a3a3;
    }
  `,
})
export class AugmentationsPage {
  private readonly api = inject(SegmentationApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly result = signal<Result | null>(null);
  protected readonly error = signal<string | null>(null);

  protected readonly runId = this.route.snapshot.paramMap.get('runId') ?? '';

  protected readonly items = computed(() => {
    const current = this.result();
    if (!current) {
      return [];
    }

    const paragraphs = new Map<string, Paragraph>(
      current.paragraphs.map((p) => [p.paragraphId, p]),
    );
    const paths = sectionPaths(current.root);

    return current.augmentations.map((augmentation) => {
      const payload = parse(augmentation.json);

      return {
        paragraphId: augmentation.paragraphId,
        path: paths.get(augmentation.paragraphId) ?? '',
        text: paragraphs.get(augmentation.paragraphId)?.text ?? '',
        points: payload?.points ?? [],
        terms: payload?.terms ?? [],
        question: payload?.question ?? '',
        verdict: augmentation.reviewVerdict || 'unreviewed',
        // "regenerated" and "failed-review" both mean a reviewer disagreed with the first answer;
        // that is worth a reader's eye even though the output was kept.
        needsAttention: ['failed-review', 'regenerated', 'review-unavailable'].includes(
          augmentation.reviewVerdict,
        ),
      };
    });
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.result.set(await this.api.getResult(this.runId));
    } catch (cause) {
      this.error.set(
        cause instanceof Error ? cause.message : 'This run has not produced a result yet.',
      );
    }
  }
}

/** Paragraph id → "Methods › Data collection". */
function sectionPaths(root: SectionNode | undefined): Map<string, string> {
  const paths = new Map<string, string>();
  if (!root) {
    return paths;
  }

  const walk = (node: SectionNode, trail: string[]): void => {
    const here = [...trail, node.title];
    for (const id of node.paragraphIds) {
      paths.set(id, here.join(' › '));
    }
    for (const child of node.children) {
      walk(child, here);
    }
  };

  walk(root, []);
  return paths;
}

/**
 * The payload is schema-validated server-side before it is stored, so a parse failure here means
 * a type this client does not know about rather than corrupt data — which is why it degrades to
 * "no points" instead of showing an error.
 */
function parse(json: string): KeyPoints | null {
  try {
    return JSON.parse(json) as KeyPoints;
  } catch {
    return null;
  }
}
