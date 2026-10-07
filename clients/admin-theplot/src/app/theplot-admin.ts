import { inject, Injectable } from '@angular/core';
import { createClient } from '@connectrpc/connect';
import { GRPC_TRANSPORT } from '../admin-kit';
import { AdminOverview } from '../lib/gen/Admin/Shared/admin_overview_pb';
import { TheplotAdmin } from '../lib/gen/Admin/Theplot/theplot_admin_pb';
import { TheplotFamilies, TheplotRuns } from '../lib/gen/Admin/Theplot/theplot_engine_pb';

export const APP = 'theplot';

@Injectable({ providedIn: 'root' })
export class TheplotAdminApi {
  private readonly transport = inject(GRPC_TRANSPORT);

  readonly overview = createClient(AdminOverview, this.transport);
  readonly theplot = createClient(TheplotAdmin, this.transport);
  readonly runs = createClient(TheplotRuns, this.transport);
  readonly families = createClient(TheplotFamilies, this.transport);
}

export function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}
