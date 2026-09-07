import { useEffect, useState } from "react";
import { api } from "../../../../api/client";
import { isApiError } from "../../../../api/types/ApiError";
import type { Floppy } from "../../../../api/types/Floppy";
import { Dialog } from "../../../../components/Dialog";
import { MessageBox } from "../../../../components/MessageBox";
import type { Translator } from "../../../../components/shell/AppShellContext";

export function NfcDialog({
  target,
  t,
  onClose,
  onDone,
}: {
  target: Floppy | null;
  t: Translator;
  onClose: () => void;
  onDone: () => void;
}) {
  const [ready, setReady] = useState(false);
  const [uid, setUid] = useState<string>();
  const [status, setStatus] = useState<string>(t("detecting"));
  const [reassignment, setReassignment] = useState<{ uid: string; owner: string } | null>(null);

  useEffect(() => {
    if (!target) return;

    let active = true;
    setReady(false);
    setUid(undefined);
    setStatus(t("detecting"));
    setReassignment(null);
    const detect = async () => {
      try {
        const drive = await api.drive();
        if (!active) return;
        if (drive.state === "blankTag" || drive.state === "loaded") {
          setReady(true);
          setUid(drive.tagUid ?? undefined);
          setStatus(t("ready"));
          return;
        }
        setReady(false);
        setUid(undefined);
        setStatus(t("detecting"));
      } catch {
        if (!active) return;
        setReady(false);
        setUid(undefined);
        setStatus(t("nfcDriveUnavailable"));
      }
    };

    void detect();
    const interval = window.setInterval(() => void detect(), 1_000);
    return () => {
      active = false;
      window.clearInterval(interval);
    };
  }, [t, target]);

  const write = async (confirm = false, tagUid = uid) => {
    if (!target) return;
    try {
      await api.writeNfc(target.id, confirm, tagUid);
      onDone();
    } catch (value) {
      if (isApiError(value) && value.code === "tag-already-assigned" && value.tagUid) {
        setReassignment({
          uid: value.tagUid,
          owner: value.previousFloppyId ?? "",
        });
        return;
      }
      setReady(false);
      setStatus(isApiError(value) ? value.message : t("nfcWriteFailed"));
    }
  };

  const confirmReassignment = async () => {
    if (!reassignment) return;
    const { uid: tagUid } = reassignment;
    setReassignment(null);
    await write(true, tagUid);
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
        <p>{status}</p>
      </div>
      <MessageBox
        presentation="embedded"
        message={
          reassignment
            ? {
                title: t("confirmNfc"),
                text: t("nfcAlreadyAssigned", { name: reassignment.owner }),
                kind: "warning",
              }
            : null
        }
        onDismiss={() => setReassignment(null)}
        actions={
          reassignment ? (
            <>
              <button onClick={() => void confirmReassignment()}>{t("reassignNfc")}</button>
              <button autoFocus onClick={() => setReassignment(null)}>
                {t("cancel")}
              </button>
            </>
          ) : undefined
        }
      />
    </Dialog>
  );
}
