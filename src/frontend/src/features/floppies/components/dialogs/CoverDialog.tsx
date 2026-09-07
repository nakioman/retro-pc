import { type FormEvent, useEffect, useState } from "react";
import { api } from "../../../../api/client";
import { apiUrl } from "../../../../api/requests/http";
import { isApiError } from "../../../../api/types/ApiError";
import type { Game } from "../../../../api/types/Game";
import type { ScraperResult } from "../../../../api/types/ScraperResult";
import { CoverSample } from "../../../../components/GameGroup";
import { Dialog } from "../../../../components/Dialog";
import type { MessageKey } from "../../../../i18n";

export function CoverDialog({
  target,
  t,
  onClose,
  onDone,
  onError,
}: {
  target: Game | null;
  t: (key: MessageKey) => string;
  onClose: () => void;
  onDone: () => void;
  onError: (message: string) => void;
}) {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<ScraperResult[]>([]);
  const [selected, setSelected] = useState<ScraperResult | null>(null);
  const [file, setFile] = useState<File | null>(null);
  const [localPreviewUrl, setLocalPreviewUrl] = useState<string | null>(null);

  useEffect(() => {
    setQuery(target?.label ?? "");
    setResults([]);
    setSelected(null);
    setFile(null);
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
    try {
      setResults(await api.searchCovers(query));
    } catch (value) {
      onError(isApiError(value) ? value.message : "No se pudieron buscar carátulas.");
    }
  };

  const save = async () => {
    if (!target) return;
    try {
      if (file) await api.uploadCover(target.id, file);
      else if (selected) await api.selectCover(target.id, selected.screenScraperId);
      else return;
      onDone();
    } catch (value) {
      onError(isApiError(value) ? value.message : "No se pudo guardar la carátula.");
    }
  };

  return (
    <Dialog
      open={Boolean(target)}
      title={t("editCover")}
      onClose={onClose}
      actions={
        <>
          <button onClick={() => void save()} disabled={!selected && !file}>
            {t("accept")}
          </button>
          <button onClick={onClose}>{t("cancel")}</button>
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
              onChange={(event) => setFile(event.target.files?.[0] ?? null)}
            />
          </fieldset>
          <fieldset>
            <legend>{t("searchCover")}</legend>
            <form onSubmit={search}>
              <label>
                {t("name")}
                <input value={query} onChange={(event) => setQuery(event.target.value)} />
              </label>
              <button>{t("searchCover")}</button>
            </form>
          </fieldset>
        </div>
      </div>
      <fieldset>
        <legend>Resultados</legend>
        <div className="cover-results">
          {results.map((result) => (
            <button
              className="cover-option"
              key={result.screenScraperId}
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
