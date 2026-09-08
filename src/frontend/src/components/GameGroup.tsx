import type { Floppy } from "../api/types/Floppy";
import type { Game } from "../api/types/Game";
import type { MessageKey } from "../i18n";
import { apiUrl } from "../api/requests/http";
import { Icon } from "./Icon";

export function CoverSample({ title }: { title: string }) {
  return (
    <span className="cover-sample">
      <small>PC · DOS</small>
      <strong>{title}</strong>
      <small>MUESTRA</small>
    </span>
  );
}

export function GameGroup({
  game,
  collapsed,
  t,
  onToggle,
  onUpload,
  onCover,
  onEdit,
  onDelete,
  onRename,
  onNfc,
  onMode,
  onDeleteFloppy,
}: {
  game: Game;
  collapsed: boolean;
  t: (key: MessageKey) => string;
  onToggle: () => void;
  onUpload: () => void;
  onCover: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onRename: (floppy: Floppy) => void;
  onNfc: (floppy: Floppy) => void;
  onMode: (floppy: Floppy) => void;
  onDeleteFloppy: (floppy: Floppy) => void;
}) {
  return (
    <article className="game-group">
      <header className="item-header">
        <button className="cover-thumb" onClick={onCover} aria-label={t("cover")}>
          {game.cover ? (
            <img src={apiUrl(`/api/covers/${encodeURIComponent(game.cover)}`)} alt="" />
          ) : (
            <CoverSample title={game.label} />
          )}
        </button>
        <div className="item-heading">
          <h3>
            <button className="group-toggle" onClick={onToggle} aria-expanded={!collapsed}>
              <span aria-hidden="true">{collapsed ? "▸" : "▾"}</span> {game.label}
            </button>
          </h3>
          <p>
            {game.floppies.length} {t("disks")}
          </p>
        </div>
        <div className="row-actions">
          <button onClick={onCover}>{t("cover")}</button>
          <button onClick={onUpload}>{t("addDisk")}</button>
          <button
            className="icon-button"
            onClick={onEdit}
            aria-label={t("editGroup")}
            title={t("editGroup")}
          >
            <Icon name="pencil" />
          </button>
          <button
            className="icon-button"
            onClick={onDelete}
            aria-label={t("deleteGroup")}
            title={t("deleteGroup")}
          >
            <Icon name="trash" />
          </button>
        </div>
      </header>
      {!collapsed && (
        <table className="floppy-table">
          <thead>
            <tr>
              <th>{t("name")}</th>
              <th>{t("mode")}</th>
              <th>{t("nfc")}</th>
              <th>{t("actions")}</th>
            </tr>
          </thead>
          <tbody>
            {game.floppies.map((floppy) => (
              <tr key={floppy.id}>
                <td>{floppy.label}</td>
                <td className="mode-cell">
                  <span
                    className="mode-indicator"
                    tabIndex={0}
                    role="img"
                    aria-label={floppy.mode === "ro" ? t("readOnly") : t("readWrite")}
                    title={floppy.mode === "ro" ? t("readOnly") : t("readWrite")}
                  >
                    <Icon name={floppy.mode === "ro" ? "lock" : "unlock"} />
                  </span>
                </td>
                <td className="nfc-cell">
                  {floppy.nfc ? (
                    <span className="badge">NFC</span>
                  ) : (
                    <span className="badge untagged">{t("noNfc")}</span>
                  )}
                </td>
                <td>
                  <div className="row-actions">
                    <button
                      className="icon-button"
                      onClick={() => onMode(floppy)}
                      aria-label={
                        floppy.mode === "ro" ? t("changeToReadWrite") : t("changeToReadOnly")
                      }
                      title={floppy.mode === "ro" ? t("changeToReadWrite") : t("changeToReadOnly")}
                    >
                      <Icon name={floppy.mode === "ro" ? "unlock" : "lock"} />
                    </button>
                    <button
                      className="icon-button"
                      onClick={() => onNfc(floppy)}
                      aria-label={t("writeNfc")}
                      title={t("writeNfc")}
                    >
                      <Icon name="nfc" />
                    </button>
                    <button
                      className="icon-button"
                      onClick={() => onRename(floppy)}
                      aria-label={t("rename")}
                      title={t("rename")}
                    >
                      <Icon name="pencil" />
                    </button>
                    <button
                      className="icon-button"
                      onClick={() => onDeleteFloppy(floppy)}
                      aria-label={t("delete")}
                      title={t("delete")}
                    >
                      <Icon name="trash" />
                    </button>
                  </div>
                </td>
              </tr>
            )) || (
              <tr>
                <td colSpan={4} className="empty">
                  {t("noDisks")}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      )}
    </article>
  );
}
