# Proto codegen (buf + Connect)

Wires TypeScript gRPC client generation for an Angular client. Each
client under `clients/` gets its own `buf.gen.yaml` selecting which
service protos to generate. `«clientname»` is the lowercase client name.

---

## 3a. Install dependencies

From `clients/«clientname»/`:

```bash
npm install --save @connectrpc/connect @connectrpc/connect-web @bufbuild/protobuf
npm install --save-dev @bufbuild/buf @bufbuild/protoc-gen-es
```

Runtime deps:
- `@bufbuild/protobuf` — protobuf runtime for generated types.
- `@connectrpc/connect` — Connect RPC framework.
- `@connectrpc/connect-web` — gRPC-Web browser transport.

Dev deps:
- `@bufbuild/buf` — the buf CLI (runs `protoc-gen-es`).
- `@bufbuild/protoc-gen-es` — generates TypeScript from `.proto` files.
  In Connect v2, this single plugin generates both message types and
  service client stubs.

## 3b. Create `clients/«clientname»/buf.gen.yaml`

Each `inputs` entry points at a service's `Protos/` directory inside the
API project. After the standard layout refactor, proto files live at
`services/«servicename»/src/«ProjectName».«ServiceName».Api/Protos/`. To
narrow to specific files within a service, add `paths:` under that entry.

```yaml
version: v2
inputs:
  - directory: ../../services/«servicename1»/src/«ProjectName».«ServiceName1».Api/Protos
  - directory: ../../services/«servicename2»/src/«ProjectName».«ServiceName2».Api/Protos
plugins:
  - local: protoc-gen-es
    out: src/lib/gen
    opt: target=ts
```

Replace `«servicename1»`, `«servicename2»`, etc. with the actual service
folder names and `«ServiceName1»`, `«ServiceName2»` with their
PascalCase forms. To discover all services with protos:

```bash
find services/*/src/*/Protos -maxdepth 0 -type d 2>/dev/null
```

To narrow to specific proto files within a service:

```yaml
inputs:
  - directory: ../../services/api/src/«ProjectName».Api/Protos
    paths:
      - greet.proto
```

## 3c. Run initial generation

```bash
cd clients/«clientname»
npx buf generate
```

Verify output exists in `clients/«clientname»/src/lib/gen/`. For the
default `greet.proto`, this produces `greet_pb.ts` with typed schemas
and a `Greeter` service descriptor.

## 3d. Create the gRPC transport provider

Create `clients/«clientname»/src/app/grpc-transport.ts`:

```typescript
import { InjectionToken } from '@angular/core';
import { type Transport } from '@connectrpc/connect';
import { createGrpcWebTransport } from '@connectrpc/connect-web';

export const GRPC_TRANSPORT = new InjectionToken<Transport>('grpc-transport', {
  providedIn: 'root',
  factory: () =>
    createGrpcWebTransport({
      baseUrl:
        typeof window !== 'undefined'
          ? `${window.location.origin}/«routeprefix»`
          : '/«routeprefix»',
    }),
});
```

Replace `«routeprefix»` with the Envoy route prefix for the primary
backend this client talks to (e.g. `api` for the `/api/` route). For
clients that call multiple services behind different prefixes, create
additional transport tokens with those base URLs.

The transport works when the browser accesses Envoy at whatever port
Aspire assigned. `window.location.origin` picks up the correct host and
port automatically.

## 3e. Add generated code to `.gitignore`

If not already present, append to the repo-root `.gitignore`:

```
# --- Proto codegen (buf + Connect) ---
clients/*/src/lib/gen/
```

The glob covers all clients so this entry only needs to appear once.

---

## Example: wiring a Greeter component (bootstrap only)

During initial bootstrap, the `admin` client gets a proof-of-concept
Greeter form. This is optional for clients added post-bootstrap — the
user wires their own components.

Replace `clients/«clientname»/src/app/app.ts`:

```typescript
import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { createClient } from '@connectrpc/connect';
import { Greeter } from '../lib/gen/greet_pb';
import { GRPC_TRANSPORT } from './grpc-transport';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly greeter = createClient(Greeter, inject(GRPC_TRANSPORT));

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
```

Replace `clients/«clientname»/src/app/app.html` with:

```html
<main class="min-h-screen bg-gray-50 flex items-center justify-center p-4">
  <div class="w-full max-w-md bg-white rounded-2xl shadow-lg p-8 space-y-6">
    <h1 class="text-2xl font-bold text-gray-900 text-center">«ProjectName» Admin</h1>

    <form (submit)="greet($event)" class="space-y-4">
      <div>
        <label for="name" class="block text-sm font-medium text-gray-700 mb-1">Name</label>
        <input
          id="name"
          type="text"
          [value]="name()"
          (input)="name.set($any($event.target).value)"
          placeholder="Enter a name"
          class="w-full rounded-lg border border-gray-300 px-4 py-2 text-gray-900
                 focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 outline-none
                 transition" />
      </div>
      <button
        type="submit"
        [disabled]="loading()"
        class="w-full rounded-lg bg-indigo-600 px-4 py-2 text-white font-medium
               hover:bg-indigo-700 disabled:opacity-50 disabled:cursor-not-allowed
               transition">
        {{ loading() ? 'Sending…' : 'Greet' }}
      </button>
    </form>

    @if (reply()) {
      <p class="rounded-lg bg-green-50 border border-green-200 p-4 text-green-800">
        {{ reply() }}
      </p>
    }
    @if (error()) {
      <p class="rounded-lg bg-red-50 border border-red-200 p-4 text-red-800">
        {{ error() }}
      </p>
    }
  </div>
</main>

<router-outlet />
```

Replace `«ProjectName» Admin` with the appropriate heading for the
client being created.
