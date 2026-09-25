import { SceneElement } from './scene-element';

export interface Scene {
  id: string;
  containerId: string;
  position: number;
  title: string;
  elements: SceneElement[];
}
