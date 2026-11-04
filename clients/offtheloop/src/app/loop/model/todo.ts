export interface Todo {
  id: string;
  label: string;
  /** YYYY-MM-DD. */
  dueOn: string | null;
  done: boolean;
}
