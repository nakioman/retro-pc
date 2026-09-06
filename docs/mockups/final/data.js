"use strict";

// Fixtures only: no real credentials, hardware state, or valid backup archives.
window.RetroBoxDemo = {
  games: [
    { id: "monkey-island", title: "The Secret of Monkey Island", cover: { title: "MONKEY ISLAND", palette: "blue" }, floppies: [
      { id: "monkey1-disk1", name: "DISK01.IMG (VGA/EGA)", readOnly: true, tagged: true, tagId: "04:A1:3B:21:8C:10:80" },
      { id: "monkey1-disk2", name: "DISK02.IMG", readOnly: true, tagged: true, tagId: "04:A1:3B:22:8C:10:80" },
      { id: "monkey1-disk3", name: "DISK03.IMG", readOnly: true, tagged: false }
    ] },
    { id: "doom-ii", title: "Doom II: Hell on Earth", cover: { title: "DOOM II", palette: "red" }, floppies: [
      { id: "doom2-disk1", name: "DISK1_INSTALL.IMG", readOnly: true, tagged: true, tagId: "04:B2:1C:11:8C:10:80" },
      { id: "doom2-disk2", name: "DISK02.IMG", readOnly: false, tagged: false }
    ] }
  ],
  vms: [
    { id: "pentium200", name: "Pentium 200 — Windows 95", isRunning: true,
      cfg: "[General]\nvid_renderer = sdl\n\n[Machine]\nmachine = p55t2p4\ncpu_family = pentium_mmx\ncpu_speed = 200000000\nmemory = 64\n\n[Video]\ncard = voodoo\n",
      backups: [
        { id: "backup-1", date: "2026-09-01T14:30:00", size: "2.1 GB", label: "Instalación limpia Win95" },
        { id: "backup-2", date: "2026-09-05T09:15:00", size: "2.4 GB", label: "Antes de instalar parches" }
      ] },
    { id: "486dx2", name: "486DX2 — MS-DOS 6.22", isRunning: false,
      cfg: "[Machine]\nmachine = opti495\ncpu_family = i486dx2\ncpu_speed = 66666666\nmemory = 16\n", backups: [] }
  ],
  settings: { startCollapsed: false, language: "es", devId: "", devPassword: "", ssId: "", ssPassword: "", regionPriority: ["sp", "wor", "eu", "us"], languagePriority: ["es", "en"], requestTimeoutSeconds: 60, maxDownloadMegabytes: 16 }
};

// Additional synthetic groups make the pagination flow visible in this mockup.
for (let index = 3; index <= 12; index++) {
  window.RetroBoxDemo.games.push({
    id: `sample-game-${index}`,
    title: `Software de muestra ${String(index).padStart(2, "0")}`,
    cover: { title: `DOS ${index}`, palette: "gray" },
    floppies: [{ id: `sample-disk-${index}`, name: `SAMPLE${index}.IMG`, readOnly: true, tagged: false }]
  });
}
