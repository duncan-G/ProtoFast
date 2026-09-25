import { ReferenceKind } from './reference-kind';

export interface ReferenceTarget {
  kind: ReferenceKind;
  id: string;
}
