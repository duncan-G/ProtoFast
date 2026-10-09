export interface ScenePropState {
  id: string;
  propId: string;
  /** In view in every frame unless a frame says otherwise. */
  inView: boolean;
  /** "broken", "lit"; empty means as the sheet describes it. */
  state: string[];
  changeReason: string | null;
  changeElementId: string | null;
  edited: boolean;
}

export function blankPropState(propId: string): ScenePropState {
  return {
    id: '',
    propId,
    inView: true,
    state: [],
    changeReason: null,
    changeElementId: null,
    edited: true,
  };
}
