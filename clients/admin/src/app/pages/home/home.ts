import { Component, afterNextRender, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AccountMenu } from '../../shared/account-menu';
import { createClient } from '@connectrpc/connect';
import { Greeter } from '../../../lib/gen/greet_pb';
import { GRPC_TRANSPORT } from '../../grpc-transport';
import { AuthIdentityService } from '../../auth/auth-identity';

@Component({
  selector: 'app-home',
  imports: [AccountMenu, RouterLink],
  templateUrl: './home.html',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class Home {
  private readonly greeter = createClient(Greeter, inject(GRPC_TRANSPORT));

  protected readonly auth = inject(AuthIdentityService);

  protected readonly name = signal('');
  protected readonly reply = signal('');
  protected readonly error = signal('');
  protected readonly loading = signal(false);

  /**
   * False until the browser has hydrated this component. SSR paints a form that *looks*
   * interactive well before the bundle lands, and a submit in that window is a plain browser
   * form submission: `preventDefault()` below cannot stop it, because event replay dispatches
   * `greet` only after the browser has already navigated. Gating the submit button keeps that
   * window inert — a disabled default button also suppresses Enter-key implicit submission —
   * instead of silently reloading the page.
   */
  protected readonly hydrated = signal(false);

  constructor() {
    afterNextRender(() => this.hydrated.set(true));
  }

  async greet(event: Event) {
    event.preventDefault();
    this.reply.set('');
    this.error.set('');
    this.loading.set(true);

    try {
      const res = await this.greeter.sayHello({ name: this.name() });
      this.reply.set(res.message);
    } catch (err) {
      this.error.set(err instanceof Error ? err.message : String(err));
    } finally {
      this.loading.set(false);
    }
  }
}
