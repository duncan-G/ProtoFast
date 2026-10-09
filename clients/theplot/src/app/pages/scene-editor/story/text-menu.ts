export interface TextMenu {
  kind: 'slash' | 'mention';
  /** Index of the `/` or `@`; -1 for a block menu opened by the mouse, with nothing typed. */
  start: number;
  query: string;
  /** Where the text being tagged starts (`him@`, or a selection then `@`). */
  tagStart: number | null;
}
