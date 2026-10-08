import { toolCallView } from './long-text';

describe('toolCallView', () => {
  const source = 'public static class Script\n{\n    static int N = 1;\n}';

  it('offers create-code source as C# and cuts it to a preview in the JSON', () => {
    const view = toolCallView(
      JSON.stringify({
        skill: 'create-code',
        script: 'create',
        args: { skill: 'screenplay-import', script: 'cut-units', source },
      }),
    );

    expect(view.skill).toBe('create-code');
    expect(view.texts).toEqual([
      { field: 'source', text: source, language: 'csharp', subject: 'screenplay-import/cut-units' },
    ]);
    expect(JSON.parse(view.json).args.source).toBe(
      `public static class Script… (${source.length} characters)`,
    );
    expect(JSON.parse(view.json).args.script).toBe('cut-units');
  });

  it('offers create-skill instructions as markdown under the skill being written', () => {
    const view = toolCallView(
      JSON.stringify({
        skill: 'create-skill',
        script: 'create',
        args: {
          name: 'screenplay-import',
          description: 'Import a manuscript.',
          instructions: '# Import\n\nSlice it.',
        },
      }),
    );

    expect(view.texts.map((t) => [t.field, t.language, t.subject])).toEqual([
      ['instructions', 'markdown', 'screenplay-import'],
    ]);
  });

  it('offers any other multi-line argument as plain text, and leaves long single lines inline', () => {
    const view = toolCallView(
      JSON.stringify({
        skill: 'record-decision',
        script: 'record-decision',
        args: { key: 'scene-boundaries', choice: 'c'.repeat(200), rationale: 'one\ntwo' },
      }),
    );

    expect(view.texts.map((t) => [t.field, t.language, t.subject])).toEqual([
      ['rationale', 'text', 'scene-boundaries'],
    ]);
    expect(JSON.parse(view.json).args.choice).toBe('c'.repeat(200));
  });

  it('leaves short arguments and unparseable input alone', () => {
    const input = JSON.stringify({ skill: 'context', script: 'context' });
    expect(toolCallView(input)).toEqual({
      skill: 'context',
      json: JSON.stringify(JSON.parse(input), null, 2),
      texts: [],
    });
    expect(toolCallView('not json')).toEqual({ skill: '', json: 'not json', texts: [] });
  });
});
