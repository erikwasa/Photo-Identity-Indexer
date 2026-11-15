/* WI-0155: one private save/import for the C/N/U group-first workspace.
 * No network, persistent state, identity writes, dynamic code or inference.
 * Older source-specific galleries and CSV measurement scripts are unchanged.
 */
(() => {
  "use strict";
  const toolbar = document.getElementById("ws-toolbar");
  if (!toolbar) return;
  const clip = toolbar.dataset.clip;
  const hash = toolbar.dataset.report;
  if (!/^V(?!00)\d{2}$/.test(clip) || !/^[0-9a-f]{64}$/.test(hash)) return;

  const groups = [...document.querySelectorAll(".group-card")];
  const audits = [...document.querySelectorAll(".ws-audit-card")];
  const status = document.getElementById("ws-status");
  const fileInput = document.getElementById("ws-file");
  const columns = [
    "Source","Clip","AuditReportSha256","Group","Observations",
    "Decision","MergeInto","SelectedPerson","ReviewerNotes",
    "Sample","Frame","Face","Label","PersonAlias"
  ];
  const groupDecisions = new Set([
    "", "Keep group", "Merge group", "Dismiss false detection",
    "Split required", "Cannot tell"
  ]);
  const faceLabels = new Set(["", "Real face", "Not a face", "Cannot tell"]);
  const alias = /^(?:P(?:\d{2}|\d{4})|UNKNOWN)$/;
  const allowedGroupIds = new Set(groups.map(c => c.dataset.group));
  let dirty = false;
  let fileHandle = null;

  const groupRow = card => ({
    Source: "CenterFaceShadow", Clip: clip, AuditReportSha256: hash,
    Group: card.dataset.group, Observations: card.dataset.observations,
    Decision: card.querySelector(".decision").value,
    MergeInto: card.querySelector(".merge-target").value.trim(),
    SelectedPerson: card.querySelector(".person").value.trim().toUpperCase(),
    ReviewerNotes: card.querySelector(".notes").value,
    Sample: "", Frame: "", Face: "", Label: "", PersonAlias: ""
  });
  const auditRow = card => ({
    Source: card.dataset.source, Clip: clip, AuditReportSha256: hash,
    Group: "", Observations: "", Decision: "", MergeInto: "",
    SelectedPerson: "", ReviewerNotes: "",
    Sample: card.dataset.sample, Frame: card.dataset.frame,
    Face: card.dataset.face, Label: card.querySelector(".ws-label").value,
    PersonAlias: card.querySelector(".ws-person").value.trim().toUpperCase()
  });
  const snapshot = () => [
    ...groups.map(groupRow),
    ...audits.map(auditRow)
  ];
  const groupRowValid = row =>
    groupDecisions.has(row.Decision) &&
    (row.SelectedPerson === "" || alias.test(row.SelectedPerson)) &&
    row.ReviewerNotes.length <= 1000 &&
    (row.MergeInto === "" ||
      (row.Decision === "Merge group" &&
       allowedGroupIds.has(row.MergeInto) && row.MergeInto !== row.Group)) &&
    (row.Decision !== "Merge group" || row.MergeInto !== "");
  const auditRowValid = row =>
    faceLabels.has(row.Label) &&
    (row.PersonAlias === "" ||
      (row.Label === "Real face" && alias.test(row.PersonAlias)));
  const rowValid = row => row.Source === "CenterFaceShadow" ?
    groupRowValid(row) : auditRowValid(row);

  function syncAudit(card) {
    const person = card.querySelector(".ws-person");
    const isFace = card.querySelector(".ws-label").value === "Real face";
    person.disabled = !isFace;
    if (!isFace) person.value = "";
  }
  function refresh() {
    for (const card of audits) syncAudit(card);
    const rows = snapshot();
    const reviewed = rows.filter(row =>
      row.Source === "CenterFaceShadow" ? row.Decision !== "" : row.Label !== "").length;
    status.textContent = reviewed + " / " + rows.length + " reviewed" +
      (dirty ? " · UNSAVED combined edits" : " · Save all in one CSV");
  }
  for (const card of [...groups, ...audits]) {
    card.addEventListener("input", () => { dirty = true; refresh(); });
    card.addEventListener("change", () => { dirty = true; refresh(); });
  }

  // The existing quick-dismiss handler updates its select programmatically,
  // so also observe the button to mark the combined worksheet unsaved.
  for (const card of groups) {
    card.querySelector(".quick-dismiss").addEventListener("click", () => {
      dirty = true;
      refresh();
    });
  }

  const quote = text => '"' + String(text ?? "").replaceAll('"', '""') + '"';
  const serialize = rows => "\uFEFF" + [
    columns.map(quote).join(","),
    ...rows.map(row => columns.map(col => quote(row[col])).join(","))
  ].join("\r\n") + "\r\n";
  const filename = () => "wi0155-" + clip + "-combined-review-" +
    new Date().toISOString().replace(/[:.]/g, "-") + ".csv";
  const download = csv => {
    const url = URL.createObjectURL(new Blob([csv], {
      type: "text/csv;charset=utf-8"
    }));
    const a = document.createElement("a");
    a.href = url;
    a.download = filename();
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
  };
  document.getElementById("ws-save").addEventListener("click", async () => {
    const rows = snapshot();
    if (!rows.every(rowValid)) {
      status.textContent = "Save refused: check merge targets and person aliases";
      return;
    }
    try {
      const csv = serialize(rows);
      if ("showSaveFilePicker" in window) {
        if (!fileHandle) {
          fileHandle = await window.showSaveFilePicker({
            suggestedName: filename(),
            types: [{ description: "Private WI-0155 combined review",
              accept: { "text/csv": [".csv"] } }]
          });
        }
        const stream = await fileHandle.createWritable();
        await stream.write(csv);
        await stream.close();
      } else {
        download(csv);
      }
      dirty = false;
      window.dispatchEvent(new Event("wi0155-workspace-saved"));
      refresh();
    } catch {
      status.textContent = "Save cancelled or blocked; decisions remain unsaved";
    }
  });

  function parseCsv(source) {
    source = source.replace(/^\uFEFF/, "");
    if (source.length > 256 * 1024) throw new Error("Oversized review");
    const rows = [];
    let row = [], cell = "", quoted = false, closed = false;
    for (let i = 0; i < source.length; i++) {
      const c = source[i];
      if (quoted) {
        if (c === '"') {
          if (source[i + 1] === '"') { cell += '"'; i++; }
          else { quoted = false; closed = true; }
        } else { cell += c; }
      } else if (c === '"') {
        if (cell !== "" || closed) throw new Error("Malformed quoting");
        quoted = true;
      } else if (c === "," || c === "\n") {
        row.push(cell);
        cell = ""; closed = false;
        if (c === "\n") {
          if (row.some(v => v !== "")) rows.push(row);
          row = [];
        }
      } else if (c === "\r") {
        if (source[i + 1] !== "\n") throw new Error("Invalid newline");
      } else {
        if (closed) throw new Error("Trailing text after CSV quote");
        cell += c;
      }
    }
    if (quoted) throw new Error("Unclosed CSV quote");
    if (cell !== "" || row.length || closed) {
      row.push(cell);
      rows.push(row);
    }
    return rows;
  }
  document.getElementById("ws-import").addEventListener("click",
    () => fileInput.click());
  fileInput.addEventListener("change", async () => {
    const file = fileInput.files?.[0];
    if (!file) return;
    try {
      if (file.size > 256 * 1024) throw new Error("Oversized review");
      const parsed = parseCsv(await file.text());
      const expected = snapshot();
      if (parsed.length !== expected.length + 1 ||
        parsed[0].length !== columns.length ||
        columns.some((col, i) => parsed[0][i] !== col)) {
        throw new Error("Wrong combined worksheet header or count");
      }
      const proposed = parsed.slice(1).map(values => {
        if (values.length !== columns.length) throw new Error("Wrong row width");
        return Object.fromEntries(columns.map((col, i) => [col, values[i]]));
      });
      for (let i = 0; i < expected.length; i++) {
        const before = expected[i], after = proposed[i];
        for (const field of ["Source","Clip","AuditReportSha256","Group",
          "Observations","Sample","Frame","Face"]) {
          if (before[field] !== after[field]) {
            throw new Error("Source evidence changed");
          }
        }
        if (!rowValid(after)) throw new Error("Invalid reviewed decision");
      }
      // All-or-nothing import: do not change any UI control until validated.
      for (let i = 0; i < groups.length; i++) {
        const card = groups[i], row = proposed[i];
        card.querySelector(".decision").value = row.Decision;
        card.querySelector(".merge-target").value = row.MergeInto;
        card.querySelector(".person").value = row.SelectedPerson;
        card.querySelector(".notes").value = row.ReviewerNotes;
        card.querySelector(".decision").dispatchEvent(
          new Event("input", { bubbles: true }));
      }
      for (let i = 0; i < audits.length; i++) {
        const card = audits[i], row = proposed[groups.length + i];
        card.querySelector(".ws-label").value = row.Label;
        card.querySelector(".ws-person").value = row.PersonAlias;
      }
      dirty = true;
      refresh();
      status.textContent = "Combined review imported; save explicitly to retain edits";
    } catch {
      status.textContent = "Import refused: wrong clip, report, group or audit evidence";
    } finally {
      fileInput.value = "";
    }
  });
  window.addEventListener("beforeunload", event => {
    if (dirty) { event.preventDefault(); event.returnValue = ""; }
  });
  refresh();
})();
