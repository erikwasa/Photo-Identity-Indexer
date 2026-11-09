// Smart Collection photo shortcuts are active only when the photo navigator
// offers a usable previous/next destination. Editable controls keep arrow keys.
let installed = false;

function handleArrowKey(event) {
    if (event.defaultPrevented || event.altKey || event.ctrlKey ||
        event.metaKey || event.shiftKey || event.isComposing) {
        return;
    }

    const direction = event.key === "ArrowLeft"
        ? "prev"
        : event.key === "ArrowRight"
            ? "next"
            : null;
    if (direction === null) {
        return;
    }

    const active = document.activeElement;
    if (active?.closest('input, textarea, select, [contenteditable], [contenteditable="true"], [role="textbox"]')) {
        return;
    }

    const target = document.querySelector(
        '.smart-photo-navigator a[rel="' + direction + '"]');
    if (!target) {
        return;
    }

    event.preventDefault();
    target.click();
}

export function installSmartCollectionArrows() {
    if (!installed) {
        window.addEventListener("keydown", handleArrowKey);
        installed = true;
    }
}

export function uninstallSmartCollectionArrows() {
    if (installed) {
        window.removeEventListener("keydown", handleArrowKey);
        installed = false;
    }
}
