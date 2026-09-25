import { SceneSummary } from './scene-summary';

export interface Container {
  id: string;
  storyId: string;
  position: number;
  label: string;
  scenes: SceneSummary[];
}
