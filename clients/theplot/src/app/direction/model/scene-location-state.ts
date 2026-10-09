/** What is different about a location in the scene, on top of its sheet: "fog rolling in". */
export interface SceneLocationState {
  id: string;
  locationId: string;
  change: string;
  edited: boolean;
}
