import { json, request } from "./http";
import type { Game } from "../types/Game";

export const createGame = (id: string, label: string) =>
  request<Game>("/api/games", json({ id, label }, "POST"));
export const updateGame = (id: string, label: string, floppyIds: string[]) =>
  request<void>(`/api/games/${encodeURIComponent(id)}`, json({ label, floppyIds }));
export const deleteGame = (id: string) =>
  request<void>(`/api/games/${encodeURIComponent(id)}`, { method: "DELETE" });
