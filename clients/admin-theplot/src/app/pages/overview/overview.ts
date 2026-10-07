import { afterNextRender, ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import type { OverviewMetric } from '../../../lib/gen/Admin/Shared/admin_overview_pb';
import { APP, errorMessage, TheplotAdminApi } from '../../theplot-admin';

@Component({
  selector: 'app-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="text-2xl font-bold text-gray-900">Overview</h1>

    @if (error()) {
      <p class="mt-6 rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">{{ error() }}</p>
    }

    <dl class="mt-6 grid gap-4 sm:grid-cols-4">
      @for (metric of metrics(); track metric.label) {
        <div class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
          <dt class="text-sm text-gray-500">{{ metric.label }}</dt>
          <dd class="mt-1 text-2xl font-semibold text-gray-900">{{ metric.value }}</dd>
        </div>
      } @empty {
        @if (loading()) {
          <p class="text-sm text-gray-500">Loading…</p>
        }
      }
    </dl>
  `,
})
export class Overview {
  private readonly api = inject(TheplotAdminApi);

  protected readonly metrics = signal<OverviewMetric[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal('');

  constructor() {
    // The session cookie never reaches the SSR render, so the api is called from the browser.
    afterNextRender(() => void this.load());
  }

  private async load(): Promise<void> {
    try {
      this.metrics.set((await this.api.overview.getOverview({ app: APP })).metrics);
    } catch (err) {
      this.error.set(errorMessage(err));
    } finally {
      this.loading.set(false);
    }
  }
}
