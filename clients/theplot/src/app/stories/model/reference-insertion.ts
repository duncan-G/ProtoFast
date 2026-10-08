import { MentionedText } from './mentioned-text';

/** Also a tag's result, with the caret after the tagged text. */
export interface ReferenceInsertion {
  value: MentionedText;
  caret: number;
}
