import { useEffect, useState } from "react";
import { api } from "../../../../api/client";
import { isApiError } from "../../../../api/types/ApiError";
import type { Floppy } from "../../../../api/types/Floppy";
import { Dialog } from "../../../../components/Dialog";
import type { MessageKey } from "../../../../i18n";

export function NfcDialog({
  target,
  t,
  onClose,
  onDone,
  onError,
}: {
  target: Floppy | null;
  t: (key: MessageKey) => string;
  onClose: () => void;
  onDone: (message: string) => void;
  onError: (message: string) => void;
}) {
  const [ready, setReady] = useState(false);
  const [uid, setUid] = useState<string>();

  useEffect(() => {
    setReady(false);
    setUid(undefined);
  }, [target]);

  const detect = async () => {
    try {
      const drive = await api.drive();
      if (drive.state === "blankTag" || drive.state === "loaded") {
        setReady(true);
        setUid(drive.tagUid ?? undefined);
        return;
      }
      onError("No hay un disquete disponible en la unidad.");
    } catch {
      onError("No se pudo consultar la unidad.");
    }
  };

  const write = async (confirm = false) => {
    if (!target) return;
    try {
      await api.writeNfc(target.id, confirm, uid);
      onDone(t("save"));
    } catch (value) {
      if (
        isApiError(value) &&
        value.code === "tag-already-assigned" &&
        window.confirm(value.message)
      ) {
        setUid(value.tagUid ?? undefined);
        await write(true);
        return;
      }
      onError(isApiError(value) ? value.message : "No se pudo escribir la etiqueta.");
    }
  };

  return (
    <Dialog
      open={Boolean(target)}
      title={t("nfcTitle")}
      onClose={onClose}
      actions={
        <>
          <button disabled={!ready} onClick={() => void write()}>
            {t("writeNfc")}
          </button>
          <button onClick={onClose}>{t("cancel")}</button>
        </>
      }
    >
      <p>{target?.label}</p>
      <p>{t("nfcInstructions")}</p>
      <div className="inset">
        <p>{ready ? t("ready") : t("detecting")}</p>
        <button onClick={() => void detect()}>{t("detect")}</button>
      </div>
    </Dialog>
  );
}
