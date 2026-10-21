/** The research brief a run writes its story from, as the worker stores it. */
export interface Brief {
  title: string;
  moment: string;
  when: string;
  where: string;
  summary: string;
  stakes?: string;
  background?: { text: string; sources?: number[] }[];
  aftermath?: { text: string; sources?: number[] }[];
  people?: {
    name: string;
    role: string;
    description?: string;
    facts?: string[];
    sources?: number[];
  }[];
  timeline?: { when: string; what: string; sources?: number[] }[];
  places?: { name: string; description: string; sources?: number[] }[];
  details?: { text: string; sources?: number[] }[];
  quotes?: { speaker: string; words: string; context?: string; source: number }[];
  sources?: { passageId: number; why: string }[];
}

export function parseBrief(json: string): Brief | null {
  if (!json) {
    return null;
  }
  try {
    return JSON.parse(json) as Brief;
  } catch {
    return null;
  }
}
