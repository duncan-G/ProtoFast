import { ConnectError } from '@connectrpc/connect';

/** A problem worded for the user, found before anything reached the server. */
export class LoopError extends Error {}

/** The server's own words when it sent some; they are written for the user. */
export function messageOf(err: unknown, fallback: string): string {
  if (err instanceof ConnectError) {
    return err.rawMessage || fallback;
  }
  if (err instanceof LoopError) {
    return err.message;
  }
  return fallback;
}
