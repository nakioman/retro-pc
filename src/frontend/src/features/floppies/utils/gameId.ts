const invalidIdCharacters = /[^a-z0-9]+/g;
const leadingOrTrailingDashes = /^-+|-+$/g;

export function createGameId(label: string, existingIds: ReadonlySet<string>) {
  const base = label
    .toLowerCase()
    .replace(invalidIdCharacters, "-")
    .replace(leadingOrTrailingDashes, "");

  if (!base) return null;

  let id = base;
  for (let suffix = 2; existingIds.has(id); suffix += 1) id = `${base}-${suffix}`;
  return id;
}
