import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AppShell } from '../../shared/app-shell';
import { AuthIdentityService } from '../../auth/auth-identity';
import { SegmentationApi } from '../../segmentation/segmentation-api';
import type { ReviewTask } from '../../../lib/gen/segmentation_pb';

/**
 * The freeze-gate queue (plan §9.10).
 *
 * A reviewer sees everyone's; anyone else sees only their own documents — which is what makes
 * this a page of ThePlot rather than a separate client. The filtering happens on the server, so
 * the list a non-reviewer gets is short because it is scoped, not because it is hidden.
 */
@Component({
  selector: 'app-reviews-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AppShell, RouterLink],
  template: `
    <app-shell [showReviews]="true">
      <h1 class="text-2xl font-semibold tracking-tight">Waiting for review</h1>
      <p class="mt-1 max-w-2xl text-sm text-[var(--color-neutral-400)]">
        @if (isReviewer()) {
          Documents whose structure needs a person before it is frozen. Every new kind of document
          is checked a few times; once a kind has been approved enough, it stops appearing here.
        } @else {
          Your documents that are waiting on a decision.
        }
      </p>

      @if (loading()) {
        <p class="mt-10 text-sm text-[var(--color-neutral-500)]">Loading…</p>
      } @else if (tasks().length === 0) {
        <p class="mt-10 text-sm text-[var(--color-neutral-500)]">Nothing is waiting.</p>
      } @else {
        <ul class="mt-8 space-y-2">
          @for (task of tasks(); track task.reviewId) {
            <li
              class="rounded-[var(--radius-lg)] border border-[var(--color-divider)] bg-[var(--color-surface)] p-4"
            >
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="min-w-0">
                  <a
                    [routerLink]="['/app/reviews', task.reviewId]"
                    class="text-base font-medium hover:text-[var(--color-accent-300)]"
                  >
                    {{ task.documentId }}
                  </a>
                  <p class="mt-1 flex flex-wrap items-center gap-x-3 text-xs text-[var(--color-neutral-500)]">
                    @if (task.family && task.family !== 'unknown') {
                      <span class="chip">{{ task.family }}</span>
                    }
                    <span>{{ waiting(task) }}</span>
                    @if (high(task) > 0) {
                      <span class="text-[#f0a3a3]">
                        {{ high(task) }} serious {{ high(task) === 1 ? 'finding' : 'findings' }}
                      </span>
                    }
                  </p>
                </div>

                <a [routerLink]="['/app/reviews', task.reviewId]" class="btn btn-secondary shrink-0">
                  Review
                </a>
              </div>
            </li>
          }
        </ul>
      }
    </app-shell>
  `,
  styles: `
    .chip {
      border-radius: 999px;
      border: 1px solid var(--color-divider);
      padding: 0.0625rem 0.5rem;
    }
  `,
})
export class ReviewsPage {
  private readonly api = inject(SegmentationApi);
  private readonly auth = inject(AuthIdentityService);

  protected readonly tasks = signal<ReviewTask[]>([]);
  protected readonly loading = signal(true);

  protected readonly isReviewer = computed(() =>
    this.auth.identity.roles.includes('segmentation-reviewer'),
  );

  constructor() {
    void this.load();
  }

  protected high(task: ReviewTask): number {
    return task.findings.filter((f) => f.severity === 'high').length;
  }

  protected waiting(task: ReviewTask): string {
    const seconds = Date.now() / 1000 - Number(task.createdUnixSeconds);
    if (seconds < 3600) {
      return `${Math.max(1, Math.round(seconds / 60))} min`;
    }
    return seconds < 86400
      ? `${Math.round(seconds / 3600)} h`
      : `${Math.round(seconds / 86400)} days`;
  }

  private async load(): Promise<void> {
    try {
      const reply = await this.api.listReviews('pending');
      this.tasks.set(reply.reviews);
    } finally {
      this.loading.set(false);
    }
  }
}
