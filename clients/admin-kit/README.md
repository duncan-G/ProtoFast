# admin-kit

The platform layer every admin console is built on (`docs/design/admin-consoles.md`). It is not a
package: each console links `src/admin-kit -> ../../admin-kit/src` and builds with
`"preserveSymlinks": true`, so Angular compiles the kit as the console's own source and resolves
`@angular/*` from the console's `node_modules`. The kit has no dependencies of its own.

- `index.ts`: browser-safe API — `AuthIdentityService`, `consoleGuard`, `roleGuard`,
  `FORBIDDEN_ROUTE`, `GRPC_TRANSPORT`, `ConsoleShell`, `AccountMenu`, `NotFound`, `Forbidden`,
  `initBrowserTelemetry`.
- `server/`: `createAdminServer` (static files, the sign-in and role gate, Angular SSR),
  `provideAdminConsoleServer`, `startServerTelemetry`.
- `styles.css`: import it after Tailwind so the kit's classes are generated.

The roles a console admits come from `ADMIN_CONSOLE_ROLES`, which the AppHost and compose set.
Changes here reach every console: they are ours to review, never an app agent's.
