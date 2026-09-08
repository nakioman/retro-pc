import type { ReactNode } from "react";

export function MenuBar({ children }: { children: ReactNode }) {
  return (
    <nav className="menu-bar" aria-label="Main menu">
      {children}
    </nav>
  );
}

export function Menu({ label, children }: { label: string; children: ReactNode }) {
  return (
    <details>
      <summary>{label}</summary>
      <div className="menu-popup">{children}</div>
    </details>
  );
}

export function MenuAction({ children, onClick }: { children: ReactNode; onClick: () => void }) {
  return (
    <button className="menu-button" onClick={onClick}>
      {children}
    </button>
  );
}
