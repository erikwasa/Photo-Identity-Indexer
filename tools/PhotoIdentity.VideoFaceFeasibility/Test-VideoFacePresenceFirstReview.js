/* Deterministic source-free tests for private presence-first crop review.
 * No HTTP, face images, database, browser or real person aliases.
 */
"use strict";
const assert=require("node:assert/strict");
const {columns,validate,measure}=require("./VideoFacePresenceFirstReview.js");
const clip="V04",hash="c".repeat(64);
const refs=[
  {Clip:clip,Group:"G001",Frame:"1",Face:"1"},
  {Clip:clip,Group:"G001",Frame:"1",Face:"2"},
  {Clip:clip,Group:"G002",Frame:"3",Face:"1"},
  {Clip:clip,Group:"G003",Frame:"4",Face:"1"},
  {Clip:clip,Group:"G004",Frame:"5",Face:"1"}
];
function row(ref,decision="",person="",exclude="False"){
  return {
    Clip:clip,ReportSha256:hash,Group:ref.Group,
    Frame:ref.Frame,Face:ref.Face,Decision:decision,
    PersonAlias:person,ExcludeFromGroup:exclude,ReviewerNotes:""
  };
}
const rows=[
  row(refs[0],"Confirm person","P0001"),
  row(refs[1],"Confirm person","P0002","True"), // Mixed group, no forced split/merge.
  row(refs[2],"Confirm person","P0001"),        // Duplicate person, not duplicated presence.
  row(refs[3],"Real face unknown"),              // Unknown cannot become a known person.
  row(refs[4])                                  // Explicitly left unreviewed.
];
const groups=["G001","G002","G003","G004"];
assert.equal(validate(rows,refs,clip,hash),true);
let m=measure(rows,groups,7);
assert.equal(m.KnownPersonPresenceProposals,2);
assert.equal(m.ConfirmedRepresentativeCrops,3);
assert.equal(m.MixedIdentityGroups,1);
assert.equal(m.ExcludedCropAnnotations,1);
assert.equal(m.UnknownFaceRepresentativeCrops,1);
assert.equal(m.UnreviewedRepresentativeCrops,1);
assert.equal(m.GroupsWithoutReviewedCrops,1);
assert.equal(m.UnassignedGroupObservations,7);
assert.equal(m.PersonCoverageProven,false);
assert.equal(m.ProductionAssignmentsMade,0);
function rejected(change){
  const changed=rows.map(x=>({...x}));
  change(changed);
  assert.equal(validate(changed,refs,clip,hash),false);
}
rejected(x=>x[0].PersonAlias="P999");        // Bad syntax.
rejected(x=>x[0].PersonAlias="UNKNOWN");     // UNKNOWN must remain unresolved.
rejected(x=>x[1].PersonAlias="");            // No known identity for confirmation.
rejected(x=>x[2].Decision="");               // Person alias cannot survive a cleared decision.
rejected(x=>x[4].ExcludeFromGroup="True");   // No silent group exclusion.
rejected(x=>x[4].Frame="180");              // Forged crop/frame.
rejected(x=>x[4].Face="32");                // Forged crop/face.
rejected(x=>x[4].Group="G999");             // Forged group.
rejected(x=>x[4].ReportSha256="d".repeat(64)); // Report mismatch.
rejected(x=>x[4].Clip="V05");               // Cross-video mutation.
rejected(x=>x[4].Decision="Keep group");    // Legacy group decision incompatible.
rejected(x=>x[4].ReviewerNotes="x".repeat(1001));
rejected(x=>x[4].ExcludeFromGroup="yes");
rejected(x=>{x[4]={...x[0]};});            // Duplicate source crop.
rejected(x=>{delete x[4].Decision;});      // Wrong worksheet shape.
assert.equal(validate(rows.slice(0,-1),refs,clip,hash),false);
assert.equal(validate(rows,refs.slice(0,-1),clip,hash),false);
assert.equal(validate(rows,refs,clip,"A".repeat(64)),false);
const reordered=[...rows].reverse();
assert.equal(validate(reordered,refs,clip,hash),true,"Source-bound import permits reordered rows.");
assert.deepEqual(columns,[
  "Clip","ReportSha256","Group","Frame","Face","Decision",
  "PersonAlias","ExcludeFromGroup","ReviewerNotes"
]);
console.log("WI-0155 private presence-first browser logic synthetic tests: PASS");
