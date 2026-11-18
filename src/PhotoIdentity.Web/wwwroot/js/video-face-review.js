/* Local-only video face review. Original face evidence stays in browser memory.
 * v3 resolves canonical names via existing read-only people API; no photo/video
 * identity writes, storage, media requests, inference or background work.
 * Legacy v2 can still load an explicit snapshot map for CSV compatibility.
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
    const snapshotRx = /^[0-9]+:[0-9]+:(?:[0-9]+(?:,[0-9]+)*)?$/;
    const uuidRx = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    const numberRx = /^[1-9]\d{0,2}$/;
    const pngPrefix = "iVBORw0KGgo";
    const maxBundleSize = 24 * 1024 * 1024;
    const key = row => [row.group || row.Group, row.frame || row.Frame,
        row.face || row.Face].join(":");
    function pngDimensions(base64) {
        try {
            // PNG IHDR supplies the native dimensions, not an enlarged preview.
            const header = atob(base64.slice(0, 44));
            if (header.slice(12, 16) !== "IHDR") return null;
            const dimension = i => header.charCodeAt(i) * 16777216 +
                header.charCodeAt(i + 1) * 65536 +
                header.charCodeAt(i + 2) * 256 + header.charCodeAt(i + 3);
            const width = dimension(16), height = dimension(20);
            return width >= 1 && width <= 640 && height >= 1 && height <= 640
                ? {width, height} : null;
        } catch {
            return null;
        }
    }
    const boundedPng = base64 => pngDimensions(base64) !== null;

    function validateBundle(bundle) {
        if (!bundle || ![1, 2, 3].includes(bundle.schemaVersion) || bundle.mode !== "PresenceFirst" ||
            typeof bundle.clipAlias !== "string" || !clipRx.test(bundle.clipAlias) ||
            typeof bundle.reportSha256 !== "string" || !shaRx.test(bundle.reportSha256) ||
            !Number.isInteger(bundle.unassignedGroupObservations) ||
            bundle.unassignedGroupObservations < 0 ||
            bundle.unassignedGroupObservations > 5760 ||
            (bundle.schemaVersion >= 2 &&
                (typeof bundle.catalogueSnapshot !== "string" ||
                 !snapshotRx.test(bundle.catalogueSnapshot) ||
                 bundle.catalogueSnapshot.length > 256 ||
                 !Number.isInteger(bundle.cataloguePeopleCount) ||
                 bundle.cataloguePeopleCount < 1 || bundle.cataloguePeopleCount > 4096)) ||
            (bundle.schemaVersion === 3 &&
                (!Array.isArray(bundle.personBindings) ||
                 bundle.personBindings.length !== bundle.cataloguePeopleCount ||
                 bundle.personBindings.some(p => !p || typeof p.alias !== "string" ||
                     !personRx.test(p.alias) || typeof p.personId !== "string" ||
                     !uuidRx.test(p.personId) ||
                     p.personId.toLowerCase() === "00000000-0000-0000-0000-000000000000") ||
                 new Set(bundle.personBindings.map(p => p.alias)).size !==
                     bundle.cataloguePeopleCount ||
                 new Set(bundle.personBindings.map(p => p.personId.toLowerCase())).size !==
                     bundle.cataloguePeopleCount)) ||
            !Array.isArray(bundle.groups) || bundle.groups.length < 1 ||
            bundle.groups.length > 64 ||
            !Array.isArray(bundle.crops) || bundle.crops.length < 1 ||
            bundle.crops.length > 320) return false;
        const groupMap = new Map();
        for (const group of bundle.groups) {
            if (!group || typeof group.alias !== "string" || !groupRx.test(group.alias) ||
                !Number.isInteger(group.observations) || group.observations < 1 ||
                group.observations > 5760 || groupMap.has(group.alias) ||
                !Array.isArray(group.suggestions) || group.suggestions.length > 3 ||
                group.suggestions.some(s => typeof s !== "string" ||
                    !/^P(?!0000)\d{4}: -?(?:0|1)\.\d{4}$/.test(s))) return false;
            groupMap.set(group.alias, {observations: group.observations, crops: 0});
        }
        const cropKeys = new Set(), frameFaces = new Set();
        let encodedBytes = 0;
        for (const crop of bundle.crops) {
            if (!crop || typeof crop.group !== "string" || !groupMap.has(crop.group) ||
                typeof crop.frame !== "string" ||
                typeof crop.face !== "string" ||
                !numberRx.test(crop.frame) || Number(crop.frame) > 180 ||
                !/^[1-9]\d{0,1}$/.test(crop.face) || Number(crop.face) > 32 ||
                typeof crop.imageBase64 !== "string" ||
                crop.imageBase64.length < 40 ||
                crop.imageBase64.length > 1400000 ||
                !crop.imageBase64.startsWith(pngPrefix) ||
                !boundedPng(crop.imageBase64) ||
                (bundle.schemaVersion >= 2 &&
                    (!Number.isInteger(crop.frameWidth) ||
                     !Number.isInteger(crop.frameHeight) ||
                     !Number.isInteger(crop.faceWidthPx) ||
                     !Number.isInteger(crop.faceHeightPx) ||
                     crop.frameWidth < 1 || crop.frameWidth > 3840 ||
                     crop.frameHeight < 1 || crop.frameHeight > 3840 ||
                     crop.faceWidthPx < 1 || crop.faceWidthPx > crop.frameWidth + 1 ||
                     crop.faceHeightPx < 1 || crop.faceHeightPx > crop.frameHeight + 1)) ||
                (bundle.schemaVersion === 3 &&
                    (!Array.isArray(crop.suggestions) ||
                     crop.suggestions.length < 1 || crop.suggestions.length > 3 ||
                     crop.suggestions.some(s => typeof s !== "string" ||
                         !/^P(?!0000)\d{4}: -?(?:0|1)\.\d{4}$/.test(s) ||
                         !bundle.personBindings.some(p => p.alias === s.split(": ")[0])))) ||
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
            g.crops <= g.observations && g.crops <= 5) &&
            (bundle.schemaVersion !== 3 || bundle.groups.every(group =>
                group.suggestions.every(s => bundle.personBindings.some(p =>
                    p.alias === s.split(": ")[0]))));
    }

    // No names/UUIDs arrive in the face package. This separate import must
    // match its exact read-only catalogue snapshot; a stale map is rejected.
    function validatePeopleMap(bundle, map) {
        if (!validateBundle(bundle) || bundle.schemaVersion !== 2 ||
            !map || map.schemaVersion !== 1 ||
            map.pgSnapshot !== bundle.catalogueSnapshot ||
            !Array.isArray(map.people) ||
            map.people.length !== bundle.cataloguePeopleCount) return false;
        const aliases = new Set(), ids = new Set();
        for (const person of map.people) {
            if (!person || !personRx.test(person.alias) ||
                !uuidRx.test(person.personId) ||
                person.personId.toLowerCase() === "00000000-0000-0000-0000-000000000000" ||
                (person.displayName !== null && person.displayName !== undefined &&
                    (typeof person.displayName !== "string" ||
                     !person.displayName.trim() ||
                     person.displayName.length > 200 ||
                     [...person.displayName].some(ch =>
                         ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127))) ||
                aliases.has(person.alias) || ids.has(person.personId.toLowerCase()))
                return false;
            aliases.add(person.alias);
            ids.add(person.personId.toLowerCase());
        }
        return bundle.groups.every(group => group.suggestions.every(s =>
            aliases.has(s.split(": ")[0])));
    }

    // Resolve live current canonical people by immutable ID, not the prior
    // snapshot's display names. We never invent aliases for people created later.
    function resolveLivePeople(bundle, catalogue) {
        if (!validateBundle(bundle) || bundle.schemaVersion !== 3 ||
            !Array.isArray(catalogue) || catalogue.length > 4096) return null;
        const byId = new Map();
        for (const row of catalogue) {
            if (!row || typeof row.id !== "string" || !uuidRx.test(row.id) ||
                typeof row.displayName !== "string" || !row.displayName.trim() ||
                row.displayName.length > 200 || byId.has(row.id.toLowerCase()))
                return null;
            byId.set(row.id.toLowerCase(), row);
        }
        const bindings = new Map(bundle.personBindings.map(p =>
            [p.personId.toLowerCase(), p.alias]));
        return { people: catalogue.map(p => ({
            alias: bindings.get(p.id.toLowerCase()) || null,
            personId: p.id.toLowerCase(),
            displayName: p.displayName
        })), live: true };
    }
    function isCompleted(row) {
        return row.Decision !== "" &&
            (row.Decision !== "Confirm person" || personRx.test(row.PersonAlias));
    }
    const personName = person => person.displayName || "Unnamed person";
    function searchPeople(map, query) {
        if (!map) return [];
        const needle = query.trim().toLocaleLowerCase();
        return map.people.filter(person =>
            (personName(person) + " " + (person.alias || "")).toLocaleLowerCase().includes(needle))
            .sort((a, b) => personName(a).localeCompare(personName(b)) ||
                (a.alias || "").localeCompare(b.alias || ""))
            .slice(0, 20);
    }
    function describeSuggestion(suggestion, map) {
        if (!map) return suggestion;
        const alias = suggestion.split(": ")[0];
        const person = map.people.find(p => p.alias === alias);
        return person
            ? personName(person) + " (" + alias + ")" + suggestion.slice(alias.length)
            : suggestion;
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
                    !personRx.test(row.PersonAlias) ||
                    (bundle.schemaVersion === 3 &&
                        !bundle.personBindings.some(p => p.alias === row.PersonAlias))
                    : row.PersonAlias !== "") ||
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
            } else if (ch === "\r" && text[i + 1] === "\n") {
                // CRLF row separator after the closing quote.
                continue;
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
    let generation = 0;
    function markDirty(value) { dirty = value; }
    function hasUnsavedChanges() { return dirty; }
    function dispose() {
        generation++;
        active = null;
        markDirty(false);
        const root = typeof document === "object" && document.getElementById("vfr-root");
        if (root) root.replaceChildren();
    }
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
    function refreshVisibility() {
        if (!active) return;
        const root = document.getElementById("vfr-root");
        root.querySelectorAll(".vfr-group").forEach(card => {
            const rows = active.rows.filter(r => r.Group === card.dataset.group);
            card.querySelectorAll(".vfr-crop").forEach(item => {
                const row = active.rows.find(r => key(r) === item.dataset.cropKey);
                item.hidden = !active.showReviewed && isCompleted(row);
            });
            card.hidden = active.manualHidden.has(card.dataset.group) ||
                (!active.showReviewed && rows.every(isCompleted));
        });
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
        const showReviewed = dom("button", "button secondary",
            active.showReviewed ? "Hide reviewed" : "Show reviewed");
        showReviewed.type = "button";
        showReviewed.addEventListener("click", () => {
            active.showReviewed = !active.showReviewed;
            showReviewed.textContent = active.showReviewed ? "Hide reviewed" : "Show reviewed";
            refreshVisibility();
        });
        const reveal = dom("button", "button secondary", "Reveal manually hidden groups");
        reveal.type = "button";
        reveal.addEventListener("click", () => {
            active.manualHidden.clear();
            refreshVisibility();
        });
        actions.append(save, importButton, showReviewed, reveal);
        root.append(actions);
        const status = dom("p", "vfr-status");
        status.id = "vfr-status";
        status.setAttribute("role", "status");
        root.append(status);
        const cards = dom("div", "vfr-grid");
        for (const group of bundle.groups) {
            const card = dom("article", "vfr-group");
            card.dataset.group = group.alias;
            card.append(dom("h3", "", group.alias));
            const groupCrops = bundle.crops.filter(c => c.group === group.alias);
            card.append(dom("p", "", groupCrops.length + " representative crops of " +
                group.observations + " observations; " +
                (group.observations - groupCrops.length) + " unshown"));
            card.append(dom("p", "vfr-suggestions",
                bundle.schemaVersion === 3
                    ? "Per-face SFace leads are unverified. Choose a person for each face:"
                    : "Group-level leads are unverified. Choose a person for each face:"));
            const hide = dom("button", "button secondary", "Hide without deciding");
            hide.type = "button";
            hide.addEventListener("click", () => {
                active.manualHidden.add(group.alias);
                refreshVisibility();
            });
            card.append(hide);
            for (const crop of groupCrops) {
                const row = active.rows.find(r => key(r) === key(crop));
                const item = dom("div", "vfr-crop");
                item.dataset.cropKey = key(row);
                const image = dom("img", "vfr-image");
                image.src = "data:image/png;base64," + crop.imageBase64;
                image.alt = "Private candidate face crop; not a confirmed identity";
                image.loading = "lazy";
                const pixels = pngDimensions(crop.imageBase64);
                item.append(image, dom("small", "", "Frame " + crop.frame +
                    " · face " + crop.face + " · crop " +
                    pixels.width + " × " + pixels.height + " px (no upscaling)" +
                    (bundle.schemaVersion >= 2
                        ? " · extracted frame " + crop.frameWidth + " × " +
                            crop.frameHeight + " px · detected face " +
                            crop.faceWidthPx + " × " + crop.faceHeightPx + " px"
                        : "")));
                const suggested = dom("ol", "vfr-suggestion-list");
                const leads = bundle.schemaVersion === 3 ? crop.suggestions : group.suggestions;
                if (leads.length && active.people) {
                    for (const suggestion of leads) {
                        const alias = suggestion.split(": ")[0];
                        const candidate = active.people.people.find(p => p.alias === alias);
                        const entry = dom("li", "");
                        const choose = dom("button", "vfr-suggestion-button",
                            describeSuggestion(suggestion, active.people));
                        choose.type = "button";
                        choose.disabled = !candidate;
                        choose.title = candidate
                            ? "Confirm this person for this face only"
                            : "Suggested person is no longer in the current catalogue";
                        choose.addEventListener("click", () => {
                            row.Decision = "Confirm person";
                            row.PersonAlias = alias;
                            decision.value = row.Decision;
                            person.value = personName(candidate);
                            person.disabled = false;
                            markDirty(true);
                            refresh();
                            refreshVisibility();
                        });
                        entry.append(choose);
                        suggested.append(entry);
                    }
                } else if (leads.length) {
                    suggested.append(dom("li", "", "Load current people to select a suggestion."));
                } else {
                    suggested.append(dom("li", "", "No available suggestions."));
                }
                item.append(suggested);
                const decision = dom("select");
                for (const value of decisions) {
                    const option = dom("option", "", value || "Not reviewed");
                    option.value = value;
                    decision.append(option);
                }
                decision.value = row.Decision;
                if (bundle.schemaVersion === 3 && !active.people) {
                    [...decision.options].find(o => o.value === "Confirm person").disabled = true;
                }
                const person = dom("input");
                const picker = dom("div", "vfr-person-picker");
                const results = dom("div", "vfr-person-results");
                results.hidden = true;
                const named = active.people !== null;
                const selected = named &&
                    active.people.people.find(p => p.alias === row.PersonAlias);
                person.type = named ? "search" : "text";
                person.maxLength = named ? 200 : 5;
                person.placeholder = named ? "Search all people by name or alias" : "P0001";
                person.autocomplete = "off";
                person.value = named && selected ? personName(selected) : row.PersonAlias;
                person.disabled = row.Decision !== "Confirm person" ||
                    (bundle.schemaVersion === 3 && !named);
                picker.append(person, results);
                function renderChoices() {
                    if (!named || person.disabled) { results.hidden = true; return; }
                    results.replaceChildren();
                    for (const candidate of searchPeople(active.people, person.value)) {
                        const option = dom("button", "vfr-person-option",
                            personName(candidate) +
                            (candidate.alias ? " (" + candidate.alias + ")" : " (new since snapshot)"));
                        option.type = "button";
                        option.disabled = !candidate.alias;
                        option.title = candidate.alias
                            ? "Select person"
                            : "Person was added after the source snapshot; new evidence is required";
                        option.addEventListener("click", () => {
                            row.PersonAlias = candidate.alias;
                            person.value = personName(candidate);
                            results.hidden = true;
                            markDirty(true);
                            refresh();
                            refreshVisibility();
                        });
                        results.append(option);
                    }
                    if (results.childElementCount === 0)
                        results.append(dom("small", "", "No matching people."));
                    results.hidden = false;
                }
                if (named) {
                    person.addEventListener("focus", renderChoices);
                    person.addEventListener("input", () => {
                        // Free text is not an identity: the person must be selected.
                        row.PersonAlias = "";
                        markDirty(true);
                        refresh();
                        renderChoices();
                    });
                    person.addEventListener("keydown", event => {
                        if (event.key === "Escape") results.hidden = true;
                    });
                } else {
                    person.addEventListener("input", () => {
                        row.PersonAlias = person.value.trim().toUpperCase();
                        markDirty(true);
                        refresh();
                    });
                }
                const excluded = dom("input");
                excluded.type = "checkbox";
                excluded.checked = row.ExcludeFromGroup === "True";
                const notes = dom("textarea");
                notes.maxLength = 1000;
                notes.value = row.ReviewerNotes;
                decision.addEventListener("change", () => {
                    row.Decision = decision.value;
                    if (row.Decision !== "Confirm person") {
                        row.PersonAlias = ""; person.value = "";
                        results.hidden = true;
                    }
                    person.disabled = row.Decision !== "Confirm person" ||
                        (bundle.schemaVersion === 3 && !named);
                    if (row.Decision === "") { excluded.checked = false; row.ExcludeFromGroup = "False"; }
                    markDirty(true); refresh();
                    refreshVisibility();
                });
                excluded.addEventListener("change", () => {
                    row.ExcludeFromGroup = excluded.checked ? "True" : "False";
                    markDirty(true); refresh();
                });
                notes.addEventListener("input", () => {
                    row.ReviewerNotes = notes.value; markDirty(true); refresh();
                });
                const personField = dom("div", "vfr-field");
                const personTitle = dom("label", "",
                    named ? "Known person — choose from all " +
                        active.people.people.length + " names"
                        :
                        bundle.schemaVersion === 3 ? "Known person (database unavailable)" :
                        "Private known-person alias (legacy map needed)");
                person.id = "vfr-person-" + crop.frame + "-" + crop.face;
                personTitle.htmlFor = person.id;
                personField.append(personTitle, picker);
                item.append(label("Human decision", decision), personField);
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
        refreshVisibility();
    }

    function init() {
        const input = document.getElementById("vfr-bundle");
        const csvInput = document.getElementById("vfr-csv");
        const mapInput = document.getElementById("vfr-people-map");
        if (!input || !csvInput || !mapInput || input.dataset.initialized === "true") return;
        input.dataset.initialized = "true";
        const status = document.getElementById("vfr-load-status");
        const peopleStatus = document.getElementById("vfr-people-status");
        input.addEventListener("change", async () => {
            const file = input.files && input.files[0];
            const request = ++generation;
            if (!file) return;
            try {
                if (dirty && !environment.confirm("Discard unsaved review changes?")) return;
                if (file.size < 1 || file.size > maxBundleSize) throw new Error("File too large or empty.");
                const bundle = JSON.parse(await file.text());
                if (request !== generation) return;
                if (!validateBundle(bundle)) throw new Error("Invalid or unsupported private evidence bundle.");
                active = {
                    bundle, rows: newRows(bundle), people: null,
                    showReviewed: false, manualHidden: new Set()
                };
                markDirty(false);
                const legacyMapLabel = document.getElementById("vfr-legacy-map-label");
                legacyMapLabel.hidden = bundle.schemaVersion !== 2;
                render();
                peopleStatus.textContent = bundle.schemaVersion === 3
                    ? "Loading current canonical people from the Photo Identity database..."
                    : bundle.schemaVersion === 2
                        ? "Legacy package: import the matching people map or regenerate v3."
                        : "Legacy alias-only package: regenerate v3 for database names.";
                status.textContent = "Private face evidence loaded locally; no identity write.";
                if (bundle.schemaVersion === 3) {
                    try {
                        const response = await environment.fetch("/api/review/people/maintenance", {
                            cache: "no-store", credentials: "same-origin"
                        });
                        if (request !== generation || !active || active.bundle !== bundle) return;
                        if (!response.ok) throw new Error("People API returned HTTP " + response.status);
                        const catalogue = await response.json();
                        if (request !== generation || !active || active.bundle !== bundle) return;
                        const current = resolveLivePeople(bundle, catalogue);
                        if (!current) throw new Error("Invalid canonical people response.");
                        active.people = current;
                        render();
                        const matched = current.people.filter(p => p.alias).length;
                        peopleStatus.textContent = current.people.length + " current canonical people" +
                            " loaded directly from the database; " + matched +
                            " match this evidence snapshot. Newer people require new evidence.";
                    } catch (error) {
                        if (request === generation && active && active.bundle === bundle) {
                            peopleStatus.textContent = "Current people unavailable: " +
                                error.message + ". Known-person confirmation is disabled.";
                        }
                    }
                }
            } catch (error) {
                status.textContent = "Import refused: " + error.message;
            } finally {
                input.value = "";
            }
        });
        mapInput.addEventListener("change", async () => {
            const file = mapInput.files && mapInput.files[0];
            const request = generation;
            if (!file) return;
            try {
                if (!active) throw new Error("Load the face evidence package first.");
                if (file.size < 1 || file.size > 1024 * 1024)
                    throw new Error("Empty or oversized people map.");
                const map = JSON.parse(await file.text());
                if (request !== generation || !active) return;
                if (!validatePeopleMap(active.bundle, map))
                    throw new Error("People map does not match this catalogue snapshot.");
                const aliases = new Set(map.people.map(p => p.alias));
                if (active.rows.some(row => row.Decision === "Confirm person" &&
                    !aliases.has(row.PersonAlias)))
                    throw new Error("Existing decisions contain people absent from this map.");
                active.people = map;
                render();
                peopleStatus.textContent = map.people.length +
                    " canonical people loaded from the matching private snapshot; names stay in browser memory.";
            } catch (error) {
                peopleStatus.textContent = "People map refused: " + error.message;
            } finally {
                mapInput.value = "";
            }
        });
        csvInput.addEventListener("change", async () => {
            const file = csvInput.files && csvInput.files[0];
            const request = generation;
            if (!file || !active) return;
            try {
                if (file.size < 1 || file.size > 256 * 1024)
                    throw new Error("Missing or oversized CSV.");
                const raw = await file.text();
                if (request !== generation || !active) return;
                const imported = importCsv(raw, active.bundle);
                if (active.people) {
                    const aliases = new Set(active.people.people.map(p => p.alias));
                    if (imported.some(row => row.Decision === "Confirm person" &&
                        !aliases.has(row.PersonAlias)))
                        throw new Error("CSV contains a person not in the selected private map.");
                }
                active.rows = imported;
                active.manualHidden.clear();
                active.showReviewed = false;
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
    const api = {validateBundle, validatePeopleMap, resolveLivePeople, isCompleted,
        searchPeople, describeSuggestion, pngDimensions, newRows, validateRows, metrics, csvEncode,
        csvParse, importCsv, init, dispose, hasUnsavedChanges};
    if (typeof module === "object" && module.exports) module.exports = api;
    else environment.photoIdentityVideoFaceReview = api;
})(typeof window === "object" ? window : {});
