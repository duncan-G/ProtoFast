import { CURSOR, placeMarkers, SELECTION_END, SELECTION_START } from './write-markers';

describe('placeMarkers', () => {
  it('puts the cursor back between the same text in an edited note', () => {
    const document = `## Stack\n\nKeep indices.\n\n${CURSOR}\n\n## Cost`;

    const placed = placeMarkers('# Added\n\n## Stack\n\nKeep indices.\n\n## Cost', document);

    expect(placed).toBe(`# Added\n\n## Stack\n\nKeep indices.\n\n${CURSOR}## Cost`);
  });

  it('falls back on one side when the other was rewritten', () => {
    const document = `Keep indices on a stack.${CURSOR} Pop while smaller.`;

    expect(placeMarkers('Keep indices on a stack. Pop when the top is smaller.', document)).toBe(
      `Keep indices on a stack.${CURSOR} Pop when the top is smaller.`,
    );
  });

  it('marks the same selection again when it is still there once', () => {
    const document = `Intro.\n\n${SELECTION_START}O(n) time${SELECTION_END}\n\nOutro.`;

    expect(placeMarkers('New intro.\n\nO(n) time\n\nOutro.', document)).toBe(
      `New intro.\n\n${SELECTION_START}O(n) time${SELECTION_END}\n\nOutro.`,
    );
  });

  it('writes at the end when the text around the mark has gone', () => {
    const document = `Old words ${CURSOR} old words`;

    expect(placeMarkers('Something else entirely.\n', document)).toBe(
      `Something else entirely.\n\n${CURSOR}`,
    );
  });

  it('writes at the end when a selection appears more than once', () => {
    const document = `${SELECTION_START}x${SELECTION_END}`;

    expect(placeMarkers('x and x', document)).toBe(`x and x\n\n${CURSOR}`);
  });
});
