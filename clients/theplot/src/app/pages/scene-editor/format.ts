import { Location } from '../../stories/model/location';
import { LocationSetting } from '../../stories/model/location-setting';

export function initials(name: string): string {
  return (
    name
      .split(/\s+/)
      .filter(Boolean)
      .map((word) => word[0])
      .join('')
      .slice(0, 2)
      .toUpperCase() || '?'
  );
}

export function settingPrefix(setting: LocationSetting): string {
  return setting === 'Interior' ? 'INT.' : 'EXT.';
}

export function slugline(location: Location): string {
  return `${settingPrefix(location.setting)} ${location.name.toUpperCase()}`;
}

/** "FADE OUT." takes a full stop; a label already ending in punctuation keeps its own. */
export function transitionText(label: string): string {
  if (/[.:!?]$/.test(label)) {
    return label;
  }
  return /^FADE OUT$/i.test(label) ? `${label}.` : `${label}:`;
}

export function plural(count: number, word: string): string {
  return `${count} ${word}${count === 1 ? '' : 's'}`;
}

export function parseLocationQuery(query: string): {
  setting: LocationSetting | null;
  name: string;
} {
  const match = /^\s*(INT|EXT)\.?\s+(.*)$/i.exec(query);
  if (!match) {
    return { setting: null, name: query.trim() };
  }
  return {
    setting: match[1].toUpperCase() === 'INT' ? 'Interior' : 'Exterior',
    name: match[2].trim(),
  };
}

const HUES = [25, 255, 145, 85, 315, 200, 350, 110];

export function nextHue(count: number, offset = 0): number {
  return HUES[(count + offset) % HUES.length];
}
