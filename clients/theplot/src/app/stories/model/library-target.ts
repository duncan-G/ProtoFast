import { ReferenceKind } from './reference-kind';

/** A library entry: anything `@`-mentionable, or extras, which are never mentioned. */
export interface LibraryTarget {
  kind: ReferenceKind | 'extra';
  id: string;
}
