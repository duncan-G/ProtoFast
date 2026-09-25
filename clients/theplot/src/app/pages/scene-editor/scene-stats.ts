/** Keyed by library id. */
export interface SceneStats {
  lines: Map<string, number>;
  mentions: Map<string, number>;
  headings: Map<string, number>;
}
