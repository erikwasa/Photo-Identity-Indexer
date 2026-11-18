/* WI-0155 opt-in, pairwise human-confirmed merge assistance for the private C-group
 * workspace. Catalogue rank overlap is a review lead, NOT an identity or a
 * permission to merge. No automatic merging, background suppression, storage,
 * network, source or production identity writes.
 */
(() => {
  "use strict";
  const panel = document.getElementById("ws-merge-assist");
  if (!panel) return;
  const cards = [...document.querySelectorAll(".group-card")];
  const proposals = document.getElementById("ws-merge-proposals");
  const summary = document.getElementById("ws-merge-summary");
  const pairLimit = 12;
  const knownAlias = /^P(?:[0-9]{2}|[0-9]{4})$/;
  const groupAlias = /^C(?!000)[0-9]{3}$/;
  const byId = new Map(cards.map(card => [card.dataset.group, card]));
  if (byId.size !== cards.length || cards.length > 64 ||
      cards.some(card => !groupAlias.test(card.dataset.group))) {
    panel.hidden = true;
    return;
  }
  const escapeFreeText = value => String(value ?? "").trim().toUpperCase();
  function snapshot(card) {
    const ranks = (card.dataset.rankAliases || "").split(",")
      .filter(value => knownAlias.test(value)).slice(0, 3);
    const frames = (card.dataset.sampledFrames || "").split(",")
      .filter(value => /^[1-9]\d{0,2}$/.test(value));
    const hasFull = Object.prototype.hasOwnProperty.call(
      card.dataset, "allMemberFrames");
    const complete = hasFull ?
      card.dataset.allMemberFrames.split(",") : [];
    // The source gallery validates this attribute against the private
    // report; malformed UI data must not establish non-overlap.
    const validComplete = !hasFull || (
      complete.length === Number(card.dataset.observations) &&
      complete.every((v, i) => /^[1-9]\d{0,2}$/.test(v) &&
        Number(v) <= 180 &&
        (i === 0 || Number(v) > Number(complete[i - 1]))) &&
      frames.every(v => complete.includes(v)));
    const decision = card.querySelector(".decision").value;
    const selected = escapeFreeText(card.querySelector(".person").value);
    return {
      id: card.dataset.group, card, ranks,
      frames: new Set(hasFull ? complete : frames),
      fullMemberEvidence: hasFull && validComplete,
      frameEvidenceValid: validComplete,
      observations: Number(card.dataset.observations),
      selected, decision
    };
  }
  const eligible = group => group.frameEvidenceValid &&
    (group.decision === "" || group.decision === "Keep group");
  const sharesSourceFrame = (a, b) =>
    [...a.frames].some(frame => b.frames.has(frame));
  function pairAllowed(a, b) {
    if (!eligible(a) || !eligible(b) ||
        sharesSourceFrame(a, b)) return false;
    // Explicit different known labels must never be overridden by a hint.
    if (knownAlias.test(a.selected) && knownAlias.test(b.selected) &&
        a.selected !== b.selected) return false;
    if (a.selected && b.selected && a.selected !== b.selected) return false;
    return true;
  }
  function chooseKeeper(a, b) {
    if (a.decision === "Keep group" && b.decision !== "Keep group") return [a, b];
    if (b.decision === "Keep group" && a.decision !== "Keep group") return [b, a];
    if (a.observations !== b.observations) {
      return a.observations > b.observations ? [a, b] : [b, a];
    }
    return a.id.localeCompare(b.id) <= 0 ? [a, b] : [b, a];
  }
  function commonRank(a, b) {
    let max = -1;
    let clue = "";
    for (let i = 0; i < a.ranks.length; i++) {
      const j = b.ranks.indexOf(a.ranks[i]);
      if (j < 0) continue;
      const weight = 6 - i - j; // rank overlap only; not calibrated similarity
      if (weight > max) { max = weight; clue = a.ranks[i]; }
    }
    return { weight: max, clue };
  }
  function candidates(items) {
    const pairs = [];
    for (let i = 0; i < items.length; i++) {
      for (let j = i + 1; j < items.length; j++) {
        const a = items[i], b = items[j];
        if (!pairAllowed(a, b)) continue;
        const common = commonRank(a, b);
        const sameConfirmed = knownAlias.test(a.selected) &&
          a.selected === b.selected;
        if (!sameConfirmed && common.weight < 0) continue;
        const [keeper, source] = chooseKeeper(a, b);
        pairs.push({ keeper, source, sameConfirmed, common,
          weight: (sameConfirmed ? 100 : 0) + Math.max(0, common.weight) });
      }
    }
    pairs.sort((a, b) => b.weight - a.weight ||
      b.keeper.observations + b.source.observations -
        a.keeper.observations - a.source.observations ||
      a.keeper.id.localeCompare(b.keeper.id) ||
      a.source.id.localeCompare(b.source.id));
    return pairs;
  }
  function addText(tag, content, className) {
    const node = document.createElement(tag);
    node.textContent = content;
    if (className) node.className = className;
    return node;
  }
  function pictures(parent, item) {
    const group = addText("div", "", "ws-merge-preview");
    group.appendChild(addText("strong", item.id + " · " +
      item.observations + " detections"));
    const imgs = addText("div", "", "ws-merge-images");
    for (const image of [...item.card.querySelectorAll(".photos img")].slice(0, 3)) {
      imgs.appendChild(image.cloneNode(false));
    }
    group.appendChild(imgs);
    parent.appendChild(group);
  }
  function dispatch(element) {
    element.dispatchEvent(new Event("input", { bubbles: true }));
  }
  function perform(keeperId, sourceId) {
    const keeper = byId.get(keeperId), source = byId.get(sourceId);
    if (!keeper || !source || keeper === source) return false;
    const a = snapshot(keeper), b = snapshot(source);
    if (!pairAllowed(a, b)) return false;
    // A suggested ranking is never enough. This is invoked only by the
    // human's explicitly confirmed button click.
    const keeperDecision = keeper.querySelector(".decision");
    const sourceDecision = source.querySelector(".decision");
    const sourceTarget = source.querySelector(".merge-target");
    keeperDecision.value = "Keep group";
    sourceDecision.value = "Merge group";
    sourceTarget.value = keeperId;
    dispatch(keeperDecision);
    dispatch(sourceTarget);
    return true;
  }
  function showPair(pair) {
    const outer = addText("article", "", "ws-merge-pair");
    const title = pair.sameConfirmed ?
      "Reviewer-labelled same person: " + pair.keeper.selected :
      "Shared catalogue review lead: " + pair.common.clue;
    outer.appendChild(addText("p", title + " (not verified by the model)"));
    const previews = addText("div", "", "ws-merge-pictures");
    pictures(previews, pair.keeper);
    pictures(previews, pair.source);
    outer.appendChild(previews);
    const tickLabel = addText("label", "", "ws-merge-checkbox");
    const tick = document.createElement("input");
    tick.type = "checkbox";
    tickLabel.appendChild(tick);
    tickLabel.appendChild(document.createTextNode(
      " I inspected both groups and confirm the same person; no conflicting simultaneous faces"));
    outer.appendChild(tickLabel);
    const button = addText("button",
      "Merge " + pair.source.id + " into " + pair.keeper.id);
    button.type = "button";
    button.disabled = true;
    tick.addEventListener("change", () => { button.disabled = !tick.checked; });
    button.addEventListener("click", () => {
      if (!tick.checked) return;
      if (!perform(pair.keeper.id, pair.source.id)) {
        summary.textContent = "Pair changed or became unsafe; no merge made.";
        refresh();
      }
      // The original C-group and combined CSV import/save stay authoritative.
      refresh();
    });
    outer.appendChild(button);
    return outer;
  }
  let refreshing = false;
  function refresh() {
    if (refreshing) return;
    refreshing = true;
    try {
      const items = cards.map(snapshot);
      const pairs = candidates(items);
      proposals.replaceChildren();
      for (const pair of pairs.slice(0, pairLimit)) {
        proposals.appendChild(showPair(pair));
      }
      const legacy = items.some(item => !item.fullMemberEvidence);
      summary.textContent = pairs.length + " rank/label-overlap pairs to inspect; " +
        Math.min(pairLimit, pairs.length) + " displayed. Other combinations " +
        "remain available in the original manual group controls." +
        (legacy ? " Legacy report: representative frames only; manual merges " +
          "cannot be certified as full-frame-safe for presence export." :
          " Full member-frame evidence available; all source frames checked.");
      if (!pairs.length) {
        proposals.appendChild(addText("p",
          "No safe shortlist from current evidence. Use manual review; do not infer a face is absent."));
      }
    } finally {
      refreshing = false;
    }
  }
  for (const card of cards) {
    card.addEventListener("input", refresh);
    card.addEventListener("change", refresh);
  }
  refresh();
})();
