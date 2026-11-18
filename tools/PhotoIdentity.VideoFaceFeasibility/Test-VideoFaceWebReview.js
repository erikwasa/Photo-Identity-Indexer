/* Browser-only synthetic regression: no private images, real media or network. */
"use strict";
const assert = require("node:assert/strict");
const review = require("../../src/PhotoIdentity.Web/wwwroot/js/video-face-review.js");
const image = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9kELqY4AAAAASUVORK5CYII=";
const bundle = {
    schemaVersion: 1, mode: "PresenceFirst",
    clipAlias: "V04", reportSha256: "a".repeat(64),
    unassignedGroupObservations: 2,
    groups: [
        {alias: "G001", observations: 2, suggestions: ["P0001: 0.8000"]},
        {alias: "G002", observations: 1, suggestions: []},
        {alias: "G003", observations: 1, suggestions: []}
    ],
    crops: [
        {group: "G001", frame: "1", face: "1", imageBase64: image},
        {group: "G001", frame: "2", face: "1", imageBase64: image},
        {group: "G002", frame: "1", face: "2", imageBase64: image},
        {group: "G003", frame: "3", face: "1", imageBase64: image}
    ]
};
const copy = value => JSON.parse(JSON.stringify(value));
const badBundle = mutate => {
    const b = copy(bundle);
    mutate(b);
    assert.equal(review.validateBundle(b), false);
};
assert.equal(review.validateBundle(bundle), true);
badBundle(b => b.schemaVersion = 2);
badBundle(b => b.mode = "GroupReview");
badBundle(b => b.reportSha256 = "A".repeat(64));
badBundle(b => b.groups[0].observations = 1);
badBundle(b => b.groups.push(copy(b.groups[0])));
badBundle(b => b.crops[1] = copy(b.crops[0]));
badBundle(b => b.crops[1].group = "G002"); // Same frame + face cannot appear in two groups.
badBundle(b => b.crops[0].frame = "181");
badBundle(b => b.crops[0].imageBase64 = "javascript:alert(1)");
badBundle(b => b.crops[0].imageBase64 = image.repeat(20000));
badBundle(b => b.groups[0].suggestions = ["<img src=x onerror=alert(1)>"]);
badBundle(b => b.unassignedGroupObservations = -1);
let rows = review.newRows(bundle);
assert.equal(review.validateRows(rows, bundle), true);
rows[0].Decision = "Confirm person";
rows[0].PersonAlias = "P0001";
rows[1].Decision = "Confirm person";
rows[1].PersonAlias = "P0002";
rows[1].ExcludeFromGroup = "True";
rows[2].Decision = "Confirm person";
rows[2].PersonAlias = "P0001"; // Duplicate confirmed person from another group.
rows[3].ReviewerNotes = "Unreviewed, note with comma, \"quotes\" and\nnewlines.";
assert.equal(review.validateRows(rows, bundle), true);
let stats = review.metrics(rows, bundle);
assert.equal(stats.knownPeople, 2);
assert.equal(stats.confirmed, 3);
assert.equal(stats.duplicateConfirmations, 1);
assert.equal(stats.mixedGroups, 1);
assert.equal(stats.excluded, 1);
assert.equal(stats.unreviewed, 1);
assert.equal(stats.groupsWithoutReview, 1);
assert.equal(stats.unassignedObservations, 2);
assert.equal(stats.personCoverageProven, false);
assert.equal(stats.productionAssignmentsMade, 0);
const csv = review.csvEncode(rows);
const imported = review.importCsv(csv, bundle);
assert.deepEqual(imported, rows);
assert.equal(review.validateRows(imported, bundle), true);
const reorderedCsv = review.csvEncode([...rows].reverse());
assert.deepEqual(review.importCsv(reorderedCsv, bundle), rows);
function rejected(mutate) {
    const changed = copy(rows);
    mutate(changed);
    assert.equal(review.validateRows(changed, bundle), false);
}
rejected(r => r[0].Group = "G999");
rejected(r => r[0].ReportSha256 = "b".repeat(64));
rejected(r => r[0].Clip = "V05");
rejected(r => r[0].Decision = "Keep group");
rejected(r => r[0].PersonAlias = "P999");
rejected(r => r[0].PersonAlias = "");
rejected(r => r[3].ExcludeFromGroup = "True");
rejected(r => r[3].Frame = "180");
rejected(r => r[3] = copy(r[0]));
rejected(r => r.splice(0, 1));
rejected(r => r[3].ReviewerNotes = "x".repeat(1001));
rejected(r => { delete r[3].Decision; });
assert.throws(() => review.importCsv(csv.replace("a".repeat(64), "b".repeat(64)), bundle));
assert.throws(() => review.importCsv("Clip,Group\nX,Y\n", bundle));
assert.throws(() => review.csvParse('"value"x,next\n'));
assert.throws(() => review.csvParse('"unterminated'));
const legacy = csv.replace(/\r\n/g, "\n");
assert.deepEqual(review.importCsv(legacy, bundle), rows);
console.log("WI-0155 in-app private video-face review synthetic tests: PASS");
