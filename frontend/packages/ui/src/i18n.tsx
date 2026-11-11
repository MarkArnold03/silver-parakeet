import { createContext, useContext, useMemo, useState, type ReactNode } from "react";

export type Lang = "sv" | "en";

/**
 * A typed two-language dictionary instead of an i18n library. TypeScript checks that Swedish and
 * English have exactly the same keys, and that the code only asks for texts that exist.
 */
export function createI18n<Texts>(texts: Record<Lang, Texts>) {
  const Context = createContext<{ lang: Lang; setLang: (lang: Lang) => void; t: Texts } | null>(null);

  function I18nProvider({ initial, children }: { initial?: Lang; children: ReactNode }) {
    const [lang, setLang] = useState<Lang>(initial ?? browserLang());
    const value = useMemo(() => ({ lang, setLang, t: texts[lang] }), [lang]);
    return <Context.Provider value={value}>{children}</Context.Provider>;
  }

  function useI18n() {
    const value = useContext(Context);
    if (!value) throw new Error("useI18n must be used inside I18nProvider");
    return value;
  }

  return { I18nProvider, useI18n };
}

/** Swedish unless the phone is clearly set to another language. */
function browserLang(): Lang {
  return typeof navigator !== "undefined" && !navigator.language.toLowerCase().startsWith("sv") ? "en" : "sv";
}
