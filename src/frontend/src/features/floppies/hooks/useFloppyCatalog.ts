import { useCallback, useEffect, useState } from "react";
import { api } from "../../../api/client";
import { isApiError } from "../../../api/types/ApiError";
import type { Catalog } from "../../../api/types/Catalog";

export function useFloppyCatalog() {
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [error, setError] = useState("");

  const reload = useCallback(async () => {
    try {
      setCatalog(await api.catalog());
      setError("");
    } catch {
      setError("No se pudo cargar el catálogo.");
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const run = useCallback(
    async (work: () => Promise<void>, onSuccess: () => void) => {
      try {
        await work();
        await reload();
        onSuccess();
      } catch (value) {
        setError(isApiError(value) ? value.message : "No se pudo completar la operación.");
      }
    },
    [reload],
  );

  return { catalog, error, reload, run, setError };
}
