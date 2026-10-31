import type { ReactNode } from "react";
import { formatFreeSpace, useDataFreeSpace } from "../../hooks/useDataFreeSpace";
import { useAppShell } from "../../hooks/useAppShell";

export function StatusBar({ children }: { children: ReactNode }) {
  const { t } = useAppShell();
  const freeBytes = useDataFreeSpace();
  return (
    <footer className="status-bar">
      <span>{children}</span>
      <span>RetroBox</span>
      {freeBytes !== null && (
        <span>{t("dataFreeSpace", { size: formatFreeSpace(freeBytes) })}</span>
      )}
    </footer>
  );
}
