import { afterNextRender, ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import {
  AdminOverview,
  type OverviewMetric,
} from '../../../lib/gen/Admin/Shared/admin_overview_pb';
import { AuthIdentityService, ConsoleShell, GRPC_TRANSPORT } from '../../../admin-kit';
import { CONSOLES, type ConsoleLink } from '../../consoles';

interface Destination {
  title: string;
  path: string;
  /** Null for the platform console, which has no single app's overview. */
  app: string | null;
}

/** The landing page: every console the operator's roles open. With just one, it goes straight there. */
@Component({
  selector: 'app-consoles',
  imports: [ConsoleShell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <kit-console-shell title="ProtoFast Admin">
      <h1 class="text-2xl font-bold text-gray-900">Consoles</h1>

      @if (destinations.length === 0) {
        <p class="mt-4 text-gray-600">Your account has no console roles.</p>
      }

      <ul class="mt-6 grid gap-4 sm:grid-cols-2">
        @for (destination of destinations; track destination.path) {
          <li class="rounded-2xl border border-gray-200 bg-white p-6 shadow-sm">
            <a
              [href]="destination.path"
              rel="external"
              class="text-lg font-semibold text-indigo-600 hover:underline"
            >
              {{ destination.title }}
            </a>
            @if (destination.app; as app) {
              <dl class="mt-4 grid grid-cols-2 gap-3 text-sm">
                @for (metric of metrics()[app] ?? []; track metric.label) {
                  <div>
                    <dt class="text-gray-500">{{ metric.label }}</dt>
                    <dd class="font-medium text-gray-900">{{ metric.value }}</dd>
                  </div>
                }
              </dl>
            } @else {
              <p class="mt-2 text-sm text-gray-600">Engine-wide and cross-app tools.</p>
            }
          </li>
        }
      </ul>
    </kit-console-shell>
  `,
})
export class Consoles {
  private readonly overview = createClient(AdminOverview, inject(GRPC_TRANSPORT));
  private readonly auth = inject(AuthIdentityService);

  protected readonly destinations: Destination[] = [
    ...CONSOLES.filter((c) => this.auth.hasRole(`admin-${c.app}`)).map(toDestination),
    ...(this.auth.hasRole('platform')
      ? [{ title: 'Platform', path: '/app/platform', app: null }]
      : []),
  ];

  protected readonly metrics = signal<Record<string, OverviewMetric[]>>({});

  constructor() {
    afterNextRender(() => {
      if (this.destinations.length === 1) {
        window.location.replace(this.destinations[0].path);
        return;
      }
      for (const { app } of this.destinations) {
        if (app) {
          void this.loadMetrics(app);
        }
      }
    });
  }

  private async loadMetrics(app: string): Promise<void> {
    try {
      const reply = await this.overview.getOverview({ app });
      this.metrics.update((all) => ({ ...all, [app]: reply.metrics }));
    } catch {
      // The card still links to the console; its counts are a convenience.
    }
  }
}

function toDestination(console: ConsoleLink): Destination {
  return { title: console.title, path: console.path, app: console.app };
}
