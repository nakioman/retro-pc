import type { ChangeEventHandler } from "react";
import type { MessageKey } from "../../../i18n";

export function LibraryToolbar({
  query,
  t,
  onUpload,
  onExpand,
  onCollapse,
  onQueryChange,
}: {
  query: string;
  t: (key: MessageKey) => string;
  onUpload: () => void;
  onExpand: () => void;
  onCollapse: () => void;
  onQueryChange: ChangeEventHandler<HTMLInputElement>;
}) {
  return (
    <div className="toolbar">
      <button onClick={onUpload}>{t("upload")}</button>
      <button onClick={onExpand}>{t("expand")}</button>
      <button onClick={onCollapse}>{t("collapse")}</button>
      <label className="search">
        {t("search")}{" "}
        <input
          type="search"
          value={query}
          placeholder={t("searchFloppies")}
          onChange={onQueryChange}
        />
      </label>
    </div>
  );
}
