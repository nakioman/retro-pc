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
    readOnly: "Solo lectura",
    readWrite: "Lectura y escritura",
    deleteAction: "Borrar",
    confirmDelete: "Borrar este disquete del catalogo?",
    uploading: "Subiendo...",
    uploaded: "Listo",
    stats: "{count} disquetes, {tagged} grabados",
    loadFailed: "No se pudo leer el catalogo.",
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
    unexpected: "Error inesperado."
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
    readOnly: "Read-only",
    readWrite: "Read-write",
    deleteAction: "Delete",
    confirmDelete: "Delete this floppy from the catalog?",
    uploading: "Uploading...",
    uploaded: "Done",
    stats: "{count} floppies, {tagged} tagged",
    loadFailed: "Could not read the catalog.",
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
    unexpected: "Unexpected error."
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

async function readError(response) {
  try {
    const body = await response.json();
    return t(body.code) !== body.code ? t(body.code) : body.message || t("unexpected");
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
  const response = await fetch("/api/floppies/" + encodeURIComponent(id), {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(patch)
  });

  if (!response.ok) {
    window.alert(await readError(response));
  }

  button.disabled = false;
  await loadCatalog();
}

async function deleteFloppy(id, button) {
  if (!window.confirm(t("confirmDelete"))) {
    return;
  }

  button.disabled = true;
  const response = await fetch("/api/floppies/" + encodeURIComponent(id), { method: "DELETE" });
  if (!response.ok) {
    window.alert(await readError(response));
  }

  button.disabled = false;
  await loadCatalog();
}

async function uploadFiles(files) {
  const status = document.getElementById("upload-status");

  for (const file of files) {
    status.textContent = t("uploading") + " " + file.name;
    const body = new FormData();
    body.append("file", file, file.name);

    const response = await fetch("/api/floppies", { method: "POST", body });
    if (!response.ok) {
      status.textContent = file.name + ": " + (await readError(response));
      await loadCatalog();
      return;
    }
  }

  status.textContent = t("uploaded");
  await loadCatalog();
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

document.getElementById("language").value = language;
applyStaticText();
loadCatalog();
