"use strict";

const STRINGS = {
  es: {
    subtitle: "Biblioteca de disquetes",
    searchPlaceholder: "Buscar disquete...",
    uploadTitle: "Subir disquetes",
    uploadHint: "Imagenes .img, .ima o .dsk",
    uploadButton: "Seleccionar archivos",
    libraryTitle: "Biblioteca",
    empty: "Todavia no hay disquetes en el catalogo.",
    catalogBroken: "El catalogo tiene un error y no se pudo cargar: {message}",
    tagged: "Grabado",
    untagged: "Sin NFC",
    untaggedHelp: "Este disquete no se puede insertar hasta que se grabe una etiqueta NFC para el. Ponelo en la disquetera y asignale un tag desde la seccion de arriba.",
    readOnly: "Solo lectura",
    readWrite: "Lectura y escritura",
    deleteAction: "Borrar",
    confirmDelete: "Borrar este disquete del catalogo?",
    uploading: "Subiendo...",
    uploaded: "Listo",
    stats: "{count} disquetes, {tagged} grabados",
    loadFailed: "No se pudo leer el catalogo.",
    networkError: "No se pudo conectar con RetroBox. Revisa la conexion e intenta de nuevo.",
    "unsupported-extension": "Solo se aceptan imagenes .img, .ima y .dsk.",
    "file-too-large": "La imagen supera el limite de subida.",
    "unusable-name": "Ese nombre de archivo no da un ID valido.",
    "missing-file": "No se selecciono ningun archivo.",
    "expected-multipart": "La subida no llego como formulario.",
    "scratch-name-taken": "Ya hay un archivo con ese nombre esperando a ser importado.",
    "import-failed": "No se pudo importar la imagen.",
    "catalog-unavailable": "No se pudo leer el catalogo en disco; la operacion se rechazo.",
    "unknown-floppy": "Ese disquete no existe.",
    "invalid-patch": "El cambio no es valido.",
    "delete-incomplete": "Se quito del catalogo, pero el archivo quedo en disco.",
    unexpected: "Error inesperado.",
    driveTitle: "Disquetera",
    driveUnavailable: "Sin controlador conectado",
    driveEmpty: "No hay disco en la disquetera",
    driveLoaded: "Disco puesto: {label}",
    driveBlankTag: "Tag en blanco, listo para asignar ({uid})",
    assignButton: "Grabar tag",
    assignReassign: "Reasignar este tag",
    assignDone: "Tag grabado",
    assignPlaceholder: "Elegi un disquete...",
    assignNoSelection: "Elegi un disquete de la lista antes de grabar el tag.",
    confirmReassign: "Ese tag ya es de \"{owner}\". Reasignarlo a este disquete?",
    "no-tag-present": "No hay ningun disco en la disquetera.",
    "tag-already-assigned": "Ese tag ya esta asignado a otro disquete.",
    "write-failed": "El controlador no pudo grabar el tag.",
    "write-unconfirmed": "No se pudo confirmar si el tag quedo grabado. Fijate que dice la disquetera.",
    "no-controller": "No hay controlador de disquetes conectado.",
    "mode-changed": "El modo del disquete cambio mientras se grababa el tag.",
    "invalid-request": "La solicitud no es valida."
  },
  en: {
    subtitle: "Floppy library",
    searchPlaceholder: "Search floppies...",
    uploadTitle: "Upload floppies",
    uploadHint: ".img, .ima or .dsk images",
    uploadButton: "Choose files",
    libraryTitle: "Library",
    empty: "No floppies in the catalog yet.",
    catalogBroken: "The catalog has an error and could not be loaded: {message}",
    tagged: "Tagged",
    untagged: "No NFC",
    untaggedHelp: "This floppy cannot be inserted until an NFC tag is written for it. Put it in the drive and assign it a tag from the section above.",
    readOnly: "Read-only",
    readWrite: "Read-write",
    deleteAction: "Delete",
    confirmDelete: "Delete this floppy from the catalog?",
    uploading: "Uploading...",
    uploaded: "Done",
    stats: "{count} floppies, {tagged} tagged",
    loadFailed: "Could not read the catalog.",
    networkError: "Could not reach RetroBox. Check the connection and try again.",
    "unsupported-extension": "Only .img, .ima and .dsk images are accepted.",
    "file-too-large": "The image exceeds the upload limit.",
    "unusable-name": "That filename yields no valid ID.",
    "missing-file": "No file was selected.",
    "expected-multipart": "The upload did not arrive as a form.",
    "scratch-name-taken": "A file with that name is already staged for import.",
    "import-failed": "The image could not be imported.",
    "catalog-unavailable": "The catalog on disk could not be read; the operation was refused.",
    "unknown-floppy": "That floppy does not exist.",
    "invalid-patch": "That change is not valid.",
    "delete-incomplete": "Removed from the catalog, but the file is still on disk.",
    unexpected: "Unexpected error.",
    driveTitle: "Drive",
    driveUnavailable: "No controller connected",
    driveEmpty: "No disk in the drive",
    driveLoaded: "Disk in the drive: {label}",
    driveBlankTag: "Blank tag, ready to assign ({uid})",
    assignButton: "Write tag",
    assignReassign: "Reassign this tag",
    assignDone: "Tag written",
    assignPlaceholder: "Choose a floppy...",
    assignNoSelection: "Choose a floppy from the list before writing the tag.",
    confirmReassign: "That tag already belongs to \"{owner}\". Reassign it to this floppy?",
    "no-tag-present": "There is no disk in the drive.",
    "tag-already-assigned": "That tag is already assigned to another floppy.",
    "write-failed": "The controller could not write the tag.",
    "write-unconfirmed": "Could not confirm whether the tag was written. Check the drive.",
    "no-controller": "No floppy controller is connected.",
    "mode-changed": "The floppy's mode changed while the tag was being written.",
    "invalid-request": "That request is not valid."
  }
};

let language = pickLanguage();
let floppies = [];

function pickLanguage() {
  const stored = window.localStorage.getItem("retrobox.lang");
  if (stored && STRINGS[stored]) {
    return stored;
  }

  return (navigator.language || "es").toLowerCase().startsWith("en") ? "en" : "es";
}

function t(key, replacements) {
  let text = STRINGS[language][key] || STRINGS.es[key] || key;
  if (replacements) {
    for (const name of Object.keys(replacements)) {
      text = text.replace("{" + name + "}", replacements[name]);
    }
  }

  return text;
}

function applyStaticText() {
  document.documentElement.lang = language;
  document.querySelectorAll("[data-i18n]").forEach((node) => {
    node.textContent = t(node.dataset.i18n);
  });
  document.querySelectorAll("[data-i18n-placeholder]").forEach((node) => {
    node.placeholder = t(node.dataset.i18nPlaceholder);
  });
}

function describeError(body) {
  return t(body.code) !== body.code ? t(body.code) : body.message || t("unexpected");
}

async function readError(response) {
  try {
    return describeError(await response.json());
  } catch (error) {
    return t("unexpected");
  }
}

async function loadCatalog() {
  const problem = document.getElementById("library-error");
  try {
    const response = await fetch("/api/catalog");
    if (!response.ok) {
      throw new Error("catalog");
    }

    const payload = await response.json();
    floppies = payload.floppies;
    problem.hidden = true;

    const broken = document.getElementById("catalog-error");
    if (payload.catalogError) {
      broken.textContent = t("catalogBroken", { message: payload.catalogError });
      broken.hidden = false;
    } else {
      broken.hidden = true;
    }
  } catch (error) {
    problem.textContent = t("loadFailed");
    problem.hidden = false;
    floppies = [];
  }

  render();
}

function render() {
  const list = document.getElementById("library");
  const term = document.getElementById("search").value.trim().toLowerCase();
  const visible = floppies.filter(
    (floppy) => floppy.id.toLowerCase().includes(term) || floppy.label.toLowerCase().includes(term)
  );

  list.textContent = "";
  document.getElementById("empty").hidden = floppies.length > 0;
  document.getElementById("stats").textContent = t("stats", {
    count: floppies.length,
    tagged: floppies.filter((floppy) => floppy.nfc).length
  });

  for (const floppy of visible) {
    list.appendChild(renderRow(floppy));
  }

  renderDrive();
}

function renderRow(floppy) {
  const row = document.createElement("li");

  const name = document.createElement("div");
  name.className = "floppy-name";
  const label = document.createElement("strong");
  label.textContent = floppy.label;
  const meta = document.createElement("span");
  meta.textContent = floppy.id + " - " + floppy.size;
  name.append(label, meta);

  const actions = document.createElement("div");
  actions.className = "actions";

  const badge = document.createElement("span");
  badge.className = "badge " + (floppy.nfc ? "tagged" : "untagged");
  badge.textContent = floppy.nfc ? t("tagged") : t("untagged");
  // An uploaded floppy is listed but inert until a tag is written for it. Without this the
  // badge is an unexplained amber label with no hint that the drive section above can fix it.
  if (!floppy.nfc) {
    badge.title = t("untaggedHelp");
  }

  const mode = document.createElement("button");
  mode.textContent = floppy.mode === "rw" ? t("readWrite") : t("readOnly");
  mode.addEventListener("click", () => patchFloppy(floppy.id, { mode: floppy.mode === "rw" ? "ro" : "rw" }, mode));

  const remove = document.createElement("button");
  remove.className = "danger";
  remove.textContent = t("deleteAction");
  remove.addEventListener("click", () => deleteFloppy(floppy.id, remove));

  actions.append(badge, mode, remove);
  row.append(name, actions);
  return row;
}

async function patchFloppy(id, patch, button) {
  button.disabled = true;
  try {
    const response = await fetch("/api/floppies/" + encodeURIComponent(id), {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(patch)
    });

    if (!response.ok) {
      window.alert(await readError(response));
    }
  } catch (error) {
    window.alert(t("networkError"));
  } finally {
    button.disabled = false;
  }

  await loadCatalog();
}

async function deleteFloppy(id, button) {
  if (!window.confirm(t("confirmDelete"))) {
    return;
  }

  button.disabled = true;
  try {
    const response = await fetch("/api/floppies/" + encodeURIComponent(id), { method: "DELETE" });
    if (!response.ok) {
      window.alert(await readError(response));
    }
  } catch (error) {
    window.alert(t("networkError"));
  } finally {
    button.disabled = false;
  }

  await loadCatalog();
}

async function uploadFiles(files) {
  const status = document.getElementById("upload-status");

  for (const file of files) {
    status.textContent = t("uploading") + " " + file.name;
    const body = new FormData();
    body.append("file", file, file.name);

    let failure = null;
    try {
      const response = await fetch("/api/floppies", { method: "POST", body });
      if (!response.ok) {
        failure = await readError(response);
      }
    } catch (error) {
      failure = t("networkError");
    }

    if (failure !== null) {
      status.textContent = file.name + ": " + failure;
      await loadCatalog();
      return;
    }
  }

  status.textContent = t("uploaded");
  await loadCatalog();
}

let drive = { state: "unavailable", floppyId: null, mode: null, tagUid: null };

// A transient status line (e.g. "Tag written") that renderDrive prefers over its normal detail
// text. render() re-invokes renderDrive on every catalog reload, which would otherwise stomp the
// message before anyone reads it; this survives that and is cleared only by an actual drive-state
// change, not by a reload.
let driveNotice = null;

function renderDrive() {
  const section = document.getElementById("drive");
  const state = document.getElementById("drive-state");
  const detail = document.getElementById("drive-detail");
  const assign = document.getElementById("assign");
  const target = document.getElementById("assign-target");

  // The section stays visible even with no controller: an explanation beats a card that just
  // vanishes. Only the assign control (which needs a controller to do anything) is hidden.
  section.hidden = false;

  if (drive.state === "unavailable") {
    state.textContent = t("driveUnavailable");
    detail.textContent = "";
    assign.hidden = true;
    return;
  }

  if (drive.state === "loaded") {
    const known = floppies.find((floppy) => floppy.id === drive.floppyId);
    state.textContent = t("driveLoaded", { label: known ? known.label : drive.floppyId });
    detail.textContent = driveNotice || t("assignReassign");
  } else if (drive.state === "blankTag") {
    state.textContent = t("driveBlankTag", { uid: drive.tagUid });
    detail.textContent = driveNotice || "";
  } else {
    state.textContent = t("driveEmpty");
    detail.textContent = driveNotice || "";
  }

  assign.hidden = drive.state === "empty";
  if (assign.hidden) {
    return;
  }

  const selected = target.value;
  target.textContent = "";

  // A disabled placeholder is the initial selection so writing a tag always takes a deliberate
  // choice — nothing here proves a blankTag reading is actually free (Ruling 27), and unlike the
  // reassignment case, a blank tag draws no 409 from the server to catch an accidental default.
  const placeholder = document.createElement("option");
  placeholder.value = "";
  placeholder.disabled = true;
  placeholder.textContent = t("assignPlaceholder");
  target.appendChild(placeholder);

  for (const floppy of floppies) {
    const option = document.createElement("option");
    option.value = floppy.id;
    option.textContent = floppy.label;
    target.appendChild(option);
  }

  target.value = floppies.some((floppy) => floppy.id === selected) ? selected : "";
}

function subscribeToDrive() {
  const source = new EventSource("/api/drive/events");

  source.addEventListener("message", (event) => {
    drive = JSON.parse(event.data);
    driveNotice = null;
    renderDrive();
  });

  // EventSource reconnects on its own; a failure only means the panel is momentarily blind.
  source.addEventListener("error", () => {
    drive = { state: "unavailable", floppyId: null, mode: null, tagUid: null };
    driveNotice = null;
    renderDrive();
  });
}

async function writeTag(confirm) {
  const button = document.getElementById("assign-write");
  const floppyId = document.getElementById("assign-target").value;

  if (!floppyId) {
    // The picker's placeholder has no floppy behind it, and a floppy removed from the catalog
    // between renders resolves to this same empty value (see renderDrive) — either way the click
    // must say something rather than silently doing nothing.
    window.alert(t("assignNoSelection"));
    return;
  }

  button.disabled = true;

  try {
    const response = await fetch("/api/nfc/write", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ floppyId: floppyId, confirm: confirm })
    });

    if (response.ok) {
      driveNotice = t("assignDone");
      await loadCatalog();
      return;
    }

    const body = await response.json().catch(() => null);

    // A blankTag reading is not proof the tag is free (see Ruling 27: right after a controller
    // reconnect the tracker can briefly forget an already-cataloged disk is seated), so the
    // first attempt always goes unconfirmed and this 409 is the real gate, not a shortcut.
    if (body && body.code === "tag-already-assigned" && !confirm) {
      const owner = floppies.find((floppy) => floppy.id === body.previousFloppyId);
      if (window.confirm(t("confirmReassign", { owner: owner ? owner.label : body.previousFloppyId }))) {
        button.disabled = false;
        await writeTag(true);
        return;
      }

      return;
    }

    // body was already consumed above (a Response body can only be read once), so the message
    // is derived from it directly rather than through readError, which re-reads the response.
    window.alert(body ? describeError(body) : t("unexpected"));
  } catch (error) {
    window.alert(t("networkError"));
  } finally {
    button.disabled = false;
  }
}

document.getElementById("pick").addEventListener("click", () => document.getElementById("file").click());
document.getElementById("file").addEventListener("change", (event) => {
  const files = Array.from(event.target.files);
  event.target.value = "";
  uploadFiles(files);
});
document.getElementById("search").addEventListener("input", render);
document.getElementById("language").addEventListener("change", (event) => {
  language = event.target.value;
  window.localStorage.setItem("retrobox.lang", language);
  applyStaticText();
  render();
});
// Wired last: if #assign-write were ever missing from the markup, a TypeError here must not
// take the listeners above (upload, search, language) down with it.
document.getElementById("assign-write").addEventListener("click", () => writeTag(false));

document.getElementById("language").value = language;
applyStaticText();
loadCatalog();
subscribeToDrive();
