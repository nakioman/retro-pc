import { useEffect, useState } from "react";
import { api } from "../../api/client";
import { isApiError } from "../../api/types/ApiError";
import type { ScraperSettings } from "../../api/types/ScraperSettings";
import { useAppShell } from "../../hooks/useAppShell";
import { getUiPreferences, updateUiPreferences } from "../../state/uiPreferences";
import { Dialog } from "../Dialog";
import type { Locale, MessageKey } from "../../i18n";

const credentialFields = [
  ["devId", "ID de desarrollador"],
  ["devPassword", "Contraseña de desarrollador"],
  ["ssId", "ID de ScreenScraper"],
  ["ssPassword", "Contraseña de ScreenScraper"],
] as const;

export function SettingsDialog({
  open,
  locale,
  t,
  onLocale,
  onClose,
}: {
  open: boolean;
  locale: Locale;
  t: (key: MessageKey) => string;
  onLocale: (locale: Locale) => void;
  onClose: () => void;
}) {
  const [settings, setSettings] = useState<ScraperSettings | null>(null);
  const [dirty, setDirty] = useState<Record<string, string>>({});
  const [collapsed, setCollapsed] = useState(false);
  const { showMessage } = useAppShell();

  useEffect(() => {
    if (!open) return;
    void api
      .settings()
      .then(setSettings)
      .catch(() =>
        showMessage({
          title: "RetroBox",
          text: "No se pudo cargar la configuración.",
          kind: "error",
        }),
      );
    setDirty({});
    setCollapsed(getUiPreferences().startCollapsed);
  }, [open, showMessage]);

  const save = async () => {
    if (!settings) return;
    try {
      await api.updateSettings({
        regionPriority: settings.regionPriority,
        languagePriority: settings.languagePriority,
        requestTimeoutSeconds: settings.requestTimeoutSeconds,
        maxDownloadMegabytes: settings.maxDownloadMegabytes,
        ...dirty,
      });
      updateUiPreferences({ startCollapsed: collapsed });
      onClose();
    } catch (value) {
      showMessage({
        title: "RetroBox",
        text: isApiError(value) ? value.message : "No se pudo guardar la configuración.",
        kind: "error",
      });
    }
  };

  const move = (field: "regionPriority" | "languagePriority", index: number, step: number) =>
    setSettings((value) => {
      if (!value) return value;
      const list = [...value[field]];
      [list[index], list[index + step]] = [list[index + step], list[index]];
      return { ...value, [field]: list };
    });

  const testCredentials = async () => {
    try {
      await api.testSettings();
      showMessage({ title: "RetroBox", text: t("credentialsTestSucceeded"), kind: "info" });
    } catch {
      showMessage({ title: "RetroBox", text: t("credentialsTestFailed"), kind: "error" });
    }
  };

  return (
    <Dialog
      open={open}
      title={t("settings")}
      onClose={onClose}
      actions={
        <>
          <button onClick={() => void save()}>{t("save")}</button>
          <button onClick={onClose}>{t("cancel")}</button>
        </>
      }
    >
      {settings ? (
        <>
          <fieldset>
            <legend>{t("library")}</legend>
            <label className="checkbox-field">
              <input
                type="checkbox"
                checked={collapsed}
                onChange={(event) => setCollapsed(event.target.checked)}
              />
              {t("startCollapsed")}
            </label>
            <label>
              {t("language")}
              <select value={locale} onChange={(event) => onLocale(event.target.value as Locale)}>
                <option value="es">Español</option>
                <option value="en">English</option>
              </select>
            </label>
          </fieldset>
          <fieldset>
            <legend>{t("scraper")}</legend>
            <div className="field-grid">
              {credentialFields.map(([name, label]) => (
                <label key={name}>
                  {label}
                  <input
                    type={name.includes("Password") ? "password" : "text"}
                    value={dirty[name] ?? ""}
                    onChange={(event) =>
                      setDirty((values) => ({ ...values, [name]: event.target.value }))
                    }
                  />
                </label>
              ))}
            </div>
            <button className="test-credentials-button" onClick={() => void testCredentials()}>
              {t("testCredentials")}
            </button>
          </fieldset>
          <div className="field-grid">
            {(["regionPriority", "languagePriority"] as const).map((field) => (
              <fieldset key={field}>
                <legend>{field === "regionPriority" ? t("region") : t("metadataLanguage")}</legend>
                {settings[field].map((item, index) => (
                  <div className="priority-row" key={item}>
                    <span>
                      {index + 1}. {item}
                    </span>
                    <button disabled={index === 0} onClick={() => move(field, index, -1)}>
                      ↑
                    </button>
                    <button
                      disabled={index === settings[field].length - 1}
                      onClick={() => move(field, index, 1)}
                    >
                      ↓
                    </button>
                  </div>
                ))}
              </fieldset>
            ))}
          </div>
          <div className="field-grid">
            <label>
              {t("timeout")}
              <input
                type="number"
                min="5"
                max="600"
                value={settings.requestTimeoutSeconds}
                onChange={(event) =>
                  setSettings({ ...settings, requestTimeoutSeconds: Number(event.target.value) })
                }
              />
            </label>
            <label>
              {t("maximum")}
              <input
                type="number"
                min="1"
                max="64"
                value={settings.maxDownloadMegabytes}
                onChange={(event) =>
                  setSettings({ ...settings, maxDownloadMegabytes: Number(event.target.value) })
                }
              />
            </label>
          </div>
        </>
      ) : null}
    </Dialog>
  );
}
