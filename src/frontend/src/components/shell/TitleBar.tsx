import { createElement, type ReactNode } from "react";

export function TitleBar({
  title,
  level = "h1",
  children,
}: {
  title: string;
  level?: "h1" | "h2";
  children?: ReactNode;
}) {
  return (
    <header className="title-bar">
      {level === "h1" ? <span className="app-mark" aria-hidden="true" /> : null}
      {createElement(level, null, title)}
      {children}
    </header>
  );
}
