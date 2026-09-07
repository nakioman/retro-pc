import { useEffect, useState } from "react";
import type { Game } from "../../../../api/types/Game";
import { Dialog } from "../../../../components/Dialog";
import type { MessageKey } from "../../../../i18n";

export function EditGameDialog({
  target,
  t,
  onClose,
  onSave,
}: {
  target: Game | null;
  t: (key: MessageKey) => string;
  onClose: () => void;
  onSave: (label: string) => void;
}) {
  const [label, setLabel] = useState("");
  useEffect(() => setLabel(target?.label ?? ""), [target]);

  return (
    <Dialog
      open={Boolean(target)}
      title={t("editGroup")}
      onClose={onClose}
      actions={
        <>
          <button type="submit" form="game-form">
            {t("save")}
          </button>
          <button onClick={onClose}>{t("cancel")}</button>
        </>
      }
    >
      <form
        id="game-form"
        onSubmit={(event) => {
          event.preventDefault();
          if (!label.trim()) return;
          onSave(label.trim());
          onClose();
        }}
      >
        <label>
          {t("groupName")}
          <input
            autoFocus
            value={label}
            maxLength={100}
            onChange={(event) => setLabel(event.target.value)}
          />
        </label>
      </form>
    </Dialog>
  );
}
