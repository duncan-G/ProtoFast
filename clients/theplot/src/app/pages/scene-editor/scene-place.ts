import { Container } from '../../stories/model/container';
import { SceneSummary } from '../../stories/model/scene-summary';

export interface ScenePlace {
  container: Container;
  summary: SceneSummary;
  /** Zero-based, within its container. */
  index: number;
}
