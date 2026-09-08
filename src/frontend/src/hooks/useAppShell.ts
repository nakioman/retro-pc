import { use } from "react";
import { AppShellContext } from "../components/shell/AppShellContext";

export function useAppShell() {
  const value = use(AppShellContext);
  if (!value) throw new Error("useAppShell must be used inside AppShellProvider.");
  return value;
}
