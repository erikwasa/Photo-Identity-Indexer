/* WI-0155 offline pre-export merge-chain safety. Applies ONLY to
 * explicitly human-entered G/D/C merge decisions, never automatically
 * merges/identifies any person. No source frames or network.
 */
((target) => {
  "use strict";
  function valid(rows) {
    if (!Array.isArray(rows) || rows.length < 1 || rows.length > 64) {
      return false;
    }
    const byId = new Map();
    let type = null;
    for (const row of rows) {
      if (!row || typeof row.Group !== "string" ||
          !/^[GCD](?!000)\d{3}$/.test(row.Group) ||
          byId.has(row.Group)) return false;
      const prefix = row.Group[0];
      if (type === null) type = prefix;
      if (type !== prefix) return false; // Cross-source merges prohibited.
      byId.set(row.Group, row);
    }
    for (const row of rows) {
      const decision = String(row.Decision ?? "");
      const dest = String(row.MergeInto ?? "");
      if (decision !== "Merge group") {
        if (dest !== "") return false; // Stale targets are not allowed.
        continue;
      }
      const visited = new Set();
      let current = row;
      while (current.Decision === "Merge group") {
        if (!visited.add) return false;
        if (visited.has(current.Group) ||
            typeof current.MergeInto !== "string" ||
            !byId.has(current.MergeInto) ||
            current.MergeInto === current.Group) return false;
        visited.add(current.Group);
        const next = byId.get(current.MergeInto);
        const fromPerson = String(current.SelectedPerson ?? "");
        const nextPerson = String(next.SelectedPerson ?? "");
        if (fromPerson !== "" && nextPerson !== "" && fromPerson !== nextPerson) {
          return false;
        }
        current = next;
      }
      if (current.Decision !== "Keep group") return false;
      // A labelled fragment must not terminate at an incompatible root.
      const rootPerson = String(current.SelectedPerson ?? "");
      const initialPerson = String(row.SelectedPerson ?? "");
      if (rootPerson !== "" && initialPerson !== "" &&
          rootPerson !== initialPerson) return false;
    }
    return true;
  }
  const api = Object.freeze({ valid });
  target.WI0155MergeChainValidation = api;
  if (typeof module === "object" && module.exports) module.exports = api;
})(typeof globalThis === "object" ? globalThis : this);
