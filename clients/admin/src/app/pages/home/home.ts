import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { createClient } from '@connectrpc/connect';
import { Greeter } from '../../../lib/gen/greet_pb';
import { GRPC_TRANSPORT } from '../../grpc-transport';
import { AuthIdentityService } from '../../auth/auth-identity';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
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
