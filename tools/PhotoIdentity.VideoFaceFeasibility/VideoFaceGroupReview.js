/* WI-0155 isolated local group-review worksheet. No network, storage,
 * automatic identification, group persistence or background saving. */
(() => {
  "use strict";
  const columns = [
    "Clip", "Group", "Observations", "Decision", "MergeInto",
    "SelectedPerson", "ReviewerNotes"
  ];
  const cards = [...document.querySelectorAll(".group-card")];
  const saveButton = document.getElementById("save-groups");
  const importButton = document.getElementById("import-groups");
  const fileInput = document.getElementById("groups-file");
  const status = document.getElementById("group-status");
  const valid = new Set(["", "Keep group", "Merge group",
    "Dismiss false detection", "Split required", "Cannot tell"]);
  let dirty = false;
  let fileHandle = null;
  const fields = (card) => ({
    Clip: card.dataset.clip,
    Group: card.dataset.group,
    Observations: card.dataset.observations,
    Decision: card.querySelector(".decision").value,
    MergeInto: card.querySelector(".merge-target").value.trim(),
    SelectedPerson: card.querySelector(".person").value.trim(),
    ReviewerNotes: card.querySelector(".notes").value
  });
  const review = () => {
    const complete = cards.filter(card => fields(card).Decision !== "").length;
    status.textContent = complete + " / " + cards.length + " provisional groups reviewed" +
      (dirty ? " · UNSAVED edits" : " · save to preserve decisions");
  };
  for (const card of cards) {
    for (const input of card.querySelectorAll("input, textarea, select")) {
      input.addEventListener("input", () => {dirty = true; review();});
    }
  }

  const escape = v => '"' + String(v ?? "").replaceAll('"', '""') + '"';
  const contents = () => "\uFEFF" + [
    columns.map(escape).join(","),
    ...cards.map(card => {
      const row = fields(card);
      return columns.map(col => escape(row[col])).join(",");
    })
  ].join("\r\n") + "\r\n";
  const filename = () => "wi0155-private-group-review-" +
    new Date().toISOString().replaceAll(":", "-").replaceAll(".", "-") + ".csv";
  const download = text => {
    const url = URL.createObjectURL(new Blob([text], {
      type: "text/csv;charset=utf-8"
    }));
    const a = document.createElement("a");
    a.href = url; a.download = filename();
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
  };
  saveButton.addEventListener("click", async () => {
    try {
      if ("showSaveFilePicker" in window) {
        if (!fileHandle) {
          fileHandle = await window.showSaveFilePicker({
            suggestedName: filename(),
            types: [{description: "Private group decisions",
              accept: {"text/csv": [".csv"]}}]
          });
        }
        const out = await fileHandle.createWritable();
        await out.write(contents());
        await out.close();
      } else {
        download(contents());
      }
      dirty = false;
      review();
    } catch {
      status.textContent = "Save cancelled or blocked. Edits remain in this page.";
    }
  });

  function parseCsv(source) {
    source = source.replace(/^\uFEFF/, "");
    const rows = [];
    let row = [], field = "", quoted = false;
    for (let i = 0; i < source.length; i++) {
      const c = source[i];
      if (quoted) {
        if (c === '"' && source[i + 1] === '"') { field += '"'; i++; }
        else if (c === '"') { quoted = false; }
        else { field += c; }
      } else if (c === '"' && field === "") { quoted = true; }
      else if (c === ",") { row.push(field); field = ""; }
      else if (c === "\n") {
        row.push(field.replace(/\r$/, ""));
        if (row.some(x => x !== "")) rows.push(row);
        row = []; field = "";
      } else { field += c; }
    }
    if (quoted) throw new Error("Malformed CSV");
    if (field !== "" || row.length) {
      row.push(field.replace(/\r$/, "")); rows.push(row);
    }
    return rows;
  }
  importButton.addEventListener("click", () => fileInput.click());
  fileInput.addEventListener("change", async () => {
    const file = fileInput.files?.[0]; if (!file) return;
    try {
      if (file.size > 256 * 1024) throw new Error("Oversized review");
      const rows = parseCsv(await file.text());
      if (rows.length !== cards.length + 1 ||
          columns.some((name, i) => rows[0][i] !== name)) {
        throw new Error("Group worksheet does not match this gallery");
      }
      const indexed = new Map();
      const allowedGroups = new Set(cards.map(c => c.dataset.clip + ":" + c.dataset.group));
      for (const values of rows.slice(1)) {
        if (values.length !== columns.length) throw new Error("Column mismatch");
        const item = Object.fromEntries(columns.map((name, i) => [name, values[i]]));
        const id = item.Clip + ":" + item.Group;
        if (!allowedGroups.has(id) || indexed.has(id) || !valid.has(item.Decision)) {
          throw new Error("Invalid group review");
        }
        if (item.MergeInto && (!allowedGroups.has(item.Clip + ":" + item.MergeInto) ||
             item.MergeInto === item.Group)) throw new Error("Invalid merge target");
        if (item.Decision === "Merge group" && !item.MergeInto) {
          throw new Error("Missing merge target");
        }
        indexed.set(id, item);
      }
      if (indexed.size !== cards.length) throw new Error("Missing groups");
      for (const card of cards) {
        const item = indexed.get(card.dataset.clip + ":" + card.dataset.group);
        if (item.Observations !== card.dataset.observations) {
          throw new Error("Changed group evidence");
        }
      }
      for (const card of cards) {
        const item = indexed.get(card.dataset.clip + ":" + card.dataset.group);
        card.querySelector(".decision").value = item.Decision;
        card.querySelector(".merge-target").value = item.MergeInto;
        card.querySelector(".person").value = item.SelectedPerson.slice(0, 120);
        card.querySelector(".notes").value = item.ReviewerNotes.slice(0, 1000);
      }
      dirty = true;
      status.textContent = "Previous decisions loaded. Save explicitly.";
      review();
    } catch {
      status.textContent = "Import refused: select an unchanged worksheet from this gallery.";
    }
    fileInput.value = "";
  });
  window.addEventListener("beforeunload", e => {
    if (dirty) {e.preventDefault(); e.returnValue = "";}
  });
  review();
})();