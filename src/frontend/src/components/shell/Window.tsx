import type { ReactNode } from "react";

export function Window({ children }: { children: ReactNode }) {
  return <main className="window">{children}</main>;
}
