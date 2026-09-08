import { getCatalog } from "./requests/catalog";
import { createGame, deleteGame, updateGame } from "./requests/games";
import { deleteFloppy, updateFloppy, uploadFloppy } from "./requests/floppies";
import { getDrive, writeNfc } from "./requests/nfc";
import {
  getSettings,
  searchCovers,
  selectCover,
  testSettings,
  updateSettings,
  uploadCover,
} from "./requests/scraper";

export const api = {
  catalog: getCatalog,
  drive: getDrive,
  settings: getSettings,
  updateSettings,
  testSettings,
  createGame,
  updateGame,
  deleteGame,
  uploadFloppy,
  updateFloppy,
  deleteFloppy,
  writeNfc,
  searchCovers,
  selectCover,
  uploadCover,
};
