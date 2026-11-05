const NICE = /\s*\((?:bonus|nice to have|preferred|a plus|plus)\)\s*$/i;

/** What a posting wants, split into must-haves and the nice-to-haves marked "(bonus)". */
export function splitWants(items: readonly string[]): { must: string[]; nice: string[] } {
  const must: string[] = [];
  const nice: string[] = [];
  for (const item of items) {
    if (NICE.test(item)) {
      nice.push(item.replace(NICE, ''));
    } else {
      must.push(item);
    }
  }
  return { must, nice };
}

export function joinWants(must: readonly string[], nice: readonly string[]): string[] {
  return [...must, ...nice.map((item) => `${item} (bonus)`)];
}
