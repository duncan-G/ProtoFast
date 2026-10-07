import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ConsoleShell } from '../admin-kit';

@Component({
  selector: 'app-root',
  imports: [ConsoleShell, RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <kit-console-shell title="ThePlot Admin">
      <a
        consoleNav
        routerLink="/"
        routerLinkActive="text-indigo-600"
        [routerLinkActiveOptions]="{ exact: true }"
        >Overview</a
      >
      <a consoleNav routerLink="/stories" routerLinkActive="text-indigo-600">Stories</a>
      <a consoleNav routerLink="/runs" routerLinkActive="text-indigo-600">Runs</a>
      <a consoleNav routerLink="/families" routerLinkActive="text-indigo-600">Families</a>
      <router-outlet />
    </kit-console-shell>
  `,
})
export class App {}
