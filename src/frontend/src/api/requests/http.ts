import { isApiError, type ApiError } from "../types/ApiError";

const configuredApiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? "").replace(/\/+$/, "");

export function apiUrl(path: string): string {
  if (!path.startsWith("/api/")) throw new Error(`Unexpected API path: ${path}`);
  return `${configuredApiBaseUrl}${path}`;
}

export const json = (body: unknown, method = "PATCH"): RequestInit => ({
  method,
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(body),
});

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(apiUrl(path), init);
  if (response.ok)
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  try {
    throw (await response.json()) as ApiError;
  } catch (error) {
    if (isApiError(error)) throw error;
    throw { code: "network-error", message: "Could not reach RetroBox." } satisfies ApiError;
  }
}
