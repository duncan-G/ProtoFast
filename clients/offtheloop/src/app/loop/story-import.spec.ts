import { readStories, speakingTime, STORY_FORMAT, storyPrompt, StoryRead } from './story-import';

function story(fields: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    title: 'Billing cron → queue migration',
    quality: 'Ownership',
    summary: 'I moved billing off a five-hour cron and onto a queue.',
    situation: 'Invoices ran as one nightly job that cost $40k a year in pages.',
    task: 'Get billing under an hour.',
    action: 'Split the run into per-account jobs.',
    result: 'Five hours became ~22 minutes.',
    questions: ['Led something end to end'],
    evidence: [
      { repo: 'billing', commit: 'a1b2c3d', date: '2025-03-14', what: 'Per-account jobs' },
    ],
    ...fields,
  };
}

function read(text: string): StoryRead[] {
  const result = readStories(text);
  if ('error' in result) {
    throw new Error(result.error);
  }
  return result.stories;
}

describe('story import', () => {
  it('turns a story into a story note tagged with its quality', () => {
    const [first] = read(JSON.stringify({ format: STORY_FORMAT, stories: [story()] }));

    expect(first.problems).toEqual([]);
    expect(first.note).toEqual(
      expect.objectContaining({
        kind: 'story',
        title: 'Billing cron → queue migration',
        tag: 'ownership',
        badges: ['30 sec', 'from git'],
        fits: ['Led something end to end'],
      }),
    );
    expect(first.note?.body).toBe(
      [
        'I moved billing off a five-hour cron and onto a queue.',
        'Situation: Invoices ran as one nightly job that cost \\$40k a year in pages.',
        'Task: Get billing under an hour.',
        'Action: Split the run into per-account jobs.',
        'Result: Five hours became \\~22 minutes.',
        '### Evidence',
        '- `a1b2c3d` billing · 2025-03-14 — Per-account jobs',
      ].join('\n\n'),
    );
  });

  it('reads JSON wrapped in a fence and a sentence', () => {
    const text = `Here it is:\n\n\`\`\`json\n${JSON.stringify([story()])}\n\`\`\`\nGood luck!`;

    expect(read(text).map((s) => s.title)).toEqual(['Billing cron → queue migration']);
  });

  it('lists what is wrong with a story instead of making a note', () => {
    const [bad, good] = read(
      JSON.stringify({
        stories: [
          story({ quality: 'grit', task: '', evidence: [{ repo: 'billing', what: 'x' }] }),
          story(),
        ],
      }),
    );

    expect(bad.note).toBeNull();
    expect(bad.problems).toEqual([
      '“grit” isn’t one of the qualities.',
      '“task” is missing.',
      'Evidence 1 has no commit hash.',
    ]);
    expect(good.note).not.toBeNull();
  });

  it('wants evidence from the history', () => {
    const [none] = read(JSON.stringify([story({ evidence: [] })]));

    expect(none.problems).toEqual(['“evidence” needs at least one commit.']);
  });

  it('turns away a file that is not a stories file', () => {
    expect(readStories('')).toEqual({ error: 'It’s empty.' });
    expect(readStories('{"format":"other/1","stories":[]}')).toEqual({
      error: `This is a “other/1” file, not ${STORY_FORMAT}.`,
    });
    expect(readStories('{"items":[]}')).toEqual({ error: 'There’s no “stories” list in it.' });
    expect(readStories('{"stories":[]}')).toEqual({ error: 'It has no stories.' });
    expect('error' in readStories('{"stories": [')).toBe(true);
  });

  it('says how long the story takes to tell', () => {
    expect(speakingTime('word '.repeat(200))).toBe('90 sec');
    expect(speakingTime('word '.repeat(300))).toBe('2 min');
  });

  it('asks the agent for this format', () => {
    const prompt = storyPrompt();

    expect(prompt).toContain(STORY_FORMAT);
    expect(prompt).toContain('- ownership:');
    expect(read(/\{[\s\S]*\n\}/.exec(prompt)?.[0] ?? '')[0].problems).toEqual([]);
  });
});
