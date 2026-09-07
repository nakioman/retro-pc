import { json, request } from "./http";
import type { Drive } from "../types/Drive";

export const getDrive = () => request<Drive>("/api/drive");
export const writeNfc = (floppyId: string, confirm = false, tagUid?: string) =>
  request<{
    code: string;
    previousFloppyId: string | null;
    message: string | null;
    tagUid: string | null;
  }>("/api/nfc/write", json({ floppyId, confirm, tagUid: tagUid ?? null }, "POST"));
