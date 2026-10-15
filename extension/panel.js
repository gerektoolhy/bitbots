const DEFAULT_PATTERNS = "fbcdn.net, cdninstagram.com, fbcdn.com";

const filterInput = document.getElementById("filter");
const imagesOnly = document.getElementById("images-only");
const stripMatching = document.getElementById("strip-matching");
const clearButton = document.getElementById("clear");
const countEl = document.getElementById("count");
const listEl = document.getElementById("list");
const detailEl = document.getElementById("detail");
const patternsInput = document.getElementById("patterns");

/** @type {Array<{id:number,url:string,method:string,status:number,mime:string,type:string,failed:boolean}>} */
const requests = [];
let selectedId = null;
let nextId = 1;

chrome.storage.local.get({ patterns: DEFAULT_PATTERNS }, (stored) => {
  patternsInput.value = stored.patterns || DEFAULT_PATTERNS;
});

patternsInput.addEventListener("change", () => {
  chrome.storage.local.set({ patterns: patternsInput.value });
  render();
  if (selectedId != null) showDetail(selectedId);
});

filterInput.addEventListener("input", render);
imagesOnly.addEventListener("change", render);
stripMatching.addEventListener("change", () => {
  render();
  if (selectedId != null) showDetail(selectedId);
});
clearButton.addEventListener("click", () => {
  requests.length = 0;
  selectedId = null;
  render();
  showEmpty();
});

function patterns() {
  return patternsInput.value
    .split(",")
    .map((part) => part.trim().toLowerCase())
    .filter(Boolean);
}

function hostMatches(url) {
  let host = "";
  try {
    host = new URL(url).hostname.toLowerCase();
  } catch {
    return false;
  }
  return patterns().some((pattern) => host === pattern || host.endsWith("." + pattern));
}

function stripQuery(url) {
  try {
    const parsed = new URL(url);
    parsed.search = "";
    parsed.hash = "";
    return parsed.toString();
  } catch {
    const q = url.indexOf("?");
    return q === -1 ? url : url.slice(0, q);
  }
}

function shouldStrip(url) {
  if (!stripMatching.checked) return false;
  return hostMatches(url);
}

function displayUrl(url) {
  return shouldStrip(url) ? stripQuery(url) : url;
}

function visibleRequests() {
  const query = filterInput.value.trim().toLowerCase();
  return requests.filter((request) => {
    if (imagesOnly.checked && !isImage(request)) return false;
    if (!query) return true;
    return (
      request.url.toLowerCase().includes(query) ||
      request.type.toLowerCase().includes(query) ||
      request.mime.toLowerCase().includes(query)
    );
  });
}

function isImage(request) {
  if (request.type === "image" || request.type === "images") return true;
  if ((request.mime || "").startsWith("image/")) return true;
  return /\.(webp|png|jpe?g|gif|avif|svg)(\?|$)/i.test(request.url);
}

function fileName(url) {
  try {
    const path = new URL(url).pathname;
    const name = path.split("/").filter(Boolean).pop();
    return name || url;
  } catch {
    return url;
  }
}

function render() {
  const shown = visibleRequests();
  countEl.textContent = `${shown.length} shown / ${requests.length} captured`;
  listEl.replaceChildren();
  for (const request of shown) {
    const item = document.createElement("li");
    if (request.id === selectedId) item.classList.add("selected");
    const status = document.createElement("span");
    status.className = "status" + (request.failed || request.status >= 400 ? " bad" : "");
    status.textContent = request.failed ? "ERR" : String(request.status || "");
    const name = document.createElement("div");
    name.className = "name";
    name.textContent = fileName(request.url);
    const meta = document.createElement("div");
    meta.className = "meta";
    const stripped = shouldStrip(request.url);
    meta.textContent = `${request.method} ${request.mime || request.type}${stripped ? " · query stripped" : ""}`;
    item.append(status, name, meta);
    item.addEventListener("click", () => {
      selectedId = request.id;
      render();
      showDetail(request.id);
    });
    listEl.append(item);
  }
}

function showEmpty() {
  detailEl.className = "detail empty";
  detailEl.replaceChildren();
  const note = document.createElement("p");
  note.className = "placeholder";
  note.textContent = "Select a request to preview it without its query string.";
  detailEl.append(note);
}

function showDetail(id) {
  const request = requests.find((entry) => entry.id === id);
  if (!request) {
    showEmpty();
    return;
  }
  const stripped = shouldStrip(request.url);
  const previewUrl = stripped ? stripQuery(request.url) : request.url;
  detailEl.className = "detail";
  detailEl.replaceChildren();

  detailEl.append(urlBlock("Original", request.url));
  if (stripped && previewUrl !== request.url) {
    detailEl.append(urlBlock("Preview URL", previewUrl));
  }

  const actions = document.createElement("div");
  actions.className = "actions";
  const copy = document.createElement("button");
  copy.type = "button";
  copy.textContent = "Copy preview URL";
  copy.addEventListener("click", () => navigator.clipboard.writeText(previewUrl));
  const open = document.createElement("button");
  open.type = "button";
  open.textContent = "Open";
  open.addEventListener("click", () => window.open(previewUrl, "_blank", "noopener"));
  actions.append(copy, open);
  detailEl.append(actions);

  if (isImage(request)) {
    const frame = document.createElement("div");
    frame.className = "preview-frame";
    const img = document.createElement("img");
    img.alt = fileName(request.url);
    img.src = previewUrl;
    const fail = document.createElement("p");
    fail.className = "note";
    fail.hidden = true;
    fail.textContent = "This URL did not load after the query string was removed. The signature in the query may be required.";
    img.addEventListener("error", () => {
      fail.hidden = false;
    });
    frame.append(img);
    detailEl.append(frame, fail);
  } else {
    const note = document.createElement("p");
    note.className = "placeholder";
    note.textContent = "Preview is shown for image requests. The URL above is still available to copy.";
    detailEl.append(note);
  }
}

function urlBlock(label, value) {
  const block = document.createElement("div");
  block.className = "url";
  const title = document.createElement("span");
  title.className = "label";
  title.textContent = label;
  block.append(title, document.createTextNode(value));
  return block;
}

function addRequest(harEntry) {
  const request = harEntry.request || {};
  const response = harEntry.response || {};
  const url = request.url || "";
  if (!url || url.startsWith("data:") || url.startsWith("chrome-extension:")) return;
  requests.push({
    id: nextId++,
    url,
    method: request.method || "GET",
    status: response.status || 0,
    mime: response.content?.mimeType || "",
    type: (harEntry._resourceType || "").toLowerCase(),
    failed: !response.status,
  });
  if (requests.length > 2000) requests.splice(0, requests.length - 2000);
  render();
}

chrome.devtools.network.onRequestFinished.addListener(addRequest);
chrome.devtools.network.getHAR((harLog) => {
  for (const entry of harLog.entries || []) addRequest(entry);
});

showEmpty();
render();
