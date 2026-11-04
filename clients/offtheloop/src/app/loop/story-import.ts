import { isObject, parseAgentJson } from './agent-json';
import { Note } from './model/note';

export const STORY_FORMAT = 'offtheloop-stories/1';
export const STORY_FILE = 'offtheloop-stories.json';
export const MAX_STORIES = 50;

/** What interviewers at top employers listen for; a story's quality is its tag. */
export const STORY_QUALITIES: readonly { tag: string; shows: string }[] = [
  { tag: 'ownership', shows: 'took something on beyond my remit and saw it through to the end' },
  { tag: 'impact', shows: 'delivered a result that mattered to users or the business' },
  { tag: 'ambiguity', shows: 'made progress when the problem or the requirements were unclear' },
  { tag: 'tradeoffs', shows: 'weighed the options and made a technical call I can defend' },
  { tag: 'craft', shows: 'raised the bar on quality, reliability, testing or performance' },
  { tag: 'customer', shows: 'worked back from what users needed, not only what the ticket said' },
  { tag: 'learning', shows: 'got productive fast in unfamiliar code, tools or domains' },
  { tag: 'influence', shows: 'brought others round without authority, with data or a prototype' },
  { tag: 'conflict', shows: 'disagreed and worked it out constructively' },
  { tag: 'leadership', shows: 'set a direction and got a group moving towards it' },
  { tag: 'mentoring', shows: 'helped others grow through reviews, pairing or docs' },
  { tag: 'failure', shows: 'owned a mistake, fixed it and changed how things are done' },
  { tag: 'deadline', shows: 'delivered under time pressure by cutting scope wisely' },
  { tag: 'feedback', shows: 'acted on feedback and changed how I work because of it' },
];

/** The server's limits on a note. */
const LIMITS = { title: 200, line: 300, items: 30, body: 50000 };
const MAX_EVIDENCE = 10;
const WORDS_PER_MINUTE = 150;

/** One story as the file lists it, and the note it becomes when nothing is wrong with it. */
export interface StoryRead {
  /** 1-based, as the file lists it. */
  number: number;
  title: string;
  quality: string;
  commits: number;
  repos: string[];
  problems: string[];
  note: Note | null;
}

export type StoriesRead = { error: string } | { stories: StoryRead[] };

/** The prompt the user gives their own agent, which reads their git history on their computer. */
export function storyPrompt(): string {
  const qualities = STORY_QUALITIES.map((q) => `- ${q.tag}: ${q.shows}`).join('\n');
  const example = {
    format: STORY_FORMAT,
    stories: [
      {
        title: 'Billing cron → queue migration',
        quality: 'ownership',
        summary: 'One or two sentences: what I did and what changed because of it.',
        situation: 'The context, and why it mattered.',
        task: 'What I had to achieve, and the constraints.',
        action: 'What I did, step by step, and the choices I made along the way.',
        result: 'What came of it, with numbers the history or I can back up.',
        learned: 'Optional: what I took away or would do differently.',
        questions: ['Tell me about a project you led end to end.', 'What are you proudest of?'],
        evidence: [
          {
            repo: 'billing-service',
            commit: 'a1b2c3d',
            date: '2025-03-14',
            what: 'Split the nightly run into per-account jobs on a queue',
          },
        ],
      },
    ],
  };

  return `Help me prepare interview stories from my real work. Read my git history and write the stories as a JSON file I'll import into OffTheLoop, my job-search notes.

## 1. Find my work
- Look for git repositories in this folder and its subfolders, plus any I name.
- Work out which names and emails I commit under: \`git config user.email\` and \`git shortlog -sne --all\` in each repository. If you can't tell which are mine, ask me before going on.
- Read my commits with \`git log --all --author=<me> --stat\`, then the diffs of the substantial ones. If the GitHub CLI (\`gh\`) is signed in, my pull requests, their descriptions and their review threads help too.

## 2. Pick the stories
Group related commits into pieces of work (a migration, an incident fix, a feature, a refactor, a tool) and pick the 5 to 12 that best show what top employers look for. Each story shows mainly one of these qualities:

${qualities}

Spread the stories across the qualities, two at most for any one. Prefer work that is substantial, recent, and mostly mine.

## 3. Rules
- Every story rests on real commits from the history. Cite them under "evidence" with their short hashes. Never invent a commit, a number or an outcome.
- Git shows what changed but not always why, or what came of it. When the situation or the result isn't in the history, ask me (one short batch of questions is fine) or leave the claim out. Use numbers only when the history or I back them up.
- Write in the first person, past tense, in plain spoken English I could say aloud. The action is what I did, not what the team did.
- Leave out secrets, keys, internal hostnames, customer names and anything confidential. Describe systems in general terms ("the billing service").

## 4. Write the file
Write \`${STORY_FILE}\` in this folder holding only this JSON, with no comments, then tell me where it is:

${JSON.stringify(example, null, 2)}

- "quality" is one of the names in step 2.
- "questions" lists 2 to 4 interview questions the story answers.
- "evidence" lists 1 to ${MAX_EVIDENCE} of my commits, oldest first; "date" is the commit date as YYYY-MM-DD.
- Keep the title under 80 characters, and each question and evidence line under ${LIMITS.line}.
- Each field is plain text. Every field except "learned" is required.
`;
}

/** Reads what the user's agent wrote; a story with problems is listed with them, not as a note. */
export function readStories(text: string): StoriesRead {
  const parsed = parseAgentJson(text);
  if ('error' in parsed) {
    return parsed;
  }
  const file = parsed.value;
  const list = Array.isArray(file) ? file : isObject(file) ? file['stories'] : undefined;
  if (isObject(file) && file['format'] !== undefined && file['format'] !== STORY_FORMAT) {
    return { error: `This is a “${String(file['format'])}” file, not ${STORY_FORMAT}.` };
  }
  if (!Array.isArray(list)) {
    return { error: 'There’s no “stories” list in it.' };
  }
  if (list.length === 0) {
    return { error: 'It has no stories.' };
  }
  if (list.length > MAX_STORIES) {
    return { error: `It has ${list.length} stories; import at most ${MAX_STORIES} at a time.` };
  }
  return { stories: list.map((story, i) => readStory(story, i + 1)) };
}

function readStory(value: unknown, number: number): StoryRead {
  const story = isObject(value) ? value : {};
  const problems: string[] = isObject(value) ? [] : ['It isn’t a story object.'];
  const field = (name: string, required: boolean, max?: number): string => {
    const raw = story[name];
    if (raw === undefined || raw === null || raw === '') {
      if (required) {
        problems.push(`“${name}” is missing.`);
      }
      return '';
    }
    if (typeof raw !== 'string') {
      problems.push(`“${name}” isn’t text.`);
      return '';
    }
    const text = raw.trim();
    if (required && !text) {
      problems.push(`“${name}” is empty.`);
    }
    if (max && text.length > max) {
      problems.push(`“${name}” is longer than ${max} characters.`);
    }
    return text;
  };

  const title = field('title', true, LIMITS.title);
  const qualityText = field('quality', true).toLowerCase();
  const quality = STORY_QUALITIES.some((q) => q.tag === qualityText) ? qualityText : '';
  if (qualityText && !quality) {
    problems.push(`“${qualityText}” isn’t one of the qualities.`);
  }
  const summary = field('summary', true);
  const situation = field('situation', true);
  const task = field('task', true);
  const action = field('action', true);
  const result = field('result', true);
  const learned = field('learned', false);
  const questions = readQuestions(story['questions'], problems);
  const evidence = readEvidence(story['evidence'], problems);

  const read = {
    number,
    title: title || `Story ${number}`,
    quality,
    commits: evidence.length,
    repos: [...new Set(evidence.map((e) => e.repo))],
    problems,
    note: null,
  };
  if (problems.length) {
    return read;
  }

  const body = [
    escape(summary),
    `Situation: ${escape(situation)}`,
    `Task: ${escape(task)}`,
    `Action: ${escape(action)}`,
    `Result: ${escape(result)}`,
    ...(learned ? [`Learned: ${escape(learned)}`] : []),
    '### Evidence',
    evidence
      .map(
        (e) =>
          `- \`${e.commit}\` ${escape(e.repo)}${e.date ? ` · ${e.date}` : ''} — ${escape(e.what)}`,
      )
      .join('\n'),
  ].join('\n\n');
  if (body.length > LIMITS.body) {
    return { ...read, problems: [`It’s longer than ${LIMITS.body} characters.`] };
  }

  return {
    ...read,
    note: {
      id: '',
      kind: 'story',
      companyId: null,
      title,
      tag: quality,
      body,
      badges: [speakingTime([situation, task, action, result].join(' ')), 'from git'],
      fits: questions,
    },
  };
}

function readQuestions(value: unknown, problems: string[]): string[] {
  if (!Array.isArray(value) || value.length === 0) {
    problems.push('“questions” needs at least one question.');
    return [];
  }
  if (value.length > LIMITS.items) {
    problems.push(`“questions” can hold at most ${LIMITS.items}.`);
  }
  const questions = value.map((q) => (typeof q === 'string' ? q.trim() : ''));
  if (questions.some((q) => !q)) {
    problems.push('A question is empty or isn’t text.');
  }
  if (questions.some((q) => q.length > LIMITS.line)) {
    problems.push(`A question is longer than ${LIMITS.line} characters.`);
  }
  return questions;
}

interface Evidence {
  repo: string;
  commit: string;
  date: string;
  what: string;
}

/** The real examples a story rests on; without one it's only a claim. */
function readEvidence(value: unknown, problems: string[]): Evidence[] {
  if (!Array.isArray(value) || value.length === 0) {
    problems.push('“evidence” needs at least one commit.');
    return [];
  }
  if (value.length > MAX_EVIDENCE) {
    problems.push(`“evidence” can hold at most ${MAX_EVIDENCE} commits.`);
  }
  return value.map((item, i) => {
    const entry = isObject(item) ? item : {};
    const text = (name: string) => (typeof entry[name] === 'string' ? entry[name].trim() : '');
    const evidence = {
      repo: text('repo'),
      commit: text('commit'),
      date: text('date'),
      what: text('what'),
    };
    const at = `Evidence ${i + 1}`;
    if (!/^[0-9a-f]{7,40}$/i.test(evidence.commit)) {
      problems.push(`${at} has no commit hash.`);
    }
    if (!evidence.repo) {
      problems.push(`${at} doesn’t name its repository.`);
    }
    if (!evidence.what) {
      problems.push(`${at} doesn’t say what the commit shows.`);
    }
    if (evidence.what.length > LIMITS.line || evidence.repo.length > LIMITS.title) {
      problems.push(`${at} is too long.`);
    }
    if (evidence.date && !/^\d{4}-\d{2}-\d{2}$/.test(evidence.date)) {
      problems.push(`${at}’s date isn’t YYYY-MM-DD.`);
    }
    return evidence;
  });
}

/** How long the STAR part takes to say, as the user's own badges put it: "90 sec", "2 min". */
export function speakingTime(text: string): string {
  const words = text.split(/\s+/).filter(Boolean).length;
  const seconds = Math.max(30, Math.ceil((words / WORDS_PER_MINUTE) * 2) * 30);
  return seconds <= 90 ? `${seconds} sec` : `${Math.round(seconds / 60)} min`;
}

/** `$` would open maths and `~` strike text through in the note page. */
function escape(text: string): string {
  return text.replace(/[\\$~]/g, (c) => `\\${c}`);
}
