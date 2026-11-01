#!/usr/bin/env python3
"""Scaffold an app's admin console from clients/admin-theplot (docs/design/admin-consoles.md §6).

    scripts/scaffold-admin-console.py APP "Title"

APP is the app's realm name; the console mounts at /APP/ on the admin host. Writes the parts that
are files of their own — the console project, its proto folder and its card on the platform
console — and prints the platform wiring still to do by hand. ProtoStrap deploys it with
everything else; there is no pipeline of its own.
"""

import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "clients" / "admin-theplot"
RESERVED = {"app", "forbidden", "api", "account", "otlp", "payments", "signin", "signup", "signout",
            "signin-oidc", "reset", "add-passkey", "assets"}
CONFIG_FILES = [".editorconfig", ".gitignore", ".postcssrc.json", ".prettierrc", "angular.json",
                "package.json", "package-lock.json", "tsconfig.json", "tsconfig.app.json",
                "tsconfig.spec.json", "public", "src/index.html", "src/main.server.ts", "src/styles.scss",
                "src/app/app.config.ts", "src/app/app.config.server.ts", "src/app/app.routes.server.ts",
                "src/app/pages/overview"]


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    app, title = sys.argv[1], sys.argv[2]
    if not re.fullmatch(r"[a-z][a-z0-9-]*", app) or app in RESERVED:
        raise SystemExit(f"{app!r} must be a lowercase realm name and not one of the host's own paths")
    pascal = "".join(part.capitalize() for part in app.split("-"))
    console = ROOT / "clients" / f"admin-{app}"
    if console.exists():
        raise SystemExit(f"{console.relative_to(ROOT)} already exists")

    def rename(text):
        return (text.replace("admin-theplot", f"admin-{app}")
                .replace("/theplot/", f"/{app}/")
                .replace("ThePlot", title)
                .replace("Theplot", pascal)
                .replace("theplot", app))

    for name in CONFIG_FILES:
        source, target = TEMPLATE / name, console / name
        if source.is_dir():
            shutil.copytree(source, target)
            for file in target.rglob("*.ts"):
                file.write_text(rename(file.read_text()))
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(rename(source.read_text()))

    for name in ["buf.gen.yaml", "src/server.ts", "src/instrumentation.ts", "src/main.ts",
                 "src/app/app.spec.ts", "src/app/theplot-admin.ts"]:
        target = console / rename(name)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(rename((TEMPLATE / name).read_text()))

    (console / "src" / "admin-kit").symlink_to("../../admin-kit/src")
    (console / "src" / "app" / "app.ts").write_text(APP_TS.replace("{title}", title))
    (console / "src" / "app" / "app.routes.ts").write_text(ROUTES_TS)

    protos = ROOT / "services" / "api" / "src" / "ProtoFast.Api" / "Protos" / "Admin" / pascal
    protos.mkdir(parents=True, exist_ok=True)
    (protos / f"{app.replace('-', '_')}_admin.proto").write_text(
        PROTO.replace("{pascal}", pascal).replace("{app}", app).replace("{package}", app.replace("-", "_")))

    consoles = ROOT / "clients" / "admin" / "src" / "app" / "consoles.ts"
    consoles.write_text(consoles.read_text().replace(
        "];", f"  {{ app: '{app}', title: '{title}', path: '/{app}/' }},\n];"))

    print(f"Scaffolded clients/admin-{app}, its proto folder and console card.")
    print(CHECKLIST.replace("{app}", app).replace("{APP}", app.upper().replace("-", "_"))
          .replace("{pascal}", pascal))


APP_TS = """import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ConsoleShell } from '../admin-kit';

@Component({
  selector: 'app-root',
  imports: [ConsoleShell, RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <kit-console-shell title="{title} Admin">
      <a
        consoleNav
        routerLink="/"
        routerLinkActive="text-indigo-600"
        [routerLinkActiveOptions]="{ exact: true }"
        >Overview</a
      >
      <router-outlet />
    </kit-console-shell>
  `,
})
export class App {}
"""

ROUTES_TS = """import { Routes } from '@angular/router';
import { consoleGuard, FORBIDDEN_ROUTE } from '../admin-kit';

export const routes: Routes = [
  FORBIDDEN_ROUTE,
  {
    path: '',
    canActivateChild: [consoleGuard],
    children: [
      {
        path: '',
        loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview),
      },
      {
        path: '**',
        loadComponent: () => import('../admin-kit/pages/not-found').then((m) => m.NotFound),
      },
    ],
  },
];
"""

PROTO = """syntax = "proto3";

option csharp_namespace = "ProtoFast.Api.Admin.{pascal}";

package admin.{package};

// Read-only views for {app}'s console. Every RPC opens with AdminAccess.Require(context, "{app}").
service {pascal}Admin {
}
"""

CHECKLIST = """
Still to do by hand (platform-owned):
  1. services/shared/Grpc/AdminAccess.cs KnownApps and scripts/keycloak-grant-admin.py KNOWN_APPS: add "{app}".
  2. services/keycloak-config/realms/operators-realm.json: an admin-{app} role. In auth's appsettings.json,
     the admin tenant: RequiredRoles += admin-{app}, Consoles += "{app}": "admin-{app}" (auth's answer to
     the edge then turns away anyone without the role).
  3. services/api: implement {pascal}Admin (or drop the empty service from the proto, which the csproj
     must list), then fill src/app/{app}-admin.ts with its client.
  4. apphost/Program.cs: a DevServer("admin-{app}", "admin.dev.localhost", ["admin/{app}/"], ...) with
     ADMIN_CONSOLE_ROLES=admin-{app}, and a ClientsHost("clients-admin-{app}", "admin-{app}", ...) routed
     at admin/{app}/, both Authorized, as admin-theplot's are.
  5. clients/Dockerfile: a build stage for admin-{app} and the COPY of its dist; clients/host/host.mjs:
     CONSOLE_PATHS += '{app}'.
  6. cd clients/admin-{app} && npm install && npm run generate:grpc && npm test -- --watch=false
     && python3 ../../scripts/check-admin-console.py clients/admin-{app}   (commit src/lib/gen)
  7. aspire do protostrap-spec; commit the spec.
Then hand clients/admin-{app}/ to {app}'s agent.
"""

if __name__ == "__main__":
    main()
