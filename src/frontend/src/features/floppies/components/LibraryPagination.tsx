import type { MessageKey } from "../../../i18n";

export function LibraryPagination({
  page,
  pages,
  count,
  t,
  onPrevious,
  onNext,
}: {
  page: number;
  pages: number;
  count: number;
  t: (key: MessageKey, values?: Record<string, number>) => string;
  onPrevious: () => void;
  onNext: () => void;
}) {
  return (
    <nav className="pagination" aria-label="Pagination">
      <span>{t("perPage")}</span>
      <span>{t("page", { page, pages, count })}</span>
      <button disabled={page <= 1} onClick={onPrevious}>
        {t("previous")}
      </button>
      <button disabled={page >= pages} onClick={onNext}>
        {t("next")}
      </button>
    </nav>
  );
}
