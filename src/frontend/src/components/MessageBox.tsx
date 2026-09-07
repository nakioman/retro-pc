import { useEffect, useRef } from "react";
import { TitleBar } from "./shell/TitleBar";

export type MessageBoxKind = "error" | "info" | "warning";

export type MessageBoxMessage = {
  title: string;
  text: string;
  kind?: MessageBoxKind;
};

export function MessageBox({
  message,
  onDismiss,
  presentation = "modal",
}: {
  message: MessageBoxMessage | null;
  onDismiss: () => void;
  presentation?: "embedded" | "modal";
}) {
  if (presentation === "embedded") {
    return <EmbeddedMessageBox message={message} onDismiss={onDismiss} />;
  }

  return <ModalMessageBox message={message} onDismiss={onDismiss} />;
}

function ModalMessageBox({
  message,
  onDismiss,
}: {
  message: MessageBoxMessage | null;
  onDismiss: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const dismiss = () => {
    const dialog = ref.current;
    if (dialog?.open) dialog.close();
    onDismiss();
  };

  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (message && !dialog.open) dialog.showModal();
    if (!message && dialog.open) dialog.close();
  }, [message]);

  return (
    <dialog
      className="message-box"
      ref={ref}
      onCancel={(event) => {
        event.preventDefault();
        dismiss();
      }}
      onClose={onDismiss}
    >
      <TitleBar title={message?.title ?? "RetroBox"} level="h2">
        <button className="close-button" type="button" onClick={dismiss} aria-label="Cerrar">
          ×
        </button>
      </TitleBar>
      <div className="message-box-body">
        <div className="message-box-content">
          <MessageBoxIcon kind={message?.kind ?? "info"} />
          <p>{message?.text}</p>
        </div>
        <div className="message-box-actions">
          <form method="dialog">
            <button autoFocus type="submit">
              Aceptar
            </button>
          </form>
        </div>
      </div>
    </dialog>
  );
}

function EmbeddedMessageBox({
  message,
  onDismiss,
}: {
  message: MessageBoxMessage | null;
  onDismiss: () => void;
}) {
  if (!message) return null;

  return (
    <div className="embedded-message-box-backdrop">
      <section className="message-box message-box-embedded" role="alertdialog" aria-modal="true">
        <TitleBar title={message.title} level="h2">
          <button className="close-button" type="button" onClick={onDismiss} aria-label="Cerrar">
            ×
          </button>
        </TitleBar>
        <div className="message-box-body">
          <div className="message-box-content">
            <MessageBoxIcon kind={message.kind ?? "info"} />
            <p>{message.text}</p>
          </div>
          <div className="message-box-actions">
            <button autoFocus type="button" onClick={onDismiss}>
              Aceptar
            </button>
          </div>
        </div>
      </section>
    </div>
  );
}

function MessageBoxIcon({ kind }: { kind: MessageBoxKind }) {
  if (kind === "error") {
    return (
      <svg className="message-box-icon" viewBox="0 0 32 32" aria-hidden="true">
        <circle cx="16" cy="16" r="14" fill="#c00" stroke="#000" strokeWidth="2" />
        <rect x="6" y="13" width="20" height="6" fill="#fff" stroke="#000" />
      </svg>
    );
  }
  if (kind === "warning") {
    return (
      <svg className="message-box-icon" viewBox="0 0 32 32" aria-hidden="true">
        <path d="m16 2 14 26H2z" fill="#fce94f" stroke="#000" strokeWidth="2" />
        <path d="M14 10h4v9h-4zm0 11h4v4h-4z" />
      </svg>
    );
  }
  return (
    <svg className="message-box-icon" viewBox="0 0 32 32" aria-hidden="true">
      <circle cx="16" cy="16" r="14" fill="#3465a4" stroke="#000" strokeWidth="2" />
      <path d="M14 7h4v4h-4zm0 6h4v11h-4z" fill="#fff" />
    </svg>
  );
}
