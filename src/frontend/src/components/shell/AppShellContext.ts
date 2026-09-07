import { createContext } from "react";
import type { Locale, MessageKey } from "../../i18n";
import type { MessageBoxMessage } from "../MessageBox";

export type Translator = (key: MessageKey, values?: Record<string, string | number>) => string;

export type AppShellContextValue = {
  locale: Locale;
  t: Translator;
  openSettings: () => void;
  showMessage: (message: MessageBoxMessage) => void;
};

export const AppShellContext = createContext<AppShellContextValue | null>(null);
