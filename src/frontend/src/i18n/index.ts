import { en } from "./locales/en";
import { es } from "./locales/es";

export type Locale = "es" | "en";
export type MessageKey = keyof typeof es;

const messages = { es, en } satisfies Record<Locale, Record<MessageKey, string>>;

export function translate(
  locale: Locale,
  key: MessageKey,
  values: Record<string, string | number> = {},
) {
  return Object.entries(values).reduce(
    (text, [name, value]) => text.replace(`{${name}}`, String(value)),
    messages[locale][key],
  );
}
