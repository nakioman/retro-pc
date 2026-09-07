export type IconName = "lock" | "unlock" | "pencil" | "nfc" | "trash";

const paths: Record<IconName, string> = {
  lock: "M4 7V5a4 4 0 0 1 8 0v2M3 7h10v8H3zM8 10v2",
  unlock: "M5 7V4a3 3 0 0 1 6 0M3 7h10v8H3zM8 10v2",
  pencil: "M2 11l9-9 3 3-9 9-4 1zM9 4l3 3",
  nfc: "M2 3h6v10H2zM10 5q3 3 0 6M12 2q6 6 0 12",
  trash: "M2 4h12M6 4V2h4v2M4 4l1 10h6l1-10M7 6v6M9 6v6",
};

export function Icon({ name }: { name: IconName }) {
  return (
    <svg viewBox="0 0 16 16" aria-hidden="true">
      <path d={paths[name]} />
    </svg>
  );
}
