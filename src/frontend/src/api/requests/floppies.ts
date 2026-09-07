import { apiUrl, json, request } from "./http";
import type { ApiError } from "../types/ApiError";
import type { Floppy } from "../types/Floppy";

export async function uploadFloppy(file: File): Promise<string> {
  const form = new FormData();
  form.append("file", file);
  const response = await fetch(apiUrl("/api/floppies"), { method: "POST", body: form });
  if (!response.ok) {
    let error: ApiError = { code: "unexpected", message: "Unexpected error." };
    try {
      error = (await response.json()) as ApiError;
    } catch {
      /* use the generic API error */
    }
    throw error;
  }
  const location = response.headers.get("Location");
  if (!location)
    throw { code: "unexpected", message: "Missing floppy location." } satisfies ApiError;
  return decodeURIComponent(location.split("/").at(-1) ?? "");
}
export const updateFloppy = (id: string, patch: Partial<Pick<Floppy, "label" | "mode">>) =>
  request<void>(`/api/floppies/${encodeURIComponent(id)}`, json(patch));
export const deleteFloppy = (id: string) =>
  request<void>(`/api/floppies/${encodeURIComponent(id)}`, { method: "DELETE" });
