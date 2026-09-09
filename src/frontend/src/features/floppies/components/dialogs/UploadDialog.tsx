import { type FormEvent, useEffect, useState } from "react";
import { api } from "../../../../api/client";
import { isApiError } from "../../../../api/types/ApiError";
import type { Game } from "../../../../api/types/Game";
import { Dialog } from "../../../../components/Dialog";
import type { MessageKey } from "../../../../i18n";
import { createGameId } from "../../utils/gameId";

export function UploadDialog({
  open,
  game,
  t,
  existingIds,
  onClose,
  onDone,
  onError,
}: {
  open: boolean;
  game: Game | null | undefined;
  t: (key: MessageKey) => string;
  existingIds: ReadonlySet<string>;
  onClose: () => void;
  onDone: (message: string) => void;
  onError: (message: string) => void;
}) {
  const [label, setLabel] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [fileInputKey, setFileInputKey] = useState(0);

  useEffect(() => {
    if (!open) return;
    setLabel(game?.label ?? "");
    setFiles([]);
    setBusy(false);
    setFileInputKey((value) => value + 1);
  }, [game, open]);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (!files.length) return;

    setBusy(true);
    let target = game;
    const successful: string[] = [];

    try {
      if (!target) {
        const id = createGameId(label.trim(), existingIds);
        if (!id) {
          throw { code: "invalid-request", message: "El nombre no puede generar un ID válido." };
        }
        target = await api.createGame(id, label.trim());
      }

      for (const file of files) {
        try {
          successful.push(await api.uploadFloppy(file));
          await api.updateGame(target.id, target.label, [
            ...target.floppies.map((floppy) => floppy.id),
            ...successful,
          ]);
        } catch (value) {
          const message = isApiError(value) ? value.message : "No se pudo subir el archivo.";
          if (!successful.length && !game) await api.deleteGame(target.id);
          onError(`${file.name}: ${message}`);
          onDone(`${successful.length} disquete(s) importado(s).`);
          return;
        }
      }
      onDone(`${successful.length} disquete(s) importado(s).`);
    } catch (value) {
      onError(isApiError(value) ? value.message : "No se pudo crear el grupo.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      title={game ? `${t("addDisk")} — ${game.label}` : t("uploadTitle")}
      onClose={onClose}
      actions={
        <>
          <button type="submit" form="upload-form" disabled={busy}>
            {t("add")}
          </button>
          <button onClick={onClose}>{t("cancel")}</button>
        </>
      }
    >
      <form id="upload-form" onSubmit={submit}>
        <label hidden={Boolean(game)}>
          {t("groupName")}
          <input
            required={!game}
            value={label}
            maxLength={100}
            onChange={(event) => setLabel(event.target.value)}
          />
        </label>
        <fieldset>
          <legend>{t("files")}</legend>
          <input
            key={fileInputKey}
            type="file"
            accept=".img,.ima,.dsk,.zip"
            multiple
            required
            onChange={(event) => setFiles(Array.from(event.target.files ?? []))}
          />
          {files.length > 0 ? (
            <ul className="file-list">
              {files.map((file) => (
                <li key={`${file.name}-${file.lastModified}`}>
                  {file.name} — {Math.round(file.size / 1024)} KB
                </li>
              ))}
            </ul>
          ) : null}
        </fieldset>
      </form>
    </Dialog>
  );
}
