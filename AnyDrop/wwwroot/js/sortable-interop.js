const sortables = new Map();

window.initSortable = (elementId, dotnetRef) => {
    const element = document.getElementById(elementId);
    if (!element || typeof Sortable === "undefined") {
        return;
    }

    if (sortables.has(elementId)) {
        sortables.get(elementId).destroy();
    }

    const sortable = Sortable.create(element, {
        animation: 150,
        onEnd: () => {
            const ids = Array.from(element.querySelectorAll("[data-id]"))
                .map((item) => item.getAttribute("data-id"))
                .filter(Boolean);
            dotnetRef.invokeMethodAsync("OnSortEnd", ids);
        }
    });

    sortables.set(elementId, sortable);
};

window.destroySortable = (elementId) => {
    const sortable = sortables.get(elementId);
    if (!sortable) {
        return;
    }

    sortable.destroy();
    sortables.delete(elementId);
};
