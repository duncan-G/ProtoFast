import { EditorMode } from './editor-mode';

/** Script rows carry `row-{elementId}` in both modes, which is what keeps the script still. */
interface ScriptAnchor {
  id: string;
  offset: number;
}

let running: ViewTransition | null = null;

/**
 * Runs `update` (which must render synchronously) inside a view transition styled by the
 * `se-morph` rules in scene-editor.css: the script's top line holds where it was and every line
 * on screen in both modes glides to its new place. Resolves once the new mode is on the page.
 */
export function morphMode(to: EditorMode, update: () => void): Promise<void> {
  if (
    typeof document.startViewTransition !== 'function' ||
    matchMedia('(prefers-reduced-motion: reduce)').matches
  ) {
    update();
    return Promise.resolve();
  }
  const root = document.documentElement;
  const anchor = scriptAnchor();
  const shown = nameShownRows(null);
  root.classList.remove('se-to-story', 'se-to-director');
  root.classList.add('se-morph', `se-to-${to}`);
  let named: HTMLElement[] = [];
  const transition = document.startViewTransition(() => {
    update();
    restore(anchor);
    named = nameShownRows(new Set(shown.map((row) => row.id)));
  });
  running = transition;
  const done = () => {
    for (const row of [...shown, ...named]) {
      row.style.viewTransitionName = '';
    }
    if (running === transition) {
      running = null;
      root.classList.remove('se-morph', `se-to-${to}`);
    }
  };
  void transition.finished.then(done, done);
  return transition.updateCallbackDone;
}

function scroller(): Element | null {
  return document.querySelector('.scene-editor .dr-scroll');
}

/** Names the rows wholly on screen (of `only`, when given) so each glides as its own group. */
function nameShownRows(only: Set<string> | null): HTMLElement[] {
  const page = scroller();
  if (!page) {
    return [];
  }
  const box = page.getBoundingClientRect();
  const rows: HTMLElement[] = [];
  for (const row of page.querySelectorAll<HTMLElement>('[id^="row-"]')) {
    const rect = row.getBoundingClientRect();
    if (rect.top >= box.top && rect.bottom <= box.bottom && (!only || only.has(row.id))) {
      row.style.viewTransitionName = `se-${row.id}`;
      rows.push(row);
    }
  }
  return rows;
}

function scriptAnchor(): ScriptAnchor | null {
  const page = scroller();
  if (!page) {
    return null;
  }
  const top = page.getBoundingClientRect().top;
  for (const row of page.querySelectorAll('[id^="row-"]')) {
    const rect = row.getBoundingClientRect();
    if (rect.bottom > top) {
      return { id: row.id, offset: rect.top - top };
    }
  }
  return null;
}

function restore(anchor: ScriptAnchor | null): void {
  const page = scroller();
  const row = anchor && document.getElementById(anchor.id);
  if (!anchor || !page || !row || !page.contains(row)) {
    return;
  }
  const top = page.getBoundingClientRect().top;
  page.scrollTop += row.getBoundingClientRect().top - top - anchor.offset;
}
