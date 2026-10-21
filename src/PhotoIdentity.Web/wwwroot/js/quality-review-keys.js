let handler = null;
let callback = null;

export function attach(dotNetReference) {
  detach();
  callback = dotNetReference;
  handler = (event) => {
    if (event.altKey || event.ctrlKey || event.metaKey || event.repeat) return;
    if (!["1", "2", "3", "ArrowLeft", "ArrowRight"].includes(event.key)) return;
    const active = event.target;
    if (active?.closest?.("input,select,textarea,[contenteditable=true],[role=dialog]")) return;
    if (active?.closest?.(".quality-confirm")) return;
    event.preventDefault();
    // A pending confirmation is additionally checked in the .NET handler.
    void callback.invokeMethodAsync("OnReviewKeyAsync", event.key).catch(() => {});
  };
  document.addEventListener("keydown", handler);
}

export function detach() {
  if (handler) document.removeEventListener("keydown", handler);
  handler = null;
  callback = null;
}
