export interface SceneSummary {
  id: string;
  containerId: string;
  position: number;
  title: string;
  openingLocationId: string | null;
  openingTimeOfDay: string | null;
}
