import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { Dialog } from "../../components/Dialog";
import { Icon } from "../../components/Icon";
import { AppShell } from "../../components/shell/AppShell";
import { useAppShell } from "../../hooks/useAppShell";

type MockVm = {
  id: string;
  name: string;
  isRunning: boolean;
  cfg: string;
  backups: { id: string; date: string; size: string; label: string }[];
};

const mockVms: MockVm[] = [
  {
    id: "pentium200",
    name: "Pentium 200 — Windows 95",
    isRunning: true,
    cfg: "[General]\nvid_renderer = sdl\n\n[Machine]\nmachine = p55t2p4\ncpu_family = pentium_mmx",
    backups: [
      {
        id: "backup-1",
        date: "2026-09-01 14:30",
        size: "2.1 GB",
        label: "Instalación limpia Win95",
      },
      {
        id: "backup-2",
        date: "2026-09-05 09:15",
        size: "2.4 GB",
        label: "Antes de instalar parches",
      },
    ],
  },
  {
    id: "486dx2",
    name: "486DX2 — MS-DOS 6.22",
    isRunning: false,
    cfg: "[Machine]\nmachine = opti495\ncpu_family = i486dx2\ncpu_speed = 66666666",
    backups: [],
  },
];

export function VmsPage() {
  const [query, setQuery] = useState("");
  const [editorTarget, setEditorTarget] = useState<MockVm | null>(null);
  const [backupsTarget, setBackupsTarget] = useState<MockVm | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();
  const { t } = useAppShell();
  const normalizedQuery = query.toLocaleLowerCase();
  const visibleVms = useMemo(
    () => mockVms.filter((vm) => vm.name.toLocaleLowerCase().includes(normalizedQuery)),
    [normalizedQuery],
  );

  useEffect(() => {
    if (searchParams.get("dialog") === "create") setEditorTarget(mockVms[0]);
  }, [searchParams]);

  const closeEditor = () => {
    setEditorTarget(null);
    setSearchParams({}, { replace: true });
  };

  return (
    <AppShell status={`${mockVms.length} máquina(s) virtual(es)`}>
      <section className="view" aria-label={t("vms")}>
        <div className="toolbar">
          <button onClick={() => setEditorTarget(mockVms[0])}>Crear VM…</button>
          <label className="search">
            Buscar:{" "}
            <input
              type="search"
              placeholder="Nombre de la máquina"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
            />
          </label>
        </div>
        <div className="workspace" aria-label={t("vms")}>
          {visibleVms.map((vm) => (
            <article className="vm-group" key={vm.id}>
              <header className="item-header">
                <span className="app-mark" aria-hidden="true" />
                <div className="item-heading">
                  <h3>{vm.name}</h3>
                </div>
                <span className={`badge${vm.isRunning ? " running" : ""}`}>
                  {vm.isRunning ? "En ejecución" : "Detenida"}
                </span>
              </header>
              <div className="vm-details">
                <p>
                  <strong>Backups disponibles:</strong> {vm.backups.length} copia(s)
                </p>
                <div className="row-actions">
                  <button onClick={() => setEditorTarget(vm)}>Editar nombre / CFG…</button>
                  <button onClick={() => setBackupsTarget(vm)}>Administrar backups…</button>
                  <button
                    className="icon-button"
                    aria-label={`Eliminar VM ${vm.name}`}
                    title={`Eliminar VM ${vm.name}`}
                  >
                    <Icon name="trash" />
                  </button>
                </div>
              </div>
            </article>
          ))}
          {visibleVms.length === 0 ? (
            <p className="empty">No hay máquinas que coincidan con la búsqueda.</p>
          ) : null}
        </div>
      </section>
      <VmEditorDialog target={editorTarget} onClose={closeEditor} />
      <VmBackupsDialog target={backupsTarget} onClose={() => setBackupsTarget(null)} />
    </AppShell>
  );
}

function VmEditorDialog({ target, onClose }: { target: MockVm | null; onClose: () => void }) {
  return (
    <Dialog
      open={Boolean(target)}
      title="Crear VM"
      onClose={onClose}
      actions={
        <>
          <button onClick={onClose}>Crear</button>
          <button onClick={onClose}>Cancelar</button>
        </>
      }
    >
      <label>
        Nombre de la VM
        <input defaultValue={target?.name ?? ""} />
      </label>
      <label>
        Configuración de 86Box (86box.cfg)
        <textarea className="cfg-editor" defaultValue={target?.cfg ?? ""} />
      </label>
      <p className="hint">
        Copiá la configuración desde tu 86Box. El hardware se define en este archivo.
      </p>
    </Dialog>
  );
}

function VmBackupsDialog({ target, onClose }: { target: MockVm | null; onClose: () => void }) {
  return (
    <Dialog
      open={Boolean(target)}
      title="Backups"
      onClose={onClose}
      actions={<button onClick={onClose}>Cerrar</button>}
    >
      <div className="section-toolbar">
        <p>Copias de seguridad de esta máquina</p>
        <button>Crear backup…</button>
      </div>
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Fecha / Hora</th>
              <th>Tamaño</th>
              <th>Etiqueta</th>
              <th>Acciones</th>
            </tr>
          </thead>
          <tbody>
            {target?.backups.map((backup) => (
              <tr key={backup.id}>
                <td>{backup.date}</td>
                <td>{backup.size}</td>
                <td>{backup.label}</td>
                <td>
                  <button>Descargar</button> <button>Eliminar</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="hint">Demostración: las copias y sus tamaños son simulados.</p>
    </Dialog>
  );
}
