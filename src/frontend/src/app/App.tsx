import { Navigate, Route, Routes } from "react-router-dom";
import { AppShellProvider } from "../components/shell/AppShell";
import { FloppiesPage } from "../features/floppies/FloppiesPage";
import { VmsPage } from "../features/vms/VmsPage";

export default function App() {
  return (
    <AppShellProvider>
      <Routes>
        <Route path="/" element={<Navigate to="/floppies" replace />} />
        <Route path="/floppies" element={<FloppiesPage />} />
        <Route path="/vms" element={<VmsPage />} />
      </Routes>
    </AppShellProvider>
  );
}
