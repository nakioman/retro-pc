import type { Floppy } from "./Floppy";

export interface Game {
  id: string;
  label: string;
  cover: string | null;
  screenScraperId: number | null;
  floppies: Floppy[];
}
