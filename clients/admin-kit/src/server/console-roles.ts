/**
 * The roles that open this console, from ADMIN_CONSOLE_ROLES (comma-separated; `*` for any
 * signed-in operator). The deployment sets it, so the console's own code cannot widen it.
 */
export function consoleRolesFromEnv(): string[] {
  return (process.env['ADMIN_CONSOLE_ROLES'] ?? '')
    .split(',')
    .map((role) => role.trim())
    .filter((role) => role.length > 0);
}
