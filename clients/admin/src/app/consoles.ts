export interface ConsoleLink {
  /** The app's realm name; its operators hold `admin-{app}`. */
  app: string;
  title: string;
  path: string;
}

/** The app consoles mounted on this host. Envoy routes each path to that console's own SSR process. */
export const CONSOLES: readonly ConsoleLink[] = [
  { app: 'theplot', title: 'ThePlot', path: '/theplot/' },
];
