import { NavLink } from "react-router-dom";
import type { MessageKey } from "../../i18n";

export function AppTabs({ t }: { t: (key: MessageKey) => string }) {
  return (
    <nav className="tabs" aria-label="Sections">
      <NavLink to="/floppies">{t("library")}</NavLink>
      <NavLink hidden to="/vms">
        {t("vms")}
      </NavLink>
    </nav>
  );
}
