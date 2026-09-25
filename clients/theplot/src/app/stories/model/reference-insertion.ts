import { MentionedText } from './mentioned-text';

export interface ReferenceInsertion {
  value: MentionedText;
  caret: number;
}
