export type FloppyMode = "ro" | "rw";

export interface Floppy {
  id: string;
  label: string;
  mode: FloppyMode;
  size: string;
  nfc: boolean;
}
