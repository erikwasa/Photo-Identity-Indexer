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
    ["ws-merge-batch-count",new Element("p")],
    ["ws-merge-batch-confirm",new Element("input")],
    ["ws-merge-batch-apply",new Element("button")]
  ]);
  const fakeDocument = {
    getElementById: id => byId[id],
    querySelectorAll: selector => selector === ".group-card" ? groups : [],
    createElement: tag => new Element(tag),
    createTextNode: value => new Element("#text", String(value))
  };
  vm.runInNewContext(script, { document: fakeDocument, Event });
  return { ...byId, groups, proposals: byId["ws-merge-proposals"],
    batch: byId["ws-merge-batch-apply"],
    tick: byId["ws-merge-batch-confirm"] };
}
function pairs(state) {
  return descendants(state.proposals, x => x.className === "ws-merge-pair");
}
function clickConfirmedPair(pair) {
  const tick = descendants(pair, x => x.tag === "input")[0];
  const button = descendants(pair, x => x.tag === "button")[0];
  assert(button.disabled);
  tick.checked = true; tick.dispatchEvent(new Event("change"));
  assert(!button.disabled);
  button.dispatchEvent(new Event("click"));
}
function input(card, selector, value) {
  const element = card.querySelector(selector);
  element.value = value;
  element.dispatchEvent(new Event("input", { bubbles: true }));
}

{
  const a = group("C001", 4, ["P0001","P0002"], ["1"], "P0001");
  const b = group("C002", 2, ["P0001","P0003"], ["2"], "P0002");
  const state = prepare([a,b]);
  assert.equal(pairs(state).length, 0, "Never suggest conflicting selected people");
  assert.equal(b.querySelector(".decision").value, "");
}
{
  const a = group("C001", 4, ["P0001"], ["1"], "P0001");
  const b = group("C002", 2, ["P0001"], ["1"], "P0001");
  const state = prepare([a,b]);
  assert.equal(pairs(state).length, 0, "Representative same-frame collision must block leads");
  assert.match(state["ws-merge-batch-count"].textContent, /1 label set/);
  assert.equal(state.batch.disabled, true);
}
{
  const a = group("C001", 4, ["P0001","P0002"], ["1"]);
  const b = group("C002", 2, ["P0001","P0003"], ["2"]);
  const state = prepare([a,b]);
  assert.equal(pairs(state).length, 1);
  assert.equal(b.querySelector(".decision").value, "", "No inference-based merge allowed");
  clickConfirmedPair(pairs(state)[0]);
  assert.equal(a.querySelector(".decision").value, "Keep group");
  assert.equal(b.querySelector(".decision").value, "Merge group");
  assert.equal(b.querySelector(".merge-target").value, "C001");
  assert.equal(a.querySelector(".person").value, "", "No automatic identity assignment");
  assert.equal(b.querySelector(".person").value, "");
  assert.equal(pairs(state).length, 0, "Applied pair disappears");
}
{
  const a = group("C001", 5, ["P0001"], ["1"], "P0001", "Keep group");
  const b = group("C002", 2, ["P0003"], ["2"], "P0001", "Keep group");
  const c = group("C003", 3, ["P0004"], ["3"], "P0001");
  const d = group("C004", 4, ["P0001"], ["4"], "P0002");
  const e = group("C005", 1, [], ["5"], "UNKNOWN");
  const state = prepare([a,b,c,d,e]);
  assert.match(state["ws-merge-batch-count"].textContent, /2 possible merges/);
  assert(state.batch.disabled, "Batch needs an explicit human acknowledgement");
  state.tick.checked = true;
  state.tick.dispatchEvent(new Event("change"));
  assert(!state.batch.disabled);
  state.batch.dispatchEvent(new Event("click"));
  assert.equal(b.querySelector(".merge-target").value, "C001");
  assert.equal(c.querySelector(".merge-target").value, "C001");
  assert.equal(b.querySelector(".decision").value, "Merge group");
  assert.equal(c.querySelector(".decision").value, "Merge group");
  assert.equal(d.querySelector(".decision").value, "");
  assert.equal(e.querySelector(".decision").value, "");
  assert.equal(b.querySelector(".person").value, "P0001");
  assert.equal(c.querySelector(".person").value, "P0001");
  assert(state.batch.disabled, "Batch acknowledgement does not persist");
  assert.equal(state.tick.checked, false);
}
{
  const a = group("C001", 4, ["P0001"], ["1"], "P0001");
  const b = group("C002", 3, ["P0001"], ["2"], "P0001");
  const state = prepare([a,b]);
  state.tick.checked = true;
  state.tick.dispatchEvent(new Event("change"));
  assert(!state.batch.disabled);
  input(b, ".person", "P0002");
  assert(state.batch.disabled, "Editing a person alias invalidates acknowledgement");
  assert.equal(state.tick.checked, false);
  assert.equal(pairs(state).length, 0, "Different reviewed people cannot be merged");
}
{
  // Representative subsets do not collide, but a hidden full member does.
  const a = group("C001", 2, ["P0001"], ["1"], "P0001", "", ["1","9"]);
  const b = group("C002", 2, ["P0001"], ["2"], "P0001", "", ["2","9"]);
  const state = prepare([a, b]);
  assert.equal(pairs(state).length, 0, "Hidden same-frame overlap must block suggested merges");
  assert.match(state["ws-merge-batch-count"].textContent, /withheld due to overlapping source frames/);
  assert.equal(state.batch.disabled, true);
}
{
  // All true members are disjoint and a human still must confirm.
  const a = group("C001", 2, ["P0001"], ["1"], "P0001", "", ["1","9"]);
  const b = group("C002", 2, ["P0001"], ["2"], "P0001", "", ["2","10"]);
  const state = prepare([a, b]);
  assert.match(state["ws-merge-summary"].textContent, /Full member-frame evidence available/);
  assert.equal(pairs(state).length, 1);
  clickConfirmedPair(pairs(state)[0]);
  assert.equal(b.querySelector(".merge-target").value, "C001");
}
{
  // Malformed or incomplete membership must never pass through an
  // apparently safe data attribute, even when representative crops differ.
  const a = group("C001", 2, ["P0001"], ["1"], "P0001", "", ["1"]);
  const b = group("C002", 2, ["P0001"], ["2"], "P0001", "", ["2","10"]);
  const state = prepare([a, b]);
  assert.equal(pairs(state).length, 0, "Incomplete full membership must block UI proposals");
  assert.equal(state.batch.disabled, true);
}
{
  // Historic report remains usable for human review, but it cannot claim
  // the stronger full-membership safety proof.
  const a = group("C001", 2, ["P0001"], ["1"]);
  const b = group("C002", 2, ["P0001"], ["2"]);
  const state = prepare([a, b]);
  assert.match(state["ws-merge-summary"].textContent, /Legacy report/);
}
console.log("WI-0155 synthetic reviewed merge assistant: PASS");
