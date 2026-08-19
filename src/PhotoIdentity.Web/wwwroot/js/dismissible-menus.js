(() => {
    const menuSelector = '.advanced-menu, .collection-person-dropdown';

    const openMenus = () => document.querySelectorAll(`${menuSelector}[open]`);

    const closeMenu = (menu, restoreFocus = false) => {
        if (!(menu instanceof HTMLDetailsElement) || !menu.open) {
            return;
        }

        menu.open = false;
        if (restoreFocus) {
            const summary = menu.querySelector(':scope > summary');
            if (summary instanceof HTMLElement) {
                summary.focus();
            }
        }
    };

    const closeAll = (except = null) => {
        for (const menu of openMenus()) {
            if (menu !== except) {
                closeMenu(menu);
            }
        }
    };

    document.addEventListener('pointerdown', event => {
        for (const menu of openMenus()) {
            if (!menu.contains(event.target)) {
                closeMenu(menu);
            }
        }
    }, true);

    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') {
            return;
        }

        const focusedMenu = event.target instanceof Element
            ? event.target.closest(menuSelector)
            : null;
        const menu = focusedMenu instanceof HTMLDetailsElement && focusedMenu.open
            ? focusedMenu
            : openMenus()[0];
        if (menu) {
            event.preventDefault();
            closeMenu(menu, true);
        }
    }, true);

    document.addEventListener('click', event => {
        if (!(event.target instanceof Element)) {
            return;
        }

        const menu = event.target.closest(menuSelector);
        if (!(menu instanceof HTMLDetailsElement)) {
            return;
        }

        const action = event.target.closest('a[href], button, input[type="checkbox"], input[type="radio"]');
        if (!(action instanceof HTMLElement) || action.hasAttribute('disabled')) {
            return;
        }

        queueMicrotask(() => closeMenu(menu));
    });

    document.addEventListener('toggle', event => {
        const menu = event.target;
        if (menu instanceof HTMLDetailsElement && menu.matches(menuSelector) && menu.open) {
            closeAll(menu);
        }
    }, true);

    const closeForNavigation = () => closeAll();
    window.addEventListener('popstate', closeForNavigation);
    window.addEventListener('hashchange', closeForNavigation);

    for (const method of ['pushState', 'replaceState']) {
        const original = history[method];
        history[method] = function (...args) {
            const result = original.apply(this, args);
            closeForNavigation();
            return result;
        };
    }
})();
