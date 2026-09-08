import { memo } from "react";
import type { Floppy } from "../../../api/types/Floppy";
import type { Game } from "../../../api/types/Game";
import { GameGroup } from "../../../components/GameGroup";
import type { MessageKey } from "../../../i18n";

export const LibraryGameList = memo(function LibraryGameList({
  games,
  collapsed,
  t,
  onToggle,
  onUpload,
  onCover,
  onEdit,
  onDeleteGame,
  onRename,
  onNfc,
  onToggleMode,
  onDeleteFloppy,
}: {
  games: Game[];
  collapsed: ReadonlySet<string>;
  t: (key: MessageKey) => string;
  onToggle: (id: string) => void;
  onUpload: (game: Game) => void;
  onCover: (game: Game) => void;
  onEdit: (game: Game) => void;
  onDeleteGame: (game: Game) => void;
  onRename: (floppy: Floppy) => void;
  onNfc: (floppy: Floppy) => void;
  onToggleMode: (floppy: Floppy) => void;
  onDeleteFloppy: (floppy: Floppy) => void;
}) {
  return games.map((game) => (
    <GameGroup
      key={game.id}
      game={game}
      collapsed={collapsed.has(game.id)}
      t={t}
      onToggle={() => onToggle(game.id)}
      onUpload={() => onUpload(game)}
      onCover={() => onCover(game)}
      onEdit={() => onEdit(game)}
      onDelete={() => onDeleteGame(game)}
      onRename={onRename}
      onNfc={onNfc}
      onMode={onToggleMode}
      onDeleteFloppy={onDeleteFloppy}
    />
  ));
});
