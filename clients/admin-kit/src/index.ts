export { AuthIdentityService, type AuthIdentity } from './access/auth-identity';
export {
  CONSOLE_ROLES,
  FORBIDDEN_ROUTE,
  admits,
  consoleGuard,
  roleGuard,
} from './access/console-access';
export { Forbidden } from './pages/forbidden';
export { NotFound } from './pages/not-found';
export { AccountMenu } from './shell/account-menu';
export { ConsoleShell } from './shell/console-shell';
export { initBrowserTelemetry } from './telemetry/browser';
export { GRPC_TRANSPORT } from './transport/grpc-transport';
export { SERVER_URL } from './transport/server-url';
