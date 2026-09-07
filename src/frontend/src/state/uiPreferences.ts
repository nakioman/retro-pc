import type { Locale } from "../i18n";

const storageKey = "retrobox.ui-preferences.v1";

interface UiPreferences {
  version: 1;
  locale: Locale;
  collapsedGameIds: string[];
  startCollapsed: boolean;
}

const defaults: UiPreferences = {
  version: 1,
  locale: "es",
  collapsedGameIds: [],
  startCollapsed: false,
};
let cached: UiPreferences | undefined;

export function getUiPreferences(): UiPreferences {
  if (cached) return cached;
  try {
    const parsed = JSON.parse(
      localStorage.getItem(storageKey) ?? "null",
    ) as Partial<UiPreferences> | null;
    cached =
      parsed?.version === 1 && (parsed.locale === "es" || parsed.locale === "en")
        ? {
            ...defaults,
            ...parsed,
            collapsedGameIds: Array.isArray(parsed.collapsedGameIds) ? parsed.collapsedGameIds : [],
          }
        : defaults;
  } catch {
    cached = defaults;
  }
  return cached;
}

export function updateUiPreferences(update: Partial<Omit<UiPreferences, "version">>): void {
  cached = { ...getUiPreferences(), ...update };
  localStorage.setItem(storageKey, JSON.stringify(cached));
}
