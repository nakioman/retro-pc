import { useEffect, useState } from "react";
import type { Floppy } from "../../../../api/types/Floppy";
import { Dialog } from "../../../../components/Dialog";
import type { MessageKey } from "../../../../i18n";

export function RenameFloppyDialog({
  target,
  t,
  onClose,
  onSave,
}: {
  target: Floppy | null;
  t: (key: MessageKey) => string;
  onClose: () => void;
  onSave: (label: string) => void;
}) {
  const [label, setLabel] = useState("");
  useEffect(() => setLabel(target?.label ?? ""), [target]);

  return (
    <Dialog
      open={Boolean(target)}
      title={t("renameFloppy")}
      onClose={onClose}
      actions={
        <>
          <button type="submit" form="rename-form">
            {t("save")}
          </button>
          <button onClick={onClose}>{t("cancel")}</button>
        </>
      }
    >
      <form
        id="rename-form"
        onSubmit={(event) => {
          event.preventDefault();
          if (!label.trim()) return;
          onSave(label.trim());
          onClose();
        }}
      >
        <label>
          {t("name")}
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
