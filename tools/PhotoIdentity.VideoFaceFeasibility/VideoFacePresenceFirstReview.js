/* WI-0155 presence-first experimental private review.
 * Every decision belongs to one source-bound representative crop, NOT an
 * entire inferred group. All evidence remains transient until explicit CSV
 * save. Never automatically assign a person, merge groups or connect to SQL.
 */
(() => {
  "use strict";
  const columns = [
    "Clip", "ReportSha256", "Group", "Frame", "Face",
    "Decision", "PersonAlias", "ExcludeFromGroup", "ReviewerNotes"
  ];
  const decisions = new Set([
    "", "Confirm person", "Real face unknown", "Not a face", "Cannot tell"
  ]);
  const personPattern = /^P(?!0000)\d{4}$/;
  const groupPattern = /^G(?!000)\d{3}$/;
  const clipPattern = /^V(?!00)\d{2}$/;
  const hashPattern = /^[0-9a-f]{64}$/;
  const signature = row => [
    row.Clip, row.Group, row.Frame, row.Face
  ].join(":");
  // Verify *all* source rows even when the human intentionally stopped
  // without reviewing most of the gallery. Never accept forged group/crop
  // membership, an invented person alias or duplicate identities.
  function validate(rows, references, clip, hash) {
    if (!clipPattern.test(clip) || !hashPattern.test(hash) ||
        references.length < 1 || references.length > 320 ||
        rows.length !== references.length) return false;
    const expected = new Set();
    for (const ref of references) {
      const id = signature(ref);
      if (ref.Clip !== clip || !groupPattern.test(ref.Group) ||
          !/^[1-9]\d{0,2}$/.test(String(ref.Frame)) ||
          !/^[1-9]\d{0,1}$/.test(String(ref.Face)) ||
          Number(ref.Frame) > 180 || Number(ref.Face) > 32 ||
          expected.has(id)) return false;
      expected.add(id);
    }
    const observed = new Set();
    for (const row of rows) {
      if (!row || Object.keys(row).length !== columns.length ||
          columns.some(c => !Object.hasOwn(row, c)) ||
          row.Clip !== clip || row.ReportSha256 !== hash ||
          !groupPattern.test(row.Group) ||
          !/^[1-9]\d{0,2}$/.test(row.Frame) ||
          !/^[1-9]\d{0,1}$/.test(row.Face) ||
          Number(row.Frame) > 180 || Number(row.Face) > 32 ||
          !decisions.has(row.Decision) ||
          !["True", "False"].includes(row.ExcludeFromGroup) ||
          row.ReviewerNotes.length > 1000 ||
          (row.Decision === "Confirm person" ?
            !personPattern.test(row.PersonAlias) : row.PersonAlias !== "") ||
          (row.ExcludeFromGroup === "True" && row.Decision === "")) {
        return false;
      }
      const id = signature(row);
      if (!expected.has(id) || observed.has(id)) return false;
      observed.add(id);
    }
    return observed.size === expected.size;
  }
  function measure(rows, groups, unassigned) {
    const people = new Set();
    const byGroup = new Map();
    let confirmed = 0, unknown = 0, background = 0, cannotTell = 0;
    let unreviewed = 0, excluded = 0;
    for (const row of rows) {
      if (!byGroup.has(row.Group)) {
        byGroup.set(row.Group, { known: new Set(), reviewed: 0 });
      }
      const group = byGroup.get(row.Group);
      if (row.Decision !== "") group.reviewed++;
      if (row.ExcludeFromGroup === "True") excluded++;
      switch (row.Decision) {
        case "Confirm person":
          people.add(row.PersonAlias);
          group.known.add(row.PersonAlias);
          confirmed++;
          break;
        case "Real face unknown": unknown++; break;
        case "Not a face": background++; break;
        case "Cannot tell": cannotTell++; break;
        default: unreviewed++;
      }
    }
    const outstandingGroups = groups.filter(id =>
      !byGroup.has(id) || byGroup.get(id).reviewed === 0).length;
    const mixedGroups = [...byGroup.values()].filter(
      group => group.known.size > 1).length;
    return {
      KnownPersonPresenceProposals: people.size,
      ConfirmedRepresentativeCrops: confirmed,
      UnknownFaceRepresentativeCrops: unknown,
      BackgroundRepresentativeCrops: background,
      CannotTellRepresentativeCrops: cannotTell,
      UnreviewedRepresentativeCrops: unreviewed,
      ExcludedCropAnnotations: excluded,
      MixedIdentityGroups: mixedGroups,
      GroupsWithoutReviewedCrops: outstandingGroups,
      UnassignedGroupObservations: unassigned,
      PersonCoverageProven: false,
      ProductionAssignmentsMade: 0
    };
  }
  if (typeof module === "object" && module.exports) {
    module.exports = { columns, validate, measure };
    return;
  }

  const root = document.getElementById("pf-root");
  if (!root) return;
  const clip = root.dataset.clip;
  const hash = root.dataset.report;
  const unassigned = Number(root.dataset.unassigned);
  const cards = [...root.querySelectorAll(".presence-group")];
  const crops = [...root.querySelectorAll(".presence-crop")];
  const groups = cards.map(c => c.dataset.group);
  const refs = crops.map(c => ({
    Clip: clip, Group: c.dataset.group,
    Frame: c.dataset.frame, Face: c.dataset.face
  }));
  const status = document.getElementById("pf-status");
  const summary = document.getElementById("pf-summary");
  const picker = document.getElementById("pf-file");
  let dirty = false;
  let handle = null;
  function snapshot() {
    return crops.map(c => ({
      Clip: clip, ReportSha256: hash, Group: c.dataset.group,
      Frame: c.dataset.frame, Face: c.dataset.face,
      Decision: c.querySelector(".pf-decision").value,
      PersonAlias: c.querySelector(".pf-person").value.trim().toUpperCase(),
      ExcludeFromGroup: c.querySelector(".pf-exclude").checked ? "True" : "False",
      ReviewerNotes: c.querySelector(".pf-notes").value
    }));
  }
  function refresh() {
    const rows = snapshot();
    const valid = validate(rows, refs, clip, hash);
    const stats = measure(rows, groups, unassigned);
    const hidden = cards.filter(c => c.hidden).length;
    summary.textContent =
      stats.KnownPersonPresenceProposals + " distinct known people confirmed; " +
      stats.ConfirmedRepresentativeCrops + " confirming crops; " +
      stats.MixedIdentityGroups + " groups with multiple confirmed people; " +
      stats.ExcludedCropAnnotations + " excluded-crop annotations.\n" +
      stats.UnreviewedRepresentativeCrops + " representative crops unreviewed; " +
      stats.GroupsWithoutReviewedCrops + " groups with no reviewed crop; " +
      stats.UnassignedGroupObservations + " observations unassigned by the original grouper; " +
      hidden + " groups hidden for this browser session.\n" +
      stats.UnknownFaceRepresentativeCrops + " unknown real-face, " +
      stats.BackgroundRepresentativeCrops + " background, " +
      stats.CannotTellRepresentativeCrops + " uncertain reviewed crops. " +
      "Coverage NOT proven; zero production writes.";
    status.textContent = (valid ? "" : "INVALID crop decisions or aliases · ") +
      (dirty ? "UNSAVED decisions" : "Save explicitly to preserve decisions");
    return valid;
  }
  for (const crop of crops) {
    const person = crop.querySelector(".pf-person");
    const decision = crop.querySelector(".pf-decision");
    const toggle = () => {
      if (decision.value !== "Confirm person") person.value = "";
      person.disabled = decision.value !== "Confirm person";
    };
    toggle();
    for (const input of crop.querySelectorAll("input,select,textarea")) {
      input.addEventListener("input", () => {
        toggle(); dirty = true; refresh();
      });
      input.addEventListener("change", () => {
        toggle(); dirty = true; refresh();
      });
    }
  }
  for (const card of cards) {
    card.querySelector(".pf-skip").addEventListener("click", () => {
      card.hidden = true; refresh();
    });
  }
  document.getElementById("pf-reveal").addEventListener("click", () => {
    cards.forEach(card => { card.hidden = false; }); refresh();
  });
  const quote = s => '"' + String(s ?? "").replaceAll('"', '""') + '"';
  const csv = rows => "\uFEFF" + [
    columns.map(quote).join(","),
    ...rows.map(row => columns.map(col => quote(row[col])).join(","))
  ].join("\r\n") + "\r\n";
  function download(text) {
    const url = URL.createObjectURL(new Blob([text], { type: "text/csv;charset=utf-8" }));
    const a = document.createElement("a");
    a.href = url;
    a.download = "wi0155-" + clip + "-presence-first-" +
      new Date().toISOString().replace(/[:.]/g, "-") + ".csv";
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
  }
  document.getElementById("pf-save").addEventListener("click", async () => {
    if (!refresh()) {
      status.textContent = "Save refused: fix decisions, aliases or crop evidence";
      return;
    }
    const data = csv(snapshot());
    try {
      if ("showSaveFilePicker" in window) {
        if (!handle) {
          handle = await window.showSaveFilePicker({
            suggestedName: "wi0155-" + clip + "-presence-first.csv",
            types: [{ description: "Private face presence review",
              accept: { "text/csv": [".csv"] } }]
          });
        }
        const stream = await handle.createWritable();
        await stream.write(data);
        await stream.close();
      } else { download(data); }
      dirty = false; refresh();
    } catch {
      status.textContent = "Save cancelled or blocked; edits remain unsaved";
    }
  });
  function parseCsv(text) {
    const input = text.replace(/^\uFEFF/, "");
    const result = [];
    let fields = [], value = "", quoted = false;
    for (let i = 0; i < input.length; i++) {
      const ch = input[i];
      if (quoted) {
        if (ch === '"' && input[i + 1] === '"') { value += '"'; i++; }
        else if (ch === '"') { quoted = false; }
        else { value += ch; }
      } else if (ch === '"' && value === "") { quoted = true; }
      else if (ch === ",") { fields.push(value); value = ""; }
      else if (ch === "\n") {
        fields.push(value.replace(/\r$/, ""));
        if (fields.some(f => f !== "")) result.push(fields);
        fields = []; value = "";
      } else { value += ch; }
    }
    if (quoted) throw new Error("Unclosed CSV quote");
    if (fields.length || value) { fields.push(value.replace(/\r$/, "")); result.push(fields); }
    return result;
  }
  document.getElementById("pf-import").addEventListener("click", () => picker.click());
  picker.addEventListener("change", async () => {
    const file = picker.files?.[0];
    if (!file) return;
    try {
      if (file.size < 1 || file.size > 256 * 1024) {
        throw new Error("Missing or oversized CSV");
      }
      const raw = parseCsv(await file.text());
      if (raw.length !== crops.length + 1 ||
          raw[0].length !== columns.length ||
          raw[0].some((value, i) => value !== columns[i])) {
        throw new Error("Unexpected CSV header or rows");
      }
      const imported = raw.slice(1).map(vals => {
        if (vals.length !== columns.length) throw new Error("Invalid CSV row");
        return Object.fromEntries(columns.map((name, i) => [name, vals[i]]));
      });
      if (!validate(imported, refs, clip, hash)) {
        throw new Error("Report/crop/identity mismatch");
      }
      const byId = new Map(imported.map(row => [signature(row), row]));
      for (const crop of crops) {
        const row = byId.get(signature({
          Clip: clip, Group: crop.dataset.group,
          Frame: crop.dataset.frame, Face: crop.dataset.face
        }));
        crop.querySelector(".pf-decision").value = row.Decision;
        crop.querySelector(".pf-person").value = row.PersonAlias;
        crop.querySelector(".pf-exclude").checked = row.ExcludeFromGroup === "True";
        crop.querySelector(".pf-notes").value = row.ReviewerNotes;
        crop.querySelector(".pf-person").disabled = row.Decision !== "Confirm person";
      }
      handle = null;
      dirty = true;
      cards.forEach(card => { card.hidden = false; });
      refresh();
      status.textContent = "Validated worksheet imported; save explicitly to preserve any edits";
    } catch {
      status.textContent = "Import refused: CSV differs from this source-bound review";
    }
    picker.value = "";
  });
  window.addEventListener("beforeunload", event => {
    if (dirty) { event.preventDefault(); event.returnValue = ""; }
  });
  refresh();
})();
