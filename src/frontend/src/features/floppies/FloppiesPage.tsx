import { useCallback, useDeferredValue, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { api } from "../../api/client";
import type { Floppy } from "../../api/types/Floppy";
import type { Game } from "../../api/types/Game";
import { LibraryGameList } from "./components/LibraryGameList";
import { LibraryPagination } from "./components/LibraryPagination";
import { LibraryToolbar } from "./components/LibraryToolbar";
import { ConfirmDeleteDialog, type DeleteTarget } from "./components/dialogs/ConfirmDeleteDialog";
import { CoverDialog } from "./components/dialogs/CoverDialog";
import { EditGameDialog } from "./components/dialogs/EditGameDialog";
import { NfcDialog } from "./components/dialogs/NfcDialog";
import { RenameFloppyDialog } from "./components/dialogs/RenameFloppyDialog";
import { UploadDialog } from "./components/dialogs/UploadDialog";
import { useFloppyCatalog } from "./hooks/useFloppyCatalog";
import { AppShell } from "../../components/shell/AppShell";
import { useAppShell } from "../../hooks/useAppShell";
import { getUiPreferences, updateUiPreferences } from "../../state/uiPreferences";

const pageSize = 10;

export function FloppiesPage() {
  const [query, setQuery] = useState("");
  const deferredQuery = useDeferredValue(query);
  const [page, setPage] = useState(1);
  const [collapsed, setCollapsed] = useState<Set<string>>(
    () => new Set(getUiPreferences().collapsedGameIds),
  );
  const [uploadTarget, setUploadTarget] = useState<Game | null | undefined>(undefined);
  const [renameTarget, setRenameTarget] = useState<Floppy | null>(null);
  const [editGame, setEditGame] = useState<Game | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<DeleteTarget>(null);
  const [nfcTarget, setNfcTarget] = useState<Floppy | null>(null);
  const [coverTarget, setCoverTarget] = useState<Game | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();
  const { catalog, error, reload, run, setError } = useFloppyCatalog();
  const { locale, showMessage, t } = useAppShell();

  useEffect(() => {
    updateUiPreferences({ collapsedGameIds: [...collapsed] });
  }, [collapsed]);
  useEffect(() => {
    if (searchParams.get("dialog") === "upload") setUploadTarget(null);
  }, [searchParams]);

  const gameIds = useMemo(() => new Set(catalog?.games.map((game) => game.id) ?? []), [catalog]);
  const searchableGames = useMemo(
    () =>
      (catalog?.games ?? []).map((game) => ({
        game,
        searchText:
          `${game.label} ${game.floppies.map((floppy) => floppy.label).join(" ")}`.toLocaleLowerCase(
            locale,
          ),
      })),
    [catalog, locale],
  );
  const normalizedQuery = deferredQuery.toLocaleLowerCase(locale);
  const games = useMemo(
    () =>
      normalizedQuery
        ? searchableGames
            .filter(({ searchText }) => searchText.includes(normalizedQuery))
            .map(({ game }) => game)
        : searchableGames.map(({ game }) => game),
    [normalizedQuery, searchableGames],
  );
  const pages = Math.max(1, Math.ceil(games.length / pageSize));
  const currentPage = Math.min(page, pages);
  const visibleGames = games.slice((currentPage - 1) * pageSize, currentPage * pageSize);

  const toggle = useCallback((id: string) => {
    setCollapsed((previous) => {
      const next = new Set(previous);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);
  const openAll = useCallback(() => setCollapsed(new Set()), []);
  const closeAll = useCallback(() => setCollapsed(new Set(gameIds)), [gameIds]);
  const notifyAfterReload = useCallback(
    (message: string) => {
      void reload();
      showMessage({ title: "RetroBox", text: message });
    },
    [reload, showMessage],
  );
  const toggleMode = useCallback(
    (floppy: Floppy) =>
      void run(
        () => api.updateFloppy(floppy.id, { mode: floppy.mode === "ro" ? "rw" : "ro" }),
        () => undefined,
      ),
    [run],
  );
  const confirmDelete = useCallback(() => {
    if (!deleteTarget) return;
    const job =
      deleteTarget.type === "game"
        ? () => api.deleteGame(deleteTarget.game.id)
        : () => api.deleteFloppy(deleteTarget.floppy.id);
    void run(job, () => undefined);
    setDeleteTarget(null);
  }, [deleteTarget, run]);
  const openUpload = useCallback(() => setUploadTarget(null), []);
  const closeUpload = useCallback(() => {
    setUploadTarget(undefined);
    setSearchParams({}, { replace: true });
  }, [setSearchParams]);
  const deleteGame = useCallback((game: Game) => setDeleteTarget({ type: "game", game }), []);
  const deleteFloppy = useCallback(
    (floppy: Floppy) => setDeleteTarget({ type: "floppy", floppy }),
    [],
  );
  const changeQuery = useCallback((value: string) => {
    setQuery(value);
    setPage(1);
  }, []);

  return (
    <AppShell
      status={
        catalog ? `${catalog.games.length} juego(s) · ${catalog.floppies.length} disquete(s)` : ""
      }
    >
      <section className="view" aria-label={t("library")}>
        <LibraryToolbar
          query={query}
          t={t}
          onUpload={openUpload}
          onExpand={openAll}
          onCollapse={closeAll}
          onQueryChange={(event) => changeQuery(event.target.value)}
        />
        {catalog?.catalogError ? (
          <p className="error" role="alert">
            {catalog.catalogError}
          </p>
        ) : null}
        {error ? (
          <p className="error" role="alert">
            {error}
          </p>
        ) : null}
        <div className="workspace">
          {catalog === null ? <p className="empty">{t("loading")}</p> : null}
          {catalog !== null ? (
            <LibraryGameList
              games={visibleGames}
              collapsed={collapsed}
              t={t}
              onToggle={toggle}
              onUpload={setUploadTarget}
              onCover={setCoverTarget}
              onEdit={setEditGame}
              onDeleteGame={deleteGame}
              onRename={setRenameTarget}
              onNfc={setNfcTarget}
              onToggleMode={toggleMode}
              onDeleteFloppy={deleteFloppy}
            />
          ) : null}
          {catalog !== null && visibleGames.length === 0 ? (
            <p className="empty">{query ? t("noMatches") : t("emptyLibrary")}</p>
          ) : null}
        </div>
        <LibraryPagination
          page={currentPage}
          pages={pages}
          count={games.length}
          t={t}
          onPrevious={() => setPage((value) => value - 1)}
          onNext={() => setPage((value) => value + 1)}
        />
      </section>
      <UploadDialog
        open={uploadTarget !== undefined}
        game={uploadTarget}
        t={t}
        existingIds={gameIds}
        onClose={closeUpload}
        onDone={(message) => {
          closeUpload();
          notifyAfterReload(message);
        }}
        onError={setError}
      />
      <RenameFloppyDialog
        target={renameTarget}
        t={t}
        onClose={() => setRenameTarget(null)}
        onSave={(label) =>
          void run(
            () => api.updateFloppy(renameTarget!.id, { label }),
            () => undefined,
          )
        }
      />
      <EditGameDialog
        target={editGame}
        t={t}
        onClose={() => setEditGame(null)}
        onSave={(label) =>
          void run(
            () =>
              api.updateGame(
                editGame!.id,
                label,
                editGame!.floppies.map((floppy) => floppy.id),
              ),
            () => undefined,
          )
        }
      />
      <ConfirmDeleteDialog
        target={deleteTarget}
        t={t}
        onClose={() => setDeleteTarget(null)}
        onConfirm={confirmDelete}
      />
      <NfcDialog
        target={nfcTarget}
        t={t}
        onClose={() => setNfcTarget(null)}
        onDone={() => {
          setNfcTarget(null);
          void reload();
        }}
      />
      <CoverDialog
        target={coverTarget}
        t={t}
        onClose={() => setCoverTarget(null)}
        onDone={() => {
          setCoverTarget(null);
          void reload();
        }}
        onError={setError}
      />
    </AppShell>
  );
}
