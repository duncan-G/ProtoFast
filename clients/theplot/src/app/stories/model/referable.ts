import { ReferenceKind } from './reference-kind';

export interface Referable {
  kind: ReferenceKind;
  id: string;
  name: string;
}
