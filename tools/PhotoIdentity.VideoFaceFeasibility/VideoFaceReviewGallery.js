/* WI-0155 offline gallery: no dependencies, network or background persistence.
 * Review decisions stay in this page until the operator explicitly chooses
 * Save CSV. Edge/Chrome may support a user-selected local file picker;
 * other browsers download a CSV which can be imported for continued review.
 */
(() => {
  "use strict";

  const fields = [
    "Clip", "Section", "Frame", "Face", "Candidate",
    "Score", "Note", "HumanDecision", "ReviewerNotes"
  ];
  const cards = Array.from(document.querySelectorAll("article.review-card"));
  const status = document.getElementById("review-status");
  const save = document.getElementById("save-review");
  const load = document.getElementById("load-review");
  const chooser = document.getElementById("review-import");
  const choices = new Set([
    "", "Correct person", "Wrong person", "Known person - no reliable match",
    "Unknown person", "Not a face", "Cannot tell", "Review not applicable"
  ]);
  let dirty = false;
  let fileHandle = null;

  const key = (row) => [
    row.Clip, row.Section, row.Frame, row.Face, row.Candidate
  ].join("\u001f");

  const record = (card) => {
    const d = card.dataset;
    return {
      Clip: d.clip, Section: d.section, Frame: d.frame,
      Face: d.face, Candidate: d.candidate, Score: d.score,
      Note: d.note,
      HumanDecision: card.querySelector(".human-decision").value,
      ReviewerNotes: card.querySelector(".reviewer-notes").value
    };
  };

  // A frame-wide overflow warning is contextual evidence, not a face
  // identity decision. Exclude these optional rows from completion progress.
  const candidateCards = cards.filter(card => card.dataset.face !== "");

  const refresh = () => {
    const completed = candidateCards.filter(card => {
      return card.querySelector(".human-decision").value !== "";
    }).length;
    status.textContent = completed + " of " + candidateCards.length +
      " face observations reviewed (" +
      (cards.length - candidateCards.length) + " optional overflow entries)" +
      (dirty ? " · unsaved edits" : " · save explicitly to keep changes");
  };

  for (const card of cards) {
    card.querySelectorAll("select,textarea").forEach(control => {
      control.addEventListener("input", () => {
        dirty = true;
        refresh();
      });
    });
  }

  const quote = value => '"' + String(value ?? "").replaceAll('"', '""') + '"';
  const csv = () => {
    const rows = [
      fields.map(quote).join(","),
      ...cards.map(card => {
        const row = record(card);
        return fields.map(field => quote(row[field])).join(",");
      })
    ];
    return "\uFEFF" + rows.join("\r\n") + "\r\n";
  };

  const filename = () => {
    const stamp = new Date().toISOString().replaceAll(":", "-").replaceAll(".", "-");
    return "wi0155-human-review-" + stamp + ".csv";
  };

  const download = (contents) => {
    const blob = new Blob([contents], { type: "text/csv;charset=utf-8" });
    const href = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = href;
    a.download = filename();
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(href), 2000);
  };

  save.addEventListener("click", async () => {
    const contents = csv();
    try {
      if ("showSaveFilePicker" in window) {
        if (!fileHandle) {
          fileHandle = await window.showSaveFilePicker({
            suggestedName: filename(),
            types: [{
              description: "Private WI-0155 review CSV",
              accept: { "text/csv": [".csv"] }
            }]
          });
        }
        const writer = await fileHandle.createWritable();
        await writer.write(contents);
        await writer.close();
      } else {
        download(contents);
      }
      dirty = false;
      refresh();
    } catch (error) {
      // A cancelled or blocked picker must not discard changes.
      if (error && error.name === "AbortError") {
        status.textContent = "Save cancelled. Your edits remain in this page.";
      } else {
        status.textContent = "Could not save. Use another supported browser or export.";
      }
    }
  });

  // Minimal RFC-4180 CSV parser; quoted commas, quotes and newlines accepted.
  const parseCsv = (source) => {
    let text = source.replace(/^\uFEFF/, "");
    const rows = [];
    let row = [];
    let field = "";
    let quoted = false;
    for (let i = 0; i < text.length; i++) {
      const char = text[i];
      if (quoted) {
        if (char === '"' && text[i + 1] === '"') {
          field += '"';
          i++;
        } else if (char === '"') {
          quoted = false;
        } else {
          field += char;
        }
      } else if (char === '"' && field === "") {
        quoted = true;
      } else if (char === ",") {
        row.push(field);
        field = "";
      } else if (char === "\n") {
        row.push(field.replace(/\r$/, ""));
        if (row.some(value => value !== "")) rows.push(row);
        row = [];
        field = "";
      } else {
        field += char;
      }
    }
    if (quoted) throw new Error("Unclosed quoted field");
    if (field || row.length) {
      row.push(field.replace(/\r$/, ""));
      rows.push(row);
    }
    return rows;
  };

  load.addEventListener("click", () => chooser.click());
  chooser.addEventListener("change", async () => {
    const file = chooser.files?.[0];
    if (!file) return;
    try {
      if (file.size > 1024 * 1024) throw new Error("Review file too large");
      const rows = parseCsv(await file.text());
      if (rows.length < 2 || fields.some((name, i) => rows[0][i] !== name)) {
        throw new Error("Unexpected CSV structure");
      }

      const sourceRecords = new Map();
      for (const cells of rows.slice(1)) {
        if (cells.length !== fields.length) throw new Error("CSV column mismatch");
        const row = Object.fromEntries(fields.map((field, i) => [field, cells[i]]));
        if (!choices.has(row.HumanDecision)) throw new Error("Invalid review decision");
        const k = key(row);
        if (sourceRecords.has(k)) throw new Error("Duplicate review row");
        sourceRecords.set(k, row);
      }

      // Only import decisions for rows that match this exact shortlist.
      // No arbitrary files, HTML or source paths are imported.
      let applied = 0;
      for (const card of cards) {
        const row = sourceRecords.get(key(record(card)));
        if (!row) continue;
        card.querySelector(".human-decision").value = row.HumanDecision;
        card.querySelector(".reviewer-notes").value = row.ReviewerNotes.slice(0, 1000);
        applied++;
      }
      dirty = true;
      status.textContent = "Imported " + applied + " matching rows. Save to retain edits.";
      setTimeout(refresh, 2000);
    } catch {
      status.textContent = "Import refused: choose a review CSV from this gallery.";
    } finally {
      chooser.value = "";
    }
  });

  window.addEventListener("beforeunload", event => {
    if (!dirty) return;
    event.preventDefault();
    event.returnValue = "";
  });

  refresh();
})();
