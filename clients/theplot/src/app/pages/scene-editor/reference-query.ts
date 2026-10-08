export interface ReferenceQuery {
  /** Index of the `@`. */
  start: number;
  query: string;
  /** Where the text being tagged starts (`him@`, or a selection then `@`); null for an `@Name`. */
  tagStart: number | null;
}
