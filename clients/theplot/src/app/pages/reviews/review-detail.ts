import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { TreeView } from '../../shared/tree-view';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import type { Paragraph, Result, ReviewTask, SectionNode } from '../../../lib/gen/segmentation_pb';

/**
 * The freeze gate itself: the proposed tree beside the document, the reviewer's findings inline,
 * and three ways out — approve, approve with notes, or reject.
 *
 * A rejection sends the run back to the structure phase with the notes as context, so the notes
 * field is not a comment box: it is the instruction the structurer gets on its second attempt.
 * The placeholder says so, because a reviewer who writes "wrong" has not helped anybody.
 */
@Component({
  selector: 'app-review-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, TreeView, FormsModule, RouterLink],
  template: `
    <app-shell [showReviews]="true">
      @if (task(); as current) {
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="min-w-0">
            <h1 class="truncate text-2xl font-semibold tracking-tight">{{ current.documentId }}</h1>
            <p class="mt-1 text-sm text-[var(--color-neutral-500)]">
              Check the structure before it is frozen.
            </p>
          </div>
          <a [routerLink]="['/app/runs', current.runId]" class="btn btn-secondary">Run details</a>
        </div>

        @if (current.findings.length > 0) {
          <section class="mt-6">
            <h2 class="text-sm font-medium text-[var(--color-neutral-400)]">
              What the reviewer flagged
            </h2>
            <ul class="mt-3 space-y-2 text-sm">
              @for (finding of current.findings; track $index) {
                <li
                  class="flex gap-2 rounded-[var(--radius-md)] border border-[var(--color-divider)] px-3 py-2"
                  [class.border-[#7a3a3a]]="finding.severity === 'high'"
                >
                  <span class="chip shrink-0" [class.chip-warn]="finding.severity === 'high'">
                    {{ finding.severity }}
                  </span>
                  <span class="min-w-0">
                    <span>{{ finding.message }}</span>
                    <span class="ml-1 font-mono text-xs text-[var(--color-neutral-600)]">
                      {{ finding.ids.join(', ') }}
                    </span>
                  </span>
                </li>
              }
            </ul>
          </section>
        }

        <div class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,20rem)_minmax(0,1fr)]">
          <aside class="lg:sticky lg:top-20 lg:max-h-[calc(100vh-7rem)] lg:overflow-y-auto">
            <h2 class="text-sm font-medium text-[var(--color-neutral-400)]">Proposed structure</h2>
            <div class="mt-3">
              @if (result()?.root; as root) {
                <app-tree-view [root]="root" (selected)="scrollTo($event)" />
              } @else {
                <p class="text-sm text-[var(--color-neutral-600)]">
                  The proposed tree is not readable yet — it is written at the freeze, and this run
                  has not reached it. Approve or reject from the findings above.
                </p>
              }
            </div>
          </aside>

          <article class="min-w-0 space-y-6">
            @for (section of flatSections(); track section.sectionId) {
              @if (section.paragraphIds.length > 0) {
                <section [id]="'section-' + section.sectionId">
                  <h3 class="text-sm font-medium tracking-wide text-[var(--color-neutral-400)] uppercase">
                    {{ section.title }}
                    @if (section.titleInferred) {
                      <span class="ml-1.5 normal-case text-[var(--color-neutral-600)]">(inferred)</span>
                    }
                  </h3>
                  @for (id of section.paragraphIds; track id) {
                    @if (paragraph(id); as p) {
                      <p class="mt-3 leading-relaxed">{{ p.text }}</p>
                    }
                  }
                </section>
              }
            } @empty {
              <p class="text-sm text-[var(--color-neutral-600)]">
                The document text is not available until the run freezes.
              </p>
            }
          </article>
        </div>

        <section
          class="mt-10 rounded-[var(--radius-lg)] border border-[var(--color-divider)] bg-[var(--color-surface)] p-5"
        >
          <h2 class="text-sm font-medium">Your decision</h2>

          <label for="notes" class="mt-4 block text-sm">Notes</label>
          <textarea
            id="notes"
            rows="3"
            class="field mt-1.5"
            [(ngModel)]="notes"
            placeholder="If you reject this, these notes go to the model as the instruction for its second attempt — so say what is wrong and where, not just that it is wrong."
          ></textarea>

          @if (error(); as message) {
            <p class="mt-3 text-sm text-[#f0a3a3]" role="alert">{{ message }}</p>
          }

          <div class="mt-4 flex flex-wrap gap-2">
            <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="decide('approve')">
              Approve
            </button>
            <button
              type="button"
              class="btn btn-secondary"
              [disabled]="busy() || notes.trim().length === 0"
              (click)="decide('approve_with_edits')"
            >
              Approve with notes
            </button>
            <button
              type="button"
              class="btn btn-secondary"
              [disabled]="busy() || notes.trim().length === 0"
              (click)="decide('reject')"
            >
              Reject and re-run
            </button>
          </div>
        </section>
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
    .field {
      display: block;
      width: 100%;
      border-radius: var(--radius-md);
      border: 1px solid var(--color-divider);
      background-color: var(--color-bg);
      padding: 0.5rem 0.75rem;
      font-size: 0.875rem;
      color: var(--color-text);
    }
  `,
})
export class ReviewDetailPage {
  private readonly api = inject(SegmentationApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly task = signal<ReviewTask | null>(null);
  protected readonly result = signal<Result | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected notes = '';

  private readonly reviewId = this.route.snapshot.paramMap.get('reviewId') ?? '';

  private readonly paragraphsById = computed(() => {
    const map = new Map<string, Paragraph>();
    for (const paragraph of this.result()?.paragraphs ?? []) {
      map.set(paragraph.paragraphId, paragraph);
    }
    return map;
  });

  protected readonly flatSections = computed(() => {
    const sections: SectionNode[] = [];
    const root = this.result()?.root;

    const walk = (node: SectionNode): void => {
      sections.push(node);
      node.children.forEach(walk);
    };

    if (root) {
      walk(root);
    }

    return sections;
  });

  constructor() {
    void this.load();
  }

  protected paragraph(id: string): Paragraph | undefined {
    return this.paragraphsById().get(id);
  }

  protected scrollTo(section: SectionNode): void {
    document
      .getElementById(`section-${section.sectionId}`)
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  protected async decide(
    decision: 'approve' | 'approve_with_edits' | 'reject',
  ): Promise<void> {
    if (this.busy()) {
      return;
    }

    this.busy.set(true);
    this.error.set(null);

    try {
      const run = await this.api.submitReviewDecision(this.reviewId, decision, this.notes.trim());
      await this.router.navigate(['/app/runs', run.runId]);
    } catch (cause) {
      this.error.set(
        cause instanceof Error ? cause.message : 'The decision could not be recorded.',
      );
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      const reply = await this.api.listReviews('pending');
      const found = reply.reviews.find((r) => r.reviewId === this.reviewId) ?? null;

      if (!found) {
        this.error.set('That review is no longer waiting for a decision.');
        return;
      }

      this.task.set(found);

      // The result only exists after the freeze, and this gate runs before it. A missing result
      // is the normal case, not an error — the findings and the outline are still reviewable.
      this.result.set(await this.api.getResult(found.runId).catch(() => null));
    } catch (cause) {
      this.error.set(cause instanceof Error ? cause.message : 'That review could not be loaded.');
    }
  }
}
