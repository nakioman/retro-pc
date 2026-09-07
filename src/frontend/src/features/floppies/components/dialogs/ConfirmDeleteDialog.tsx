import type { Floppy } from "../../../../api/types/Floppy";
import type { Game } from "../../../../api/types/Game";
import { Dialog } from "../../../../components/Dialog";
import type { MessageKey } from "../../../../i18n";

export type DeleteTarget = { type: "game"; game: Game } | { type: "floppy"; floppy: Floppy } | null;

export function ConfirmDeleteDialog({
  target,
  t,
  onClose,
  onConfirm,
}: {
  target: DeleteTarget;
  t: (key: MessageKey, values?: Record<string, string>) => string;
  onClose: () => void;
  onConfirm: () => void;
}) {
  const name = target?.type === "game" ? target.game.label : (target?.floppy.label ?? "");
  const message =
    target?.type === "game"
      ? t("deleteGroupMessage", { name })
      : t("deleteFloppyMessage", { name });

  return (
    <Dialog
      open={Boolean(target)}
      title={t("confirm")}
      onClose={onClose}
      actions={
        <>
          <button onClick={onConfirm}>{t("delete")}</button>
          <button autoFocus onClick={onClose}>
            {t("cancel")}
          </button>
        </>
      }
    >
      <p>{message}</p>
    </Dialog>
  );
}
