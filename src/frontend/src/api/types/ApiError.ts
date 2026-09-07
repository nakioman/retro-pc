export interface ApiError {
  code: string;
  message: string;
  previousFloppyId?: string | null;
  tagUid?: string | null;
}

export function isApiError(value: unknown): value is ApiError {
  return typeof value === "object" && value !== null && "code" in value && "message" in value;
}
