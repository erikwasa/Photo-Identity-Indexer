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
badBundle(b => b.clipAlias = ["V04"]);
badBundle(b => b.groups[0].alias = ["G001"]);
badBundle(b => {
    const bytes = Buffer.from(image, "base64");
    bytes.writeUInt32BE(30000, 16); // Malicious PNG dimensions, not a bounded crop.
    b.crops[0].imageBase64 = bytes.toString("base64");
});
const signedRank = copy(bundle);
signedRank.groups[0].suggestions = ["P0001: -0.1234"];
assert.equal(review.validateBundle(signedRank), true);
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
// V2 package can resolve real names only against its exact private
// read-only catalogue snapshot. Legacy v1 packages remain supported for
// alias-only CSV review, but must not accept arbitrary names.
const namedBundle = copy(bundle);
namedBundle.schemaVersion = 2;
namedBundle.catalogueSnapshot = "10:15:12,14";
namedBundle.cataloguePeopleCount = 3;
for (const crop of namedBundle.crops) {
    crop.frameWidth = 1920;
    crop.frameHeight = 1080;
    crop.faceWidthPx = 35;
    crop.faceHeightPx = 46;
}
const privateMap = {
    schemaVersion: 1, pgSnapshot: "10:15:12,14",
    people: [
        {alias: "P0001", personId: "00000000-0000-0000-0000-000000000001", displayName: "Alice Example"},
        {alias: "P0002", personId: "00000000-0000-0000-0000-000000000002", displayName: "Álex Sample"},
        {alias: "P0003", personId: "00000000-0000-0000-0000-000000000003", displayName: null}
    ]
};
assert.equal(review.validateBundle(namedBundle), true);
assert.equal(review.validatePeopleMap(namedBundle, privateMap), true);
assert.equal(review.validatePeopleMap(bundle, privateMap), false);
assert.equal(review.pngDimensions(image).width, 1);
assert.equal(review.pngDimensions(image).height, 1);
assert.deepEqual(review.searchPeople(privateMap, "aLiCe").map(p => p.alias), ["P0001"]);
assert.deepEqual(review.searchPeople(privateMap, "P0002").map(p => p.alias), ["P0002"]);
assert.match(review.describeSuggestion("P0001: 0.8000", privateMap), /Alice Example/);
assert.equal(review.describeSuggestion("P0001: 0.8000", null), "P0001: 0.8000");
assert.equal(review.validateRows(rows, namedBundle), true);
assert.equal(review.importCsv(review.csvEncode(rows), namedBundle).length, 4);
assert.equal(review.csvEncode(rows).includes("Alice Example"), false);
const rejectedMap = mutate => {
    const clone = copy(privateMap);
    mutate(clone);
    assert.equal(review.validatePeopleMap(namedBundle, clone), false);
};
rejectedMap(x => x.pgSnapshot = "10:15:13");
rejectedMap(x => x.people.pop());
rejectedMap(x => x.people[1].alias = x.people[0].alias);
rejectedMap(x => x.people[1].personId = x.people[0].personId.toUpperCase());
rejectedMap(x => x.people[0].displayName = "<script>\n");
rejectedMap(x => x.people[0].displayName = " ");
rejectedMap(x => x.people[0].personId = "not-a-uuid");
const invalidGeometry = copy(namedBundle);
invalidGeometry.crops[0].faceWidthPx = 4000;
assert.equal(review.validateBundle(invalidGeometry), false);
const missingGeometry = copy(namedBundle);
delete missingGeometry.crops[0].frameWidth;
assert.equal(review.validateBundle(missingGeometry), false);
const badNamed = copy(namedBundle);
badNamed.cataloguePeopleCount = 0;
assert.equal(review.validateBundle(badNamed), false);
namedBundle.groups[0].suggestions = ["P0999: 0.9000"];
assert.equal(review.validatePeopleMap(namedBundle, privateMap), false);

// V3: database-derived canonical names and stable UUID binding; no name map
// upload to the web UI and no change to original alias-only CSV.
const liveBundle = copy(namedBundle);
liveBundle.schemaVersion = 3;
liveBundle.groups[0].suggestions = ["P0001: 0.8000"];
liveBundle.personBindings = privateMap.people.map(p =>
    ({alias: p.alias, personId: p.personId}));
liveBundle.crops[0].suggestions = ["P0002: 0.8300", "P0001: 0.8000"];
liveBundle.crops[1].suggestions = ["P0001: 0.7900"];
liveBundle.crops[2].suggestions = ["P0003: 0.8100"];
liveBundle.crops[3].suggestions = ["P0001: 0.7200"];
assert.equal(review.validateBundle(liveBundle), true);
const currentCatalogue = [
    {id: privateMap.people[0].personId, displayName: "Alice Renamed"},
    {id: privateMap.people[1].personId, displayName: "Álex Sample"},
    {id: privateMap.people[2].personId, displayName: "Unidentified"},
    {id: "00000000-0000-0000-0000-000000000004", displayName: "New Person"}
];
const current = review.resolveLivePeople(liveBundle, currentCatalogue);
assert.equal(current.people.length, 4);
assert.equal(current.people[0].displayName, "Alice Renamed");
assert.equal(current.people[0].alias, "P0001");
assert.equal(current.people[3].alias, null);
assert.deepEqual(review.searchPeople(current, "New Person").map(p => p.alias), [null]);
assert.match(review.describeSuggestion("P0001: 0.8000", current), /Alice Renamed/);
assert.equal(review.validateRows(rows, liveBundle), true);
assert.equal(review.importCsv(csv, liveBundle).length, 4);
assert.equal(review.csvEncode(rows).includes("Alice Renamed"), false);
const duplicateLive = copy(currentCatalogue);
duplicateLive[1].id = duplicateLive[0].id;
assert.equal(review.resolveLivePeople(liveBundle, duplicateLive), null);
const missingFaceRank = copy(liveBundle);
delete missingFaceRank.crops[0].suggestions;
assert.equal(review.validateBundle(missingFaceRank), false);
const invalidFaceRank = copy(liveBundle);
invalidFaceRank.crops[0].suggestions = ["P0999: 0.9900"];
assert.equal(review.validateBundle(invalidFaceRank), false);
const unknownBinding = copy(liveBundle);
unknownBinding.personBindings[1].alias = unknownBinding.personBindings[0].alias;
assert.equal(review.validateBundle(unknownBinding), false);
const unknownLead = copy(liveBundle);
unknownLead.groups[0].suggestions = ["P0999: 0.9000"];
assert.equal(review.validateBundle(unknownLead), false);
const unknownRow = copy(rows);
unknownRow[0].PersonAlias = "P0999";
assert.equal(review.validateRows(unknownRow, liveBundle), false);
assert.equal(review.isCompleted({Decision: "Confirm person", PersonAlias: ""}), false);
assert.equal(review.isCompleted({Decision: "Confirm person", PersonAlias: "P0001"}), true);
assert.equal(review.isCompleted({Decision: "Cannot tell", PersonAlias: ""}), true);
assert.equal(review.isCompleted({Decision: "", PersonAlias: ""}), false);

console.log("WI-0155 in-app private video-face review synthetic tests: PASS");
