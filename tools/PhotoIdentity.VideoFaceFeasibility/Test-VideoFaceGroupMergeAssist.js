/* Offline, synthetic, dependency-free CI tests for WI-0155 group-merge UI.
 * Exercises only transient browser controls. No real videos or identities.
 */
"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");

const script = fs.readFileSync(
  path.join(__dirname, "VideoFaceGroupMergeAssist.js"), "utf8");

class Element {
  constructor(tag = "div", text = "") {
    this.tag = tag; this.textContent = text; this.className = "";
    this.children = []; this.parent = null; this.listeners = {};
    this.dataset = {}; this.controls = {}; this.value = "";
    this.disabled = false; this.checked = false; this.hidden = false;
  }
  appendChild(node) {
    node.parent = this; this.children.push(node); return node;
  }
  replaceChildren(...children) {
    this.children = [];
    for (const child of children) this.appendChild(child);
  }
  addEventListener(type, callback) {
    (this.listeners[type] ||= []).push(callback);
  }
  dispatchEvent(event) {
    for (const callback of this.listeners[event.type] || []) callback(event);
    if (event.bubbles && this.parent) this.parent.dispatchEvent(event);
    return true;
  }
  querySelector(selector) { return this.controls[selector] || null; }
  querySelectorAll(selector) { return this.controls[selector] || []; }
  cloneNode() { const result = new Element(this.tag); result.src = this.src; return result; }
}
class Event {
  constructor(type, options = {}) { this.type = type; this.bubbles = !!options.bubbles; }
}
function descendants(node, match) {
  const result = [];
  for (const child of node.children) {
    if (match(child)) result.push(child);
    result.push(...descendants(child, match));
  }
  return result;
}
function group(id, observations, ranks, frames, selected = "", decision = "", fullFrames = null) {
  const card = new Element("article");
  card.dataset = {
    group: id, observations: String(observations),
    rankAliases: ranks.join(","), sampledFrames: frames.join(",")
  };
  if (fullFrames !== null) card.dataset.allMemberFrames = fullFrames.join(",");
  const control = (selector, value) => {
    const element = new Element("input");
    element.value = value;
    element.parent = card;
    card.controls[selector] = element;
    return element;
  };
  control(".decision", decision);
  control(".merge-target", "");
  control(".person", selected);
  const pictures = [new Element("img"), new Element("img")];
  card.controls[".photos img"] = pictures;
  return card;
}
function prepare(groups) {
  const byId = Object.fromEntries([
    ["ws-merge-assist",new Element("section")],
    ["ws-merge-proposals",new Element("div")],
    ["ws-merge-summary",new Element("p")],
  ]);
  const fakeDocument = {
    getElementById: id => byId[id],
    querySelectorAll: selector => selector === ".group-card" ? groups : [],
    createElement: tag => new Element(tag),
    createTextNode: value => new Element("#text", String(value))
  };
  vm.runInNewContext(script, { document: fakeDocument, Event });
  return { ...byId, groups, proposals: byId["ws-merge-proposals"],
 };
}
function pairs(state) {
  return descendants(state.proposals, node => node.className === "ws-merge-pair");
}
function confirmPair(pair) {
  const inputs = descendants(pair, x => x.tag === "input");
  const buttons = descendants(pair, x => x.tag === "button");
  assert.equal(inputs.length, 1);
  assert.equal(buttons.length, 1);
  assert(buttons[0].disabled, "Explicit human confirmation required");
  inputs[0].checked = true;
  inputs[0].dispatchEvent(new Event("change"));
  assert(!buttons[0].disabled);
  buttons[0].dispatchEvent(new Event("click"));
}
{
  const a = group("C001", 2, ["P0001"], ["1"], "P0001");
  const b = group("C002", 2, ["P0001"], ["2"], "P0002");
  const state = prepare([a,b]);
  assert.equal(pairs(state).length,0,"Different selected people never merge");
  assert.equal(b.querySelector(".decision").value,"");
}
{
  const a = group("C001", 2, ["P0001"], ["1"]);
  const b = group("C002", 2, ["P0001"], ["1"]);
  const state = prepare([a,b]);
  assert.equal(pairs(state).length,0,"Same-frame overlap vetoes even a suggested pair");
}
{
  const a = group("C001", 2, ["P0001"], ["1"]);
  const b = group("C002", 2, ["P0001"], ["2"]);
  const state = prepare([a,b]);
  assert.equal(pairs(state).length,1,"Shared rank yields only a human review lead");
  assert.equal(b.querySelector(".decision").value,"","No automatic merge");
  confirmPair(pairs(state)[0]);
  assert.equal(a.querySelector(".decision").value,"Keep group");
  assert.equal(b.querySelector(".decision").value,"Merge group");
  assert.equal(b.querySelector(".merge-target").value,"C001");
  assert.equal(a.querySelector(".person").value,"","No automatic identity assignment");
  assert.equal(pairs(state).length,0,"Completed pair no longer proposed");
}
{
  const a = group("C001", 2, ["P0001"], ["1"]);
  const b = group("C002", 2, ["P0002"], ["2"]);
  const state = prepare([a,b]);
  assert.equal(pairs(state).length,0,"No rank/label overlap means no speculative lead");
}
{
  const a = group("C001", 2, ["P0001"], ["1"],"P0001","",["1","9"]);
  const b = group("C002", 2, ["P0001"], ["2"],"P0001","",["2","9"]);
  const state = prepare([a,b]);
  assert.equal(pairs(state).length,0,"Full-member collision blocks pairwise merge");
}
{
  const a = group("C001", 2, ["P0001"], ["1"],"P0001","",["1"]);
  const b = group("C002", 2, ["P0001"], ["2"],"P0001","",["2","10"]);
  const state = prepare([a,b]);
  assert.equal(pairs(state).length,0,"Malformed full membership is not trusted");
}
{
  const a = group("C001", 2, ["P0001"], ["1"]);
  const b = group("C002", 2, ["P0001"], ["2"]);
  const state = prepare([a,b]);
  assert.match(state["ws-merge-summary"].textContent,/Legacy report/);
  assert.equal(state["ws-merge-batch-apply"],undefined,
    "Rejected batch merge controls must remain absent");
}
console.log("WI-0155 synthetic optional pairwise merge assistant: PASS");
