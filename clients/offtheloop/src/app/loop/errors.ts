import { ConnectError } from '@connectrpc/connect';

/** The server's own words when it sent some; they are written for the user. */
export function messageOf(err: unknown, fallback: string): string {
  if (err instanceof ConnectError) {
    return err.rawMessage || fallback;
  }
  return fallback;
}
