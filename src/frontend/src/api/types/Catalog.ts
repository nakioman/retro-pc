import type { Floppy } from "./Floppy";
import type { Game } from "./Game";

export interface Catalog {
  floppies: Floppy[];
  games: Game[];
  ungroupedFloppies: Floppy[];
  catalogError: string | null;
}
