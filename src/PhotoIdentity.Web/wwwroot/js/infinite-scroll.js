export function observe(element, dotNetReference) {
    let disposed = false;
    const observer = new IntersectionObserver(entries => {
        if (disposed || !entries.some(entry => entry.isIntersecting)) {
            return;
        }

        observer.disconnect();
        dotNetReference.invokeMethodAsync("OnIntersectingAsync");
    }, {
        root: null,
        rootMargin: "700px 0px",
        threshold: 0
    });

    observer.observe(element);

    return {
        dispose: () => {
            disposed = true;
            observer.disconnect();
        }
    };
}

export function scrollIntoViewById(id) {
    const element = document.getElementById(id);
    if (!element) {
        return false;
    }

    element.scrollIntoView({ block: "center", inline: "nearest" });
    return true;
}

export function elementTopById(id) {
    const element = document.getElementById(id);
    return element ? element.getBoundingClientRect().top : null;
}

export function restoreElementTopById(id, previousTop) {
    const element = document.getElementById(id);
    if (!element || typeof previousTop !== "number") {
        return false;
    }

    const currentTop = element.getBoundingClientRect().top;
    window.scrollBy(0, currentTop - previousTop);
    return true;
}
