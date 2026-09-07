import type { ReactNode } from "react";

export function StatusBar({ children }: { children: ReactNode }) {
  return (
    <footer className="status-bar">
      <span>{children}</span>
      <span>RetroBox</span>
    </footer>
  );
}
