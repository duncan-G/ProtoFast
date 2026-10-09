/** An extra in the scene; frames picture them unless a frame says otherwise. */
export interface SceneExtra {
  id: string;
  extraId: string;
  onScreen: boolean;
  edited: boolean;
}

export function blankSceneExtra(extraId: string): SceneExtra {
  return { id: '', extraId, onScreen: true, edited: true };
}
