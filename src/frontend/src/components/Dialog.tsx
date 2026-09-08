import { type ReactNode, useEffect, useRef } from "react";
import { TitleBar } from "./shell/TitleBar";

export function Dialog({
  open,
  title,
  children,
  actions,
  onClose,
}: {
  open: boolean;
  title: string;
  children: ReactNode;
  actions: ReactNode;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    if (!open && dialog.open) dialog.close();
  }, [open]);
  return (
    <dialog
      ref={ref}
      onCancel={(event) => {
        event.preventDefault();
        onClose();
      }}
      onClose={onClose}
    >
      <TitleBar title={title} level="h2">
        <button className="close-button" type="button" onClick={onClose} aria-label="Cerrar">
          ×
        </button>
      </TitleBar>
      <div className="dialog-body">
        <div className="dialog-content">{children}</div>
        <div className="dialog-actions">{actions}</div>
      </div>
    </dialog>
  );
}
