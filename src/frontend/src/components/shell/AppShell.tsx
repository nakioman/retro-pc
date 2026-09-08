import { type ReactNode, useCallback, useMemo, useState } from "react";
import { translate, type Locale } from "../../i18n";
import { getUiPreferences, updateUiPreferences } from "../../state/uiPreferences";
import { SettingsDialog } from "../settings/SettingsDialog";
import { MessageBox, type MessageBoxAction, type MessageBoxMessage } from "../MessageBox";
import { AppTabs } from "./AppTabs";
import { AppMenu } from "./AppMenu";
import { MenuBar } from "./MenuBar";
import { StatusBar } from "./StatusBar";
import { TitleBar } from "./TitleBar";
import { Window } from "./Window";
import { AppShellContext, type Translator } from "./AppShellContext";
import { useAppShell } from "../../hooks/useAppShell";

export function AppShellProvider({ children }: { children: ReactNode }) {
  const [locale, setLocale] = useState<Locale>(() => getUiPreferences().locale);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [message, setMessage] = useState<{
    content: MessageBoxMessage;
    actions?: readonly MessageBoxAction[];
  } | null>(null);
  const t = useCallback<Translator>((key, values) => translate(locale, key, values), [locale]);
  const openSettings = useCallback(() => setSettingsOpen(true), []);
  const showMessage = useCallback(
    (content: MessageBoxMessage, actions?: readonly MessageBoxAction[]) =>
      setMessage({ content, actions }),
    [],
  );
  const changeLocale = useCallback((value: Locale) => {
    setLocale(value);
    updateUiPreferences({ locale: value });
  }, []);
  const value = useMemo(
    () => ({ locale, t, openSettings, showMessage }),
    [locale, openSettings, showMessage, t],
  );

  return (
    <AppShellContext value={value}>
      {children}
      <SettingsDialog
        open={settingsOpen}
        locale={locale}
        t={t}
        onLocale={changeLocale}
        onClose={() => setSettingsOpen(false)}
      />
      <MessageBox
        message={message?.content ?? null}
        actions={message?.actions}
        onDismiss={() => setMessage(null)}
      />
    </AppShellContext>
  );
}

export function AppShell({ status, children }: { status: ReactNode; children: ReactNode }) {
  const { openSettings, t } = useAppShell();
  return (
    <Window>
      <TitleBar title={t("appTitle")} />
      <MenuBar>
        <AppMenu t={t} onOpenSettings={openSettings} />
      </MenuBar>
      <AppTabs t={t} />
      {children}
      <StatusBar>{status}</StatusBar>
    </Window>
  );
}
