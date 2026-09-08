import type { FloppyMode } from "./Floppy";

export interface Drive {
  state: "unavailable" | "empty" | "loaded" | "blankTag";
  floppyId: string | null;
  mode: FloppyMode | null;
  tagUid: string | null;
}
