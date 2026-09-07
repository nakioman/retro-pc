import { useNavigate } from "react-router-dom";
import type { MessageKey } from "../../i18n";
import { Menu, MenuAction } from "./MenuBar";

export function AppMenu({
  t,
  onOpenSettings,
}: {
  t: (key: MessageKey) => string;
  onOpenSettings: () => void;
}) {
  const navigate = useNavigate();
  return (
    <>
      <Menu label={t("file")}>
        <button onClick={() => navigate("/floppies?dialog=upload")}>{t("upload")}</button>
        <button hidden onClick={() => navigate("/vms?dialog=create")}>
          {t("createVm")}
        </button>
      </Menu>
      <MenuAction onClick={onOpenSettings}>{t("options")}</MenuAction>
    </>
  );
}
