import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ConsoleShell } from '../../../admin-kit';

@Component({
  selector: 'app-platform',
  imports: [ConsoleShell],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <kit-console-shell title="ProtoFast Admin">
      <h1 class="text-2xl font-bold text-gray-900">Platform</h1>
      <p class="mt-2 text-gray-600">Engine-wide and cross-app tools will live here.</p>
    </kit-console-shell>
  `,
})
export class Platform {}
