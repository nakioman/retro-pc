import { json, request } from "./http";
import type { ScraperResult } from "../types/ScraperResult";
import type { ScraperSettings } from "../types/ScraperSettings";

export const getSettings = () => request<ScraperSettings>("/api/settings/scraper");
export const updateSettings = (body: Partial<ScraperSettings> & Record<string, unknown>) =>
  request<ScraperSettings>("/api/settings/scraper", json(body, "PUT"));
export const testSettings = () => request<void>("/api/settings/scraper/test", { method: "POST" });
export const searchCovers = (query: string) =>
  request<ScraperResult[]>(`/api/scraper/search?q=${encodeURIComponent(query)}`);
export const selectCover = (gameId: string, screenScraperId: string) =>
  request<{ cover: string }>(
    `/api/games/${encodeURIComponent(gameId)}/cover`,
    json({ screenScraperId }, "POST"),
  );
export async function uploadCover(gameId: string, file: File): Promise<{ cover: string }> {
  const form = new FormData();
  form.append("file", file);
  return request<{ cover: string }>(`/api/games/${encodeURIComponent(gameId)}/cover/upload`, {
    method: "POST",
    body: form,
  });
}
