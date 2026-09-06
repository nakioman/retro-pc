"use strict";

(() => {
  const state = structuredClone(window.RetroBoxDemo);
  const $ = (selector) => document.querySelector(selector);
  const field = (form, name) => form.elements.namedItem(name);
  const escape = (value) => String(value).replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[char]);
  let serial = 0;
  let libraryPage = 1;
  const pageSize = 10;
  const collapsedGames = new Set(state.settings.startCollapsed ? state.games.map((game) => game.id) : []);
  const nextId = (prefix) => `${prefix}-demo-${++serial}`;
  let activeVmId = null;
  let activeGameId = null;
  let activeFloppyId = null;
  let coverDraft = null;
  let settingsDraft = null;
  let confirmAction = null;
  let noticeTimer;
  let searchTimer;
  const objectUrls = new Set();
  const currentVm = () => state.vms.find((vm) => vm.id === activeVmId);
  const currentGame = () => state.games.find((game) => game.id === activeGameId);
  const currentFloppy = () => currentGame()?.floppies.find((floppy) => floppy.id === activeFloppyId);

  const icons = {
    lock: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M4 7V5a4 4 0 018 0v2M3 7h10v8H3zM8 10v2"/></svg>',
    unlock: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M5 7V4a3 3 0 016 0M3 7h10v8H3zM8 10v2"/></svg>',
    pencil: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 11l9-9 3 3-9 9-4 1zM9 4l3 3"/></svg>',
    nfc: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 3h6v10H2zM10 5q3 3 0 6M12 2q6 6 0 12"/></svg>',

    trash: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4h12M6 4V2h4v2M4 4l1 10h6l1-10M7 6v6M9 6v6"/></svg>',
    disk: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 1h10l2 2v12H2zM5 1v5h6V1M5 15V9h6v6M9 2v3"/></svg>'
  };
  const iconButton = (action, icon, label) => `<button class="icon-button" data-action="${action}" aria-label="${escape(label)}" title="${escape(label)}">${icons[icon]}</button>`;

  function notify(message) {
    clearTimeout(noticeTimer);
    $("#notification").textContent = message;
    $("#notification").hidden = false;
    noticeTimer = setTimeout(() => { $("#notification").hidden = true; }, 4500);
  }

  function openDialog(id) {
    document.querySelectorAll("details[open]").forEach((menu) => { menu.open = false; });
    $(id).showModal();
  }

  function confirmDelete(message, action) {
    $("#confirm-message").textContent = message;
    confirmAction = action;
    openDialog("#confirm-dialog");
  }

  function coverMarkup(cover) {
    if (cover.url) return `<img src="${escape(cover.url)}" alt="Carátula seleccionada">`;
    return `<span class="cover-sample" data-palette="${escape(cover.palette)}"><small>PC · DOS</small><strong>${escape(cover.title)}</strong><small>MUESTRA</small></span>`;
  }

  function renderLibrary() {
    const query = $("#floppy-search").value.trim().toLocaleLowerCase();
    const matches = state.games.filter((game) => `${game.title} ${game.floppies.map((floppy) => floppy.name).join(" ")}`.toLocaleLowerCase().includes(query));
    const pageCount = Math.max(1, Math.ceil(matches.length / pageSize));
    libraryPage = Math.max(1, Math.min(libraryPage, pageCount));
    const visibleGames = matches.slice((libraryPage - 1) * pageSize, libraryPage * pageSize);
    $("#page-info").textContent = `Página ${libraryPage} de ${pageCount} · ${matches.length} juego(s)`;
    $("#previous-page").disabled = libraryPage === 1;
    $("#next-page").disabled = libraryPage === pageCount;
    $("#library").innerHTML = visibleGames.map((game) => `
      <article class="game-group" data-game="${escape(game.id)}">
        <header class="item-header">
          <button class="cover-thumb" data-action="cover" aria-label="Cambiar carátula de ${escape(game.title)}">${coverMarkup(game.cover)}</button>
          <div class="item-heading"><h3><button class="group-toggle" data-action="toggle-group" aria-expanded="${!collapsedGames.has(game.id)}" aria-controls="disks-${escape(game.id)}"><span aria-hidden="true">${collapsedGames.has(game.id) ? "▸" : "▾"}</span> ${escape(game.title)}</button></h3><p>${game.floppies.length} disquete(s)</p></div>
          <div class="row-actions"><button data-action="cover">Carátula…</button><button data-action="add-disk">Agregar disco…</button></div>
        </header>
        <table id="disks-${escape(game.id)}" class="floppy-table" ${collapsedGames.has(game.id) ? "hidden" : ""}><thead><tr><th scope="col">Nombre del disquete</th><th scope="col">Modo actual</th><th scope="col">Etiqueta NFC</th><th scope="col">Acciones</th></tr></thead><tbody>
          ${game.floppies.map((floppy) => `<tr data-floppy="${escape(floppy.id)}">
            <td>${escape(floppy.name)}</td>
            <td class="mode-cell"><span class="mode-indicator" role="img" tabindex="0" aria-label="${floppy.readOnly ? "Solo lectura" : "Lectura y escritura"}" title="${floppy.readOnly ? "Solo lectura" : "Lectura y escritura"}">${icons[floppy.readOnly ? "lock" : "unlock"]}<span class="mode-tooltip">${floppy.readOnly ? "Solo lectura" : "Lectura y escritura"}</span></span></td>
            <td class="nfc-cell">${floppy.tagged ? `<span class="badge" title="Tag ID de demostración">${escape(floppy.tagId)}</span>` : `<span class="badge untagged">Sin NFC</span>`}</td>
            <td><div class="row-actions">${iconButton("toggle-mode", floppy.readOnly ? "unlock" : "lock", `${floppy.readOnly ? "Permitir escritura en" : "Proteger contra escritura"} ${floppy.name}`)}${iconButton("nfc", "nfc", `Escribir NFC en ${floppy.name}`)}${iconButton("rename-floppy", "pencil", `Renombrar ${floppy.name}`)}${iconButton("delete-floppy", "trash", `Eliminar ${floppy.name}`)}</div></td>
          </tr>`).join("") || '<tr><td colspan="4" class="empty">Este juego no tiene disquetes. Usá “Agregar disco…”.</td></tr>'}
        </tbody></table>
      </article>`).join("") || `<p class="empty">${query ? "No hay juegos o disquetes que coincidan con la búsqueda." : "La biblioteca está vacía. Usá “Subir disquetes…” para empezar."}</p>`;
    updateStats();
  }

  function renderVms() {
    const query = $("#vm-search").value.trim().toLocaleLowerCase();
    $("#vm-list").innerHTML = state.vms.filter((vm) => vm.name.toLocaleLowerCase().includes(query)).map((vm) => `
      <article class="vm-group" data-vm="${escape(vm.id)}">
        <header class="item-header"><span class="app-mark" aria-hidden="true"></span><div class="item-heading"><h3>${escape(vm.name)}</h3></div><span class="badge ${vm.isRunning ? "running" : ""}">${vm.isRunning ? "En ejecución" : "Detenida"}</span></header>
        <div class="vm-details"><p><strong>Backups disponibles:</strong> ${vm.backups.length} copia(s)</p><div class="row-actions"><button data-action="edit-vm">Editar nombre / CFG…</button><button data-action="backups">Administrar backups…</button>${iconButton("delete-vm", "trash", `Eliminar VM ${vm.name}`)}</div></div>
      </article>`).join("") || `<p class="empty">${query ? "No hay máquinas que coincidan con la búsqueda." : "No hay máquinas virtuales. Usá “Crear VM…” para agregar una."}</p>`;
    updateStats();
  }

  function updateStats() {
    const vmView = location.hash === "#/vms";
    $("#stats").textContent = vmView ? `${state.vms.length} máquina(s) virtual(es)` : `${state.games.length} juego(s) · ${state.games.reduce((count, game) => count + game.floppies.length, 0)} disquete(s)`;
    const running = state.vms.find((vm) => vm.isRunning);
    $("#running-status").textContent = running ? `En ejecución: ${running.name}` : "Ninguna VM en ejecución";
  }

  function route() {
    const vmView = location.hash === "#/vms";
    $("#vms-view").hidden = !vmView;
    $("#floppies-view").hidden = vmView;
    $("#tab-vms").toggleAttribute("aria-current", vmView);
    $("#tab-floppies").toggleAttribute("aria-current", !vmView);
    $(vmView ? "#tab-vms" : "#tab-floppies").setAttribute("aria-current", "page");
    updateStats();
  }

  function openVm(edit) {
    const form = $("#vm-form");
    form.reset();
    if (!edit) activeVmId = null;
    const vm = edit ? currentVm() : null;
    field(form, "name").value = vm?.name || "";
    field(form, "cfg").value = vm?.cfg || "";
    $("#vm-title").textContent = edit ? "Editar máquina virtual" : "Crear máquina virtual";
    $("#vm-save").textContent = edit ? "Guardar" : "Crear";
    $("#vm-edit-hint").hidden = !edit;
    openDialog("#vm-dialog");
  }

  $("#vm-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    if (!validateTrimmed(form, ["name", "cfg"])) return;
    const name = field(form, "name").value.trim();
    const cfg = field(form, "cfg").value;
    if (activeVmId) Object.assign(currentVm(), { name, cfg });
    else state.vms.push({ id: nextId("vm"), name, cfg, isRunning: false, backups: [] });
    $("#vm-dialog").close();
    location.hash = "/vms";
    renderVms();
    notify("Máquina guardada en la demostración.");
  });

  $("#rename-floppy-form").addEventListener("submit", (event) => {
    event.preventDefault();
    if (!validateTrimmed(event.currentTarget, ["name"])) return;
    currentFloppy().name = field(event.currentTarget, "name").value.trim();
    $("#rename-floppy-dialog").close();
    $("#floppy-search").value = "";
    libraryPage = Math.floor(state.games.indexOf(currentGame()) / pageSize) + 1;
    renderLibrary();
    notify("Nombre del disquete actualizado.");
  });

  function renderBackups() {
    const vm = currentVm();
    $("#backups-title").textContent = `Backups — ${vm.name}`;
    $("#backup-list").innerHTML = vm.backups.map((backup) => `<tr data-backup="${escape(backup.id)}"><td>${escape(new Date(backup.date).toLocaleString("es-AR", { dateStyle: "short", timeStyle: "short" }))}</td><td>${escape(backup.size)}</td><td>${escape(backup.label)}</td><td><div class="row-actions">${iconButton("download-backup", "disk", `Descargar backup ${backup.label}`)}${iconButton("delete-backup", "trash", `Eliminar backup ${backup.label}`)}</div></td></tr>`).join("") || '<tr><td colspan="4" class="empty">Todavía no hay backups para esta máquina.</td></tr>';
  }

  $("#backup-form").addEventListener("submit", (event) => {
    event.preventDefault();
    if (!validateTrimmed(event.currentTarget, ["label"])) return;
    currentVm().backups.unshift({ id: nextId("backup"), date: new Date().toISOString(), size: "2.2 GB", label: field(event.currentTarget, "label").value.trim() });
    $("#backup-create-dialog").close();
    renderBackups();
    renderVms();
  });

  function openUpload(addToGame) {
    if (!addToGame) activeGameId = null;
    const form = $("#upload-form");
    form.reset();
    $("#upload-error").hidden = true;
    $("#upload-files").replaceChildren();
    $("#upload-title").textContent = addToGame ? `Agregar discos — ${currentGame().title}` : "Subir disquetes";
    $("#upload-name-label").hidden = addToGame;
    field(form, "title").required = !addToGame;
    openDialog("#upload-dialog");
  }

  field($("#upload-form"), "files").addEventListener("change", (event) => {
    $("#upload-files").innerHTML = Array.from(event.target.files).map((file) => `<li>${escape(file.name)} — ${(file.size / 1024).toFixed(0)} KB</li>`).join("");
  });

  $("#upload-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    const files = Array.from(field(form, "files").files);
    if (!activeGameId && !validateTrimmed(form, ["title"])) return;
    if (files.some((file) => !/\.(img|ima|dsk)$/i.test(file.name))) {
      $("#upload-error").textContent = "Seleccioná únicamente archivos .img, .ima o .dsk.";
      $("#upload-error").hidden = false;
      return;
    }
    let game = currentGame();
    if (!game) {
      const title = field(form, "title").value.trim();
      game = { id: nextId("game"), title, cover: { title, palette: "gray" }, floppies: [] };
      state.games.unshift(game);
    }
    collapsedGames.delete(game.id);
    libraryPage = activeGameId ? Math.floor(state.games.indexOf(game) / pageSize) + 1 : 1;
    game.floppies.push(...files.map((file) => ({ id: nextId("disk"), name: file.name, readOnly: false, tagged: false })));
    $("#upload-dialog").close();
    location.hash = "/floppies";
    $("#floppy-search").value = "";
    renderLibrary();
    notify(`${files.length} disquete(s) agregado(s) a la demostración.`);
  });

  function openCover() {
    coverDraft = { ...currentGame().cover };
    $("#cover-title").textContent = `Carátula — ${currentGame().title}`;
    $("#cover-query").value = currentGame().title;
    $("#cover-file").value = "";
    $("#cover-preview").innerHTML = coverMarkup(coverDraft);
    $("#cover-results").textContent = "Buscá un juego para ver las carátulas disponibles.";
    openDialog("#cover-dialog");
  }

  $("#cover-file").addEventListener("change", (event) => {
    const file = event.target.files[0];
    if (!file) return;
    if (!["image/png", "image/jpeg", "image/webp"].includes(file.type)) {
      $("#cover-results").textContent = "Usá una imagen PNG, JPEG o WebP.";
      return;
    }
    const url = URL.createObjectURL(file);
    objectUrls.add(url);
    const preview = new Image();
    preview.onload = () => {
      if (!$("#cover-dialog").open || event.target.files[0] !== file) return;
      coverDraft = { url };
      $("#cover-preview").innerHTML = coverMarkup(coverDraft);
      $("#cover-results").textContent = "Imagen local seleccionada. Aceptá para aplicar el cambio.";
    };
    preview.onerror = () => { $("#cover-results").textContent = "No se pudo leer la imagen. Seleccioná otro archivo."; };
    preview.src = url;
  });

  $("#cover-search-form").addEventListener("submit", (event) => {
    event.preventDefault();
    clearTimeout(searchTimer);
    const query = $("#cover-query").value.trim();
    if (!query) return;
    $("#cover-results").textContent = "Buscando carátulas…";
    searchTimer = setTimeout(() => {
      $("#cover-results").innerHTML = ["blue", "red", "gray"].map((palette) => `<button class="cover-option" data-action="pick-cover" data-palette="${palette}" data-title="${escape(query)}" aria-label="Seleccionar carátula ${palette}" aria-pressed="false">${coverMarkup({ title: query, palette })}</button>`).join("");
    }, 450);
  });
  $("#cover-dialog").addEventListener("close", () => clearTimeout(searchTimer));

  const priorityLabels = { sp: "España", wor: "Mundo", eu: "Europa", us: "Estados Unidos", es: "Español", en: "English" };
  function renderPriorities() {
    for (const [key, target] of [["regionPriority", "#region-priority"], ["languagePriority", "#language-priority"]]) {
      $(target).innerHTML = settingsDraft[key].map((value, index, values) => `<div class="priority-row"><span>${index + 1}. ${priorityLabels[value]}</span><button type="button" data-action="priority" data-key="${key}" data-index="${index}" data-step="-1" aria-label="Subir ${priorityLabels[value]}" ${index === 0 ? "disabled" : ""}>↑</button><button type="button" data-action="priority" data-key="${key}" data-index="${index}" data-step="1" aria-label="Bajar ${priorityLabels[value]}" ${index === values.length - 1 ? "disabled" : ""}>↓</button></div>`).join("");
    }
  }

  function openSettings() {
    settingsDraft = structuredClone(state.settings);
    const form = $("#settings-form");
    for (const key of ["language", "devId", "devPassword", "ssId", "ssPassword", "requestTimeoutSeconds", "maxDownloadMegabytes"]) field(form, key).value = state.settings[key];
    field(form, "startCollapsed").checked = state.settings.startCollapsed;
    $("#scraper-result").textContent = "";
    renderPriorities();
    openDialog("#settings-dialog");
  }

  $("#settings-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const form = event.currentTarget;
    for (const key of ["language", "devId", "devPassword", "ssId", "ssPassword"]) settingsDraft[key] = field(form, key).value;
    for (const key of ["requestTimeoutSeconds", "maxDownloadMegabytes"]) settingsDraft[key] = Number(field(form, key).value);
    settingsDraft.startCollapsed = field(form, "startCollapsed").checked;
    if (settingsDraft.startCollapsed !== state.settings.startCollapsed) {
      collapsedGames.clear();
      if (settingsDraft.startCollapsed) state.games.forEach((game) => collapsedGames.add(game.id));
    }
    state.settings = structuredClone(settingsDraft);
    renderLibrary();
    $("#settings-dialog").close();
    notify("Configuración guardada en la demostración. Los textos siguen en español.");
  });

  function validateTrimmed(form, names) {
    for (const name of names) {
      const input = field(form, name);
      input.setCustomValidity(input.value.trim() ? "" : "Completá este campo.");
    }
    return form.reportValidity();
  }
  document.addEventListener("input", (event) => event.target.setCustomValidity?.(""));

  document.addEventListener("click", (event) => {
    const close = event.target.closest("[data-close]");
    if (close) { close.closest("dialog").close(); return; }
    const button = event.target.closest("[data-action]");
    if (!button) return;
    const gameId = button.closest("[data-game]")?.dataset.game;
    const vmId = button.closest("[data-vm]")?.dataset.vm;
    const floppyId = button.closest("[data-floppy]")?.dataset.floppy;
    const backupId = button.closest("[data-backup]")?.dataset.backup;
    if (gameId) activeGameId = gameId;
    if (vmId) activeVmId = vmId;
    if (floppyId) activeFloppyId = floppyId;
    switch (button.dataset.action) {
      case "toggle-group":
        if (collapsedGames.has(activeGameId)) collapsedGames.delete(activeGameId);
        else collapsedGames.add(activeGameId);
        renderLibrary();
        document.querySelector(`[data-game="${activeGameId}"] .group-toggle`).focus();
        break;
      case "expand-all": collapsedGames.clear(); renderLibrary(); break;
      case "collapse-all": state.games.forEach((game) => collapsedGames.add(game.id)); renderLibrary(); break;
      case "previous-page": libraryPage--; renderLibrary(); $("#library").scrollTop = 0; break;
      case "next-page": libraryPage++; renderLibrary(); $("#library").scrollTop = 0; break;
      case "upload": openUpload(false); break;
      case "add-disk": openUpload(true); break;
      case "new-vm": openVm(false); break;
      case "edit-vm": openVm(true); break;
      case "settings": openSettings(); break;
      case "about": openDialog("#about-dialog"); break;
      case "backups": renderBackups(); openDialog("#backups-dialog"); break;
      case "new-backup": $("#backup-form").reset(); openDialog("#backup-create-dialog"); break;
      case "delete-backup": {
        const vm = currentVm();
        const backup = vm.backups.find((item) => item.id === backupId);
        confirmDelete(`¿Eliminar el backup “${backup.label}” de ${vm.name}?`, () => {
          vm.backups = vm.backups.filter((item) => item.id !== backupId);
          renderBackups(); renderVms();
        });
        break;
      }
      case "download-backup": {
        const backup = currentVm().backups.find((item) => item.id === backupId);
        const url = URL.createObjectURL(new Blob([`RetroBox — DEMOSTRACIÓN\nVM: ${currentVm().name}\nBackup: ${backup.label}\nEste archivo es un comprobante de prueba. No contiene una copia de la VM.\n`], { type: "text/plain;charset=utf-8" }));
        const link = document.createElement("a");
        link.href = url; link.download = `${backup.id}-DEMO.txt`; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
        break;
      }
      case "delete-vm": {
        const vm = currentVm();
        confirmDelete(`¿Eliminar “${vm.name}” y sus ${vm.backups.length} backups de demostración?`, () => {
          state.vms = state.vms.filter((item) => item.id !== vm.id); renderVms();
        });
        break;
      }
      case "rename-floppy":
        field($("#rename-floppy-form"), "name").value = currentFloppy().name;
        openDialog("#rename-floppy-dialog"); break;
      case "toggle-mode":
        currentFloppy().readOnly = !currentFloppy().readOnly;
        renderLibrary();
        document.querySelector(`[data-floppy="${activeFloppyId}"] [data-action="toggle-mode"]`).focus();
        notify(`Modo actual: ${currentFloppy().readOnly ? "solo lectura" : "lectura y escritura"}.`);
        break;
      case "delete-floppy": {
        const game = currentGame();
        const floppy = currentFloppy();
        confirmDelete(`¿Eliminar “${floppy.name}” de “${game.title}”?`, () => {
          game.floppies = game.floppies.filter((item) => item.id !== floppy.id); renderLibrary();
        });
        break;
      }
      case "nfc":
        $("#nfc-file").textContent = `${currentGame().title} — ${currentFloppy().name}`;
        $("#nfc-state").textContent = "Esperando un disquete…";
        $("#nfc-write").disabled = true;
        openDialog("#nfc-dialog"); break;
      case "detect-nfc": $("#nfc-state").textContent = "Disquete detectado (simulación). Listo para escribir."; $("#nfc-write").disabled = false; break;
      case "write-nfc": currentFloppy().tagged = true; currentFloppy().tagId ||= `04:A1:00:00:00:${(++serial).toString(16).padStart(4, "0").match(/../g).join(":").toUpperCase()}`; $("#nfc-dialog").close(); renderLibrary(); notify("Etiqueta NFC asignada en la demostración."); break;
      case "cover": openCover(); break;
      case "pick-cover":
        coverDraft = { title: button.dataset.title, palette: button.dataset.palette };
        $("#cover-preview").innerHTML = coverMarkup(coverDraft);
        document.querySelectorAll(".cover-option").forEach((option) => option.setAttribute("aria-pressed", String(option === button)));
        break;
      case "save-cover": currentGame().cover = { ...coverDraft }; $("#cover-dialog").close(); renderLibrary(); notify("Carátula actualizada."); break;
      case "priority": {
        const { key, index, step } = button.dataset;
        const list = settingsDraft[key];
        const target = Number(index) + Number(step);
        [list[index], list[target]] = [list[target], list[index]];
        renderPriorities();
        document.querySelector(`[data-key="${key}"][data-index="${target}"][data-step="${step}"]:not(:disabled)`)?.focus();
        break;
      }
      case "test-scraper": {
        const form = $("#settings-form");
        const filled = ["devId", "devPassword", "ssId", "ssPassword"].every((key) => field(form, key).value.trim());
        $("#scraper-result").textContent = filled ? "Prueba simulada correcta. No se verificaron las credenciales en el servicio." : "Completá las cuatro credenciales con valores de prueba.";
        break;
      }
    }
  });

  $("#confirm-accept").addEventListener("click", () => {
    confirmAction?.();
    $("#confirm-dialog").close();
  });
  $("#confirm-dialog").addEventListener("close", () => { confirmAction = null; });
  document.addEventListener("click", (event) => {
    if (!event.target.closest("details")) document.querySelectorAll("details[open]").forEach((menu) => { menu.open = false; });
  });
  $("#floppy-search").addEventListener("input", () => { libraryPage = 1; renderLibrary(); $("#library").scrollTop = 0; });
  $("#vm-search").addEventListener("input", renderVms);
  window.addEventListener("hashchange", route);
  window.addEventListener("pagehide", () => objectUrls.forEach((url) => URL.revokeObjectURL(url)));
  renderLibrary();
  renderVms();
  route();
})();
