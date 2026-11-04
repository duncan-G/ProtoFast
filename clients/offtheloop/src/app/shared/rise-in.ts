/** Eases an element in, as if it just arrived. Browser only. */
export function riseIn(element: HTMLElement): void {
  const still = matchMedia('(prefers-reduced-motion: reduce)').matches;
  element.animate(
    still
      ? [{ opacity: 0 }, { opacity: 1 }]
      : [
          { opacity: 0, transform: 'translateY(8px)' },
          { opacity: 1, transform: 'none' },
        ],
    { duration: still ? 120 : 240, easing: 'cubic-bezier(0.2, 0.7, 0.2, 1)' },
  );
}
