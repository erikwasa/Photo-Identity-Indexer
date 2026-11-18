/* Source-free regression for the previously observed bad V06 merge-export
 * failure. Reject cycles, invalid destinations, cross-source merges and
 * contradictory human person labels before saving any private CSV.
 */
"use strict";
const assert=require("node:assert/strict");
const {valid}=require("./VideoFaceMergeChainValidation.js");
const row=(Group,Decision="",MergeInto="",SelectedPerson="")=>({
  Group,Decision,MergeInto,SelectedPerson
});
const good=[
  row("G001","Keep group","","P0001"),
  row("G002","Merge group","G001","P0001"),
  row("G003","Merge group","G002","P0001"),
  row("G004","Keep group","","P0002"),
  row("G005",""),
  row("G006","Cannot tell")
];
assert.equal(valid(good),true,"Unreviewed groups remain legal");
const change=(index,patch)=>{
  const clone=good.map(x=>({...x}));
  Object.assign(clone[index],patch);
  assert.equal(valid(clone),false,"Expected merge-chain rejection");
};
change(1,{MergeInto:"G999"});
change(1,{MergeInto:"G002"});
change(0,{Decision:"Merge group",MergeInto:"G002"});
change(0,{Decision:"Cannot tell"});
change(0,{Decision:"Dismiss false detection"});
change(0,{Decision:""});
change(2,{MergeInto:"G004"}); // X cannot merge into Y.
change(1,{SelectedPerson:"P0003"});
change(0,{MergeInto:"G002"}); // Stale target on kept root.
change(4,{Group:"G001"});
change(4,{Group:"D005"}); // Cross-source worksheet contamination.
assert.equal(valid([row("C001","Keep group"),row("C002","Merge group","C001")]),true);
assert.equal(valid([row("D001","Keep group"),row("D002","Merge group","D001")]),true);
assert.equal(valid([]),false);
assert.equal(valid(null),false);
const tooMany=Array.from({length:65},(_,i)=>row("G"+String(i+1).padStart(3,"0")));
assert.equal(valid(tooMany),false);
console.log("WI-0155 browser merge-chain validation synthetic tests: PASS");
