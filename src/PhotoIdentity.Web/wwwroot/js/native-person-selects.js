(() => {
    const favoritePrefix = '★ ';
    const favoriteSuffix = ' — Favorite';

    const normalizeOption = option => {
        if (!(option instanceof HTMLOptionElement)) {
            return;
        }

        const text = option.textContent?.trim() ?? '';
        if (!text.startsWith(favoritePrefix)) {
            return;
        }

        const displayName = text.slice(favoritePrefix.length).trim();
        option.textContent = displayName.length > 0
            ? `${displayName}${favoriteSuffix}`
            : text;
    };

    const normalizeWithin = root => {
        if (root instanceof HTMLOptionElement) {
            normalizeOption(root);
        }

        if (root instanceof Element || root instanceof Document) {
            root.querySelectorAll('select option').forEach(normalizeOption);
        }
    };

    normalizeWithin(document);

    const observer = new MutationObserver(records => {
        for (const record of records) {
            for (const node of record.addedNodes) {
                if (node instanceof Element) {
                    normalizeWithin(node);
                }
            }
        }
    });

    observer.observe(document.body, {
        childList: true,
        subtree: true,
    });
})();
