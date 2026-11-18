/* WI-0155 private, browser-memory-only video face review.
 * No fetch, storage, API calls, video processing or catalogue writes.
 * Imported JSON contains sensitive images; use only on a trusted local host.
 */
(function (environment) {
    "use strict";
    const csvColumns = ["Clip", "ReportSha256", "Group", "Frame", "Face",
        "Decision", "PersonAlias", "ExcludeFromGroup", "ReviewerNotes"];
    const decisions = new Set(["", "Confirm person", "Real face unknown",
        "Not a face", "Cannot tell"]);
    const clipRx = /^V(?!00)\d{2}$/;
    const groupRx = /^G(?!000)\d{3}$/;
    const personRx = /^P(?!0000)\d{4}$/;
    const shaRx = /^[0-9a-f]{64}$/;
    const numberRx = /^[1-9]\d{0,2}$/;
    const pngPrefix = "iVBORw0KGgo";
    const maxBundleSize = 24 * 1024 * 1024;
    const key = row => [row.group || row.Group, row.frame || row.Frame,
        row.face || row.Face].join(":");

    function validateBundle(bundle) {
        if (!bundle || bundle.schemaVersion !== 1 || bundle.mode !== "PresenceFirst" ||
            !clipRx.test(bundle.clipAlias) || !shaRx.test(bundle.reportSha256) ||
            !Number.isInteger(bundle.unassignedGroupObservations) ||
            bundle.unassignedGroupObservations < 0 ||
            bundle.unassignedGroupObservations > 5760 ||
            !Array.isArray(bundle.groups) || bundle.groups.length < 1 ||
            bundle.groups.length > 64 ||
            !Array.isArray(bundle.crops) || bundle.crops.length < 1 ||
            bundle.crops.length > 320) return false;
        const groupMap = new Map();
        for (const group of bundle.groups) {
            if (!group || !groupRx.test(group.alias) ||
                !Number.isInteger(group.observations) || group.observations < 1 ||
                group.observations > 5760 || groupMap.has(group.alias) ||
                !Array.isArray(group.suggestions) || group.suggestions.length > 3 ||
                group.suggestions.some(s => typeof s !== "string" ||
                    !/^P(?!0000)\d{4}: (?:0|1)\.\d{4}$/.test(s))) return false;
            groupMap.set(group.alias, {observations: group.observations, crops: 0});
        }
        const cropKeys = new Set(), frameFaces = new Set();
        let encodedBytes = 0;
        for (const crop of bundle.crops) {
            if (!crop || !groupMap.has(crop.group) ||
                typeof crop.frame !== "string" ||
                typeof crop.face !== "string" ||
                !numberRx.test(crop.frame) || Number(crop.frame) > 180 ||
                !/^[1-9]\d{0,1}$/.test(crop.face) || Number(crop.face) > 32 ||
                typeof crop.imageBase64 !== "string" ||
                crop.imageBase64.length < 40 ||
                crop.imageBase64.length > 1400000 ||
                !crop.imageBase64.startsWith(pngPrefix) ||
                !/^[A-Za-z0-9+/]+={0,2}$/.test(crop.imageBase64) ||
                cropKeys.has(key(crop)) ||
                frameFaces.has(crop.frame + ":" + crop.face)) return false;
            encodedBytes += crop.imageBase64.length;
            if (encodedBytes > maxBundleSize) return false;
            cropKeys.add(key(crop));
            frameFaces.add(crop.frame + ":" + crop.face);
            groupMap.get(crop.group).crops++;
        }
        return [...groupMap.values()].every(g => g.crops >= 1 &&
            g.crops <= g.observations && g.crops <= 5);
    }

    function newRows(bundle) {
        return bundle.crops.map(crop => ({
            Clip: bundle.clipAlias, ReportSha256: bundle.reportSha256,
            Group: crop.group, Frame: crop.frame, Face: crop.face,
            Decision: "", PersonAlias: "", ExcludeFromGroup: "False",
            ReviewerNotes: ""
        }));
    }

    function validateRows(rows, bundle) {
        if (!validateBundle(bundle) || !Array.isArray(rows) ||
            rows.length !== bundle.crops.length) return false;
        const expected = new Set(bundle.crops.map(key));
        const observed = new Set();
        for (const row of rows) {
            if (!row || Object.keys(row).length !== csvColumns.length ||
                csvColumns.some(c => !Object.hasOwn(row, c)) ||
                row.Clip !== bundle.clipAlias ||
                row.ReportSha256 !== bundle.reportSha256 ||
                !groupRx.test(row.Group) ||
                typeof row.Frame !== "string" || typeof row.Face !== "string" ||
                !numberRx.test(row.Frame) || Number(row.Frame) > 180 ||
                !/^[1-9]\d{0,1}$/.test(row.Face) || Number(row.Face) > 32 ||
                !decisions.has(row.Decision) ||
                !["True", "False"].includes(row.ExcludeFromGroup) ||
                typeof row.PersonAlias !== "string" ||
                typeof row.ReviewerNotes !== "string" ||
                row.ReviewerNotes.length > 1000 ||
                (row.Decision === "Confirm person" ?
                    !personRx.test(row.PersonAlias) : row.PersonAlias !== "") ||
                (row.ExcludeFromGroup === "True" && row.Decision === "")) return false;
            const signature = key(row);
            if (!expected.has(signature) || observed.has(signature)) return false;
            observed.add(signature);
        }
        return observed.size === expected.size;
    }

    function metrics(rows, bundle) {
        const known = new Set(), byGroup = new Map();
        let confirmed = 0, unknown = 0, background = 0, uncertain = 0;
        let unreviewed = 0, excluded = 0;
        for (const row of rows) {
            if (!byGroup.has(row.Group))
                byGroup.set(row.Group, {reviewed: 0, people: new Set()});
            const group = byGroup.get(row.Group);
            if (row.Decision) group.reviewed++;
            if (row.ExcludeFromGroup === "True") excluded++;
            switch (row.Decision) {
                case "Confirm person":
                    known.add(row.PersonAlias); group.people.add(row.PersonAlias);
                    confirmed++; break;
                case "Real face unknown": unknown++; break;
                case "Not a face": background++; break;
                case "Cannot tell": uncertain++; break;
                default: unreviewed++;
            }
        }
        return {
            knownPeople: known.size, confirmed, duplicateConfirmations: confirmed - known.size,
            unknown, background, uncertain, unreviewed, excluded,
            mixedGroups: [...byGroup.values()].filter(g => g.people.size > 1).length,
            groupsWithoutReview: bundle.groups.filter(g =>
                !byGroup.has(g.alias) || byGroup.get(g.alias).reviewed === 0).length,
            unshownObservations: bundle.groups.reduce((sum, g) => sum + g.observations, 0) -
                bundle.crops.length,
            unassignedObservations: bundle.unassignedGroupObservations,
            personCoverageProven: false, productionAssignmentsMade: 0
        };
    }

    function csvEncode(rows) {
        const quote = value => '"' + String(value).replaceAll('"', '""') + '"';
        return "\uFEFF" + [csvColumns.map(quote).join(","),
            ...rows.map(row => csvColumns.map(column => quote(row[column])).join(","))]
            .join("\r\n") + "\r\n";
    }

    function csvParse(csv) {
        const text = csv.replace(/^\uFEFF/, "");
        const records = [];
        let cells = [], value = "", quoted = false, closed = false;
        for (let i = 0; i < text.length; i++) {
            const ch = text[i];
            if (quoted) {
                if (ch === '"' && text[i + 1] === '"') { value += '"'; i++; }
                else if (ch === '"') { quoted = false; closed = true; }
                else value += ch;
            } else if (ch === '"' && value === "" && !closed) quoted = true;
            else if (ch === ",") { cells.push(value); value = ""; closed = false; }
            else if (ch === "\n") {
                cells.push(value.replace(/\r$/, ""));
                if (cells.some(cell => cell !== "")) records.push(cells);
                cells = []; value = ""; closed = false;
            } else if (closed) {
                // RFC 4180 only allows comma or row terminator after a closing quote.
                throw new Error("Invalid CSV after quoted value.");
            } else if (ch === '"') {
                throw new Error("Unexpected quote in CSV.");
            } else value += ch;
        }
        if (quoted) throw new Error("Unclosed CSV quote.");
        if (cells.length || value) { cells.push(value.replace(/\r$/, "")); records.push(cells); }
        return records;
    }

    function importCsv(csv, bundle) {
        const records = csvParse(csv);
        if (records.length !== bundle.crops.length + 1 ||
            records[0].length !== csvColumns.length ||
            records[0].some((column, i) => column !== csvColumns[i]))
            throw new Error("Incorrect CSV header or row count.");
        const rows = records.slice(1).map(cells => {
            if (cells.length !== csvColumns.length) throw new Error("Incorrect CSV column count.");
            return Object.fromEntries(csvColumns.map((column, i) => [column, cells[i]]));
        });
        if (!validateRows(rows, bundle)) throw new Error("CSV does not match the imported face evidence.");
        const byKey = new Map(rows.map(row => [key(row), row]));
        return bundle.crops.map(crop => byKey.get(key(crop)));
    }

    let active = null;
    let dirty = false;
    function markDirty(value) { dirty = value; }
    function dom(tag, className, value) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (value !== undefined) element.textContent = value;
        return element;
    }
    function label(text, control) {
        const item = dom("label", "vfr-field");
        item.append(dom("span", "", text), control);
        return item;
    }
    function refresh() {
        if (!active) return;
        const summary = document.getElementById("vfr-summary");
        const status = document.getElementById("vfr-status");
        const m = metrics(active.rows, active.bundle);
        summary.textContent =
            m.knownPeople + " distinct known-person proposals · " +
            m.confirmed + " confirming crops (" + m.duplicateConfirmations + " duplicates)\n" +
            m.unknown + " unknown-face observations · " + m.background +
            " background · " + m.uncertain + " uncertain\n" +
            m.unreviewed + " unreviewed crops · " + m.groupsWithoutReview +
            " groups without review · " + m.mixedGroups + " mixed-identity groups\n" +
            m.excluded + " crop exclusions · " + m.unshownObservations +
            " unshown group observations · " + m.unassignedObservations +
            " unassigned observations\nCoverage not proven · 0 production assignments";
        status.textContent = (validateRows(active.rows, active.bundle) ?
            (dirty ? "Unsaved changes — export CSV before leaving." :
                "No unsaved changes. Review remains private.") :
            "Invalid decision/alias — correct before exporting.");
    }
    function render() {
        const root = document.getElementById("vfr-root");
        root.replaceChildren();
        if (!active) return;
        const bundle = active.bundle;
        root.append(dom("h2", "", bundle.clipAlias + " · " + bundle.groups.length +
            " provisional groups · " + bundle.crops.length + " representative crops"));
        const summary = dom("pre", "vfr-summary");
        summary.id = "vfr-summary";
        root.append(summary);
        const actions = dom("div", "vfr-actions");
        const save = dom("button", "button primary", "Export private review CSV");
        save.type = "button";
        save.addEventListener("click", () => {
            if (!validateRows(active.rows, active.bundle)) {
                refresh(); return;
            }
            const csv = csvEncode(active.rows);
            const url = URL.createObjectURL(new Blob([csv], {type: "text/csv;charset=utf-8"}));
            const link = document.createElement("a");
            link.href = url;
            link.download = "wi0155-" + bundle.clipAlias + "-presence-first.csv";
            document.body.append(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 2000);
            markDirty(false); refresh();
        });
        const importButton = dom("button", "button secondary", "Import saved review CSV");
        importButton.type = "button";
        importButton.addEventListener("click", () => document.getElementById("vfr-csv").click());
        const reveal = dom("button", "button secondary", "Reveal hidden groups");
        reveal.type = "button";
        reveal.addEventListener("click", () => {
            root.querySelectorAll(".vfr-group").forEach(card => { card.hidden = false; });
        });
        actions.append(save, importButton, reveal);
        root.append(actions);
        const status = dom("p", "vfr-status");
        status.id = "vfr-status";
        status.setAttribute("role", "status");
        root.append(status);
        const cards = dom("div", "vfr-grid");
        for (const group of bundle.groups) {
            const card = dom("article", "vfr-group");
            card.append(dom("h3", "", group.alias));
            const groupCrops = bundle.crops.filter(c => c.group === group.alias);
            card.append(dom("p", "", groupCrops.length + " representative crops of " +
                group.observations + " observations; " +
                (group.observations - groupCrops.length) + " unshown"));
            card.append(dom("p", "vfr-suggestions",
                "Exploratory group leads (not identifications): " +
                (group.suggestions.length ? group.suggestions.join(" · ") : "Not available")));
            const hide = dom("button", "button secondary", "Hide without deciding");
            hide.type = "button";
            hide.addEventListener("click", () => { card.hidden = true; });
            card.append(hide);
            for (const crop of groupCrops) {
                const row = active.rows.find(r => key(r) === key(crop));
                const item = dom("div", "vfr-crop");
                const image = dom("img", "vfr-image");
                image.src = "data:image/png;base64," + crop.imageBase64;
                image.alt = "Private candidate face crop; not a confirmed identity";
                image.loading = "lazy";
                item.append(image, dom("small", "", "Frame " + crop.frame +
                    " · face " + crop.face));
                const decision = dom("select");
                for (const value of decisions) {
                    const option = dom("option", "", value || "Not reviewed");
                    option.value = value;
                    decision.append(option);
                }
                decision.value = row.Decision;
                const person = dom("input");
                person.type = "text";
                person.maxLength = 5;
                person.placeholder = "P0001";
                person.autocomplete = "off";
                person.value = row.PersonAlias;
                person.disabled = row.Decision !== "Confirm person";
                const excluded = dom("input");
                excluded.type = "checkbox";
                excluded.checked = row.ExcludeFromGroup === "True";
                const notes = dom("textarea");
                notes.maxLength = 1000;
                notes.value = row.ReviewerNotes;
                decision.addEventListener("change", () => {
                    row.Decision = decision.value;
                    if (row.Decision !== "Confirm person") { row.PersonAlias = ""; person.value = ""; }
                    person.disabled = row.Decision !== "Confirm person";
                    if (row.Decision === "") { excluded.checked = false; row.ExcludeFromGroup = "False"; }
                    markDirty(true); refresh();
                });
                person.addEventListener("input", () => {
                    row.PersonAlias = person.value.trim().toUpperCase();
                    markDirty(true); refresh();
                });
                excluded.addEventListener("change", () => {
                    row.ExcludeFromGroup = excluded.checked ? "True" : "False";
                    markDirty(true); refresh();
                });
                notes.addEventListener("input", () => {
                    row.ReviewerNotes = notes.value; markDirty(true); refresh();
                });
                item.append(label("Human decision", decision), label("Private known-person alias", person));
                const exclusionLabel = dom("label", "vfr-check");
                exclusionLabel.append(excluded,
                    dom("span", "", "Exclude this crop from its provisional group (annotation only)"));
                item.append(exclusionLabel, label("Optional private note", notes));
                card.append(item);
            }
            cards.append(card);
        }
        root.append(cards);
        refresh();
    }

    function init() {
        const input = document.getElementById("vfr-bundle");
        const csvInput = document.getElementById("vfr-csv");
        if (!input || !csvInput || input.dataset.initialized === "true") return;
        input.dataset.initialized = "true";
        const status = document.getElementById("vfr-load-status");
        input.addEventListener("change", async () => {
            const file = input.files && input.files[0];
            if (!file) return;
            try {
                if (dirty && !environment.confirm("Discard unsaved review changes?")) return;
                if (file.size < 1 || file.size > maxBundleSize) throw new Error("File too large or empty.");
                const bundle = JSON.parse(await file.text());
                if (!validateBundle(bundle)) throw new Error("Invalid or unsupported private evidence bundle.");
                active = {bundle, rows: newRows(bundle)};
                markDirty(false);
                render();
                status.textContent = "Private package loaded in browser memory. No upload or database write.";
            } catch (error) {
                status.textContent = "Import refused: " + error.message;
            } finally {
                input.value = "";
            }
        });
        csvInput.addEventListener("change", async () => {
            const file = csvInput.files && csvInput.files[0];
            if (!file || !active) return;
            try {
                if (file.size < 1 || file.size > 256 * 1024)
                    throw new Error("Missing or oversized CSV.");
                const imported = importCsv(await file.text(), active.bundle);
                active.rows = imported;
                markDirty(true);
                render();
                status.textContent = "Source-bound CSV loaded; export to preserve changes.";
            } catch (error) {
                status.textContent = "CSV rejected: " + error.message;
            } finally {
                csvInput.value = "";
            }
        });
    }
    if (environment.addEventListener) environment.addEventListener("beforeunload", event => {
        if (dirty) { event.preventDefault(); event.returnValue = ""; }
    });
    const api = {validateBundle, newRows, validateRows, metrics, csvEncode,
        csvParse, importCsv, init};
    if (typeof module === "object" && module.exports) module.exports = api;
    else environment.photoIdentityVideoFaceReview = api;
})(typeof window === "object" ? window : {});
