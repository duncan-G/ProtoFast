/** The API's column lengths. */
export const NAME_MAX = 255;
export const LABEL_MAX = 64;

/** Ignores case and surrounding space. */
export function sameName(a: string, b: string): boolean {
  return a.trim().localeCompare(b.trim(), undefined, { sensitivity: 'accent' }) === 0;
}

export function findByName<T extends { id: string; name: string }>(
  items: readonly T[],
  name: string,
  exceptId?: string,
): T | undefined {
  return items.find((item) => item.id !== exceptId && sameName(item.name, name));
}

export function nameProblem<T extends { id: string; name: string }>(
  items: readonly T[],
  name: string,
  noun: string,
  exceptId?: string,
): string | null {
  const trimmed = name.trim();
  if (!trimmed) {
    return `A ${noun} needs a name.`;
  }
  if (trimmed.length > NAME_MAX) {
    return `Names are limited to ${NAME_MAX} characters.`;
  }
  const clash = findByName(items, trimmed, exceptId);
  return clash ? `There’s already a ${noun} called “${clash.name}”.` : null;
}

export function labelProblem(labels: readonly string[], label: string): string | null {
  const trimmed = label.trim();
  if (!trimmed) {
    return 'Type a label first.';
  }
  if (trimmed.length > LABEL_MAX) {
    return `Labels are limited to ${LABEL_MAX} characters.`;
  }
  const clash = labels.find((l) => sameName(l, trimmed));
  return clash ? `“${clash}” is already on the list.` : null;
}
