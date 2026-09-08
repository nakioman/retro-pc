import { type FormEvent, useEffect, useState } from "react";
import { api } from "../../../../api/client";
import { apiUrl } from "../../../../api/requests/http";
import { isApiError } from "../../../../api/types/ApiError";
import type { Game } from "../../../../api/types/Game";
import type { ScraperResult } from "../../../../api/types/ScraperResult";
import { CoverSample } from "../../../../components/GameGroup";
import { Dialog } from "../../../../components/Dialog";
import type { Translator } from "../../../../components/shell/AppShellContext";

type CoverSearchState = "idle" | "loading" | "empty" | "results" | "error";

export function CoverDialog({
  target,
  t,
  onClose,
  onDone,
  onError,
}: {
  target: Game | null;
  t: Translator;
  onClose: () => void;
  onDone: () => void;
  onError: (message: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<ScraperResult[]>([]);
  const [selected, setSelected] = useState<ScraperResult | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [localPreviewUrl, setLocalPreviewUrl] = useState<string | null>(null);
  const [searchState, setSearchState] = useState<CoverSearchState>("idle");
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    setQuery(target?.label ?? "");
    setResults([]);
    setSelected(null);
    setFile(null);
    setSearchState("idle");
    setIsSaving(false);
  }, [target]);

  useEffect(() => {
    if (!file) {
      setLocalPreviewUrl(null);
      return;
    }
    const url = URL.createObjectURL(file);
    setLocalPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [file]);

  const search = async (event: FormEvent) => {
    event.preventDefault();
    setSearchState("loading");
    setResults([]);
    setSelected(null);
    try {
      const nextResults = await api.searchCovers(query);
      setResults(nextResults);
      setSearchState(nextResults.length === 0 ? "empty" : "results");
    } catch {
      setSearchState("error");
    }
  };

  const save = async () => {
    if (!target || isSaving) return;
    setIsSaving(true);
    try {
      if (file) await api.uploadCover(target.id, file);
      else if (selected) await api.selectCover(target.id, selected.screenScraperId);
      else return;
      onDone();
    } catch (value) {
      onError(isApiError(value) ? value.message : "No se pudo guardar la carátula.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Dialog
      open={Boolean(target)}
      title={t("editCover")}
      onClose={onClose}
      actions={
        <>
          <button onClick={() => void save()} disabled={isSaving || (!selected && !file)}>
            {isSaving ? t("savingCover") : t("accept")}
          </button>
          <button disabled={isSaving} onClick={onClose}>
            {t("cancel")}
          </button>
        </>
      }
    >
      <div className="cover-layout">
        <div className="cover-preview">
          {localPreviewUrl ? (
            <img src={localPreviewUrl} alt="" />
          ) : selected?.thumbnailUrl ? (
            <img src={selected.thumbnailUrl} alt="" />
          ) : target?.cover ? (
            <img src={apiUrl(`/api/covers/${encodeURIComponent(target.cover)}`)} alt="" />
          ) : (
            <CoverSample title={target?.label ?? ""} />
          )}
        </div>
        <div className="cover-tools">
          <fieldset>
            <legend>{t("localFile")}</legend>
            <input
              type="file"
              accept="image/png,image/jpeg,image/webp"
              disabled={isSaving}
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
          </fieldset>
          <fieldset>
            <legend>{t("searchCover")}</legend>
            <form onSubmit={search}>
              <label>
                {t("name")}
                <input
                  value={query}
                  disabled={isSaving}
                  onChange={(event) => setQuery(event.target.value)}
                />
              </label>
              <button disabled={isSaving || searchState === "loading"}>
                {searchState === "loading" ? t("searchingCovers") : t("searchCover")}
              </button>
            </form>
          </fieldset>
        </div>
      </div>
      <fieldset>
        <legend>{t("coverSearchResults")}</legend>
        {searchState !== "idle" && (
          <p
            className={`cover-search-status${searchState === "error" ? " error" : ""}`}
            aria-live="polite"
          >
            {searchState === "loading" && t("searchingCovers")}
            {searchState === "empty" && t("noCoverResults")}
            {searchState === "results" && t("coverResultsFound", { count: results.length })}
            {searchState === "error" && t("coverSearchFailed")}
          </p>
        )}
        <div className="cover-results" aria-busy={searchState === "loading"}>
          {results.map((result) => (
            <button
              className="cover-option"
              key={result.screenScraperId}
              disabled={isSaving}
              aria-pressed={selected?.screenScraperId === result.screenScraperId}
              onClick={() => {
                setSelected(result);
                setFile(null);
              }}
            >
              {result.thumbnailUrl ? (
                <img src={result.thumbnailUrl} alt="" />
              ) : (
                <CoverSample title={result.title} />
              )}
            </button>
          ))}
        </div>
      </fieldset>
    </Dialog>
  );
}
