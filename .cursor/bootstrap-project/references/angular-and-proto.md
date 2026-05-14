# Steps 4 & 5 — Angular `admin` client + proto codegen

Only `clients/admin/` is scaffolded during bootstrap. Each client
under `clients/` is its own standalone Angular project, not a
multi-project workspace.

---

## Step 4 — Scaffold the Angular `admin` client

### 4a. Generate the project

```bash
npx -y @angular/cli@latest new admin \
  --directory ./clients/admin \
  --style scss \
  --routing \
  --ssr \
  --skip-git \
  --package-manager npm \
  --ai-config cursor \
  --defaults
```

Per principle 2, if any flag is rejected with `Unknown argument: ...`,
drop **that** flag and re-run.

### 4b. Verify SSR scaffolded

- `ls clients/admin/angular.json`
- `ls clients/admin/src/server.ts` (path may vary by Angular version)
- `clients/admin/package.json` `scripts` includes a `serve:ssr:*`
  entry.
- `angular.json` `architect.build.options` includes both `server`
  and `ssr` keys.

If any is missing, re-run the generator with `--ssr` — don't try to
bolt SSR on after the fact.

### 4c. Patch the `start` and `build` scripts

Edit `clients/admin/package.json` scripts:

```json
"generate:grpc": "npx buf generate",
"start": "npm run generate:grpc && ng serve --host 0.0.0.0 --allowed-hosts",
"build": "npm run generate:grpc && ng build",
```

- `generate:grpc` runs `buf generate` using the client's
  `buf.gen.yaml` to produce TypeScript from protos.
- `--host 0.0.0.0` is Appendix B's binding rule for the dev server.
- `--allowed-hosts` is Appendix C's host-allow-list widening for the
  Envoy-fronted dev loop.
- Both `start` and `build` run codegen first so generated types are
  always current.

### 4d. Add the Angular dev-server proxy config

Create `clients/admin/proxy.conf.json` so the dev server (port 4200)
forwards gRPC-Web requests to Envoy (port 8080):

```json
{
  "/api/": {
    "target": "http://localhost:8080",
    "secure": false
  },
  "/auth/": {
    "target": "http://localhost:8080",
    "secure": false
  },
  "/payments/": {
    "target": "http://localhost:8080",
    "secure": false
  }
}
```

Then reference it in `clients/admin/angular.json` under the `serve`
architect target:

```json
"serve": {
  "builder": "@angular/build:dev-server",
  "options": {
    "proxyConfig": "proxy.conf.json"
  },
  ...
}
```

This makes the app work at both `http://localhost:4200` (via proxy)
and `http://localhost:8080` (direct through Envoy).

### 4e. Register the admin client as an Aspire JS resource

The `Aspire.Hosting.JavaScript` package provides `AddJavaScriptApp`.
Add it from inside `apphost/`:

```bash
cd apphost
aspire add javascript --non-interactive
```

This appends `#:package Aspire.Hosting.JavaScript@<version>` to
`apphost.cs`. Then register the client between the services and
Envoy:

```csharp
var admin = builder.AddJavaScriptApp("admin", "../clients/admin", "start");
```

The third argument is the npm script Aspire runs (the patched
`start` from 4c). Aspire auto-creates an `admin-installer`
resource that runs `npm install` first; it appears in the dashboard
as a separate row that reaches `Finished`.

---

## Step 5 — Wire proto codegen (buf + Connect)

Each Angular client generates its own TypeScript gRPC clients from
the backend `.proto` files using buf + Connect. This step wires
`clients/admin/`; future clients follow the same pattern.

### 5a. Install dependencies

From `clients/admin/`:

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
- `@bufbuild/protoc-gen-es` — generates TypeScript from `.proto`
  files. In Connect v2, this single plugin generates both message
  types and service client stubs.

### 5b. Create `clients/admin/buf.gen.yaml`

This file selects which service protos the admin client generates
TypeScript for. Each `inputs` entry points at a service's `Protos/`
directory. To narrow to specific files within a service, add `paths:`
under that entry.

```yaml
version: v2
inputs:
  - directory: ../../services/api/Protos
  - directory: ../../services/auth/Protos
  - directory: ../../services/payments/Protos
plugins:
  - local: protoc-gen-es
    out: src/lib/gen
    opt: target=ts
```

To add or remove a service, edit the `inputs:` list. To narrow to
specific proto files within a service:

```yaml
inputs:
  - directory: ../../services/api/Protos
    paths:
      - greet.proto
```

### 5c. Run initial generation

```bash
cd clients/admin
npx buf generate
```

Verify output exists in `clients/admin/src/lib/gen/`. For the
default `greet.proto`, this produces `greet_pb.ts` with typed
`HelloRequest`, `HelloReply` schemas and a `Greeter` service
descriptor.

### 5d. Create the gRPC transport provider

Create `clients/admin/src/app/grpc-transport.ts`:

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
          ? `${window.location.origin}/api`
          : '/api',
    }),
});
```

The `/api` prefix matches Envoy's route for the `api` cluster. The
transport works at both `localhost:4200` (Angular proxy forwards to
Envoy) and `localhost:8080` (direct to Envoy).

For services behind other Envoy prefixes (`/auth/`, `/payments/`),
create additional transport tokens with those base URLs as needed.

### 5e. Wire the component to call the Greeter service

Replace the default Angular template with a working greeter form.
`clients/admin/src/app/app.ts`:

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

Update `clients/admin/src/app/app.html` to include a form with a
name input, submit button, and reply/error display.

### 5f. Add generated code to `.gitignore`

Append to the root `.gitignore`:

```
# --- Proto codegen (buf + Connect) ---
clients/*/src/lib/gen/
```
