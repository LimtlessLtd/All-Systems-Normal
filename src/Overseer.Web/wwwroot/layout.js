window.overseerLayout = (() => {
    const storageKey = "all-systems-normal.workspace-layout.v1";
    const desktopQuery = window.matchMedia("(min-width: 1181px)");
    const roots = new Set();
    const limits = {
        systems: { min: 165, max: 360 },
        inspector: { min: 250, max: 470 },
        events: { min: 140, max: 700 },
        map: { min: 360, max: 900 },
        mapMinimumWidth: 480,
        splitterWidth: 24
    };

    let resizeListenerInstalled = false;

    function clamp(value, min, max) {
        return Math.min(Math.max(value, min), max);
    }

    function readStored() {
        try {
            const value = localStorage.getItem(storageKey);
            if (!value) return {};
            const parsed = JSON.parse(value);
            return parsed && typeof parsed === "object" ? parsed : {};
        } catch {
            return {};
        }
    }

    function writeStored(state) {
        try {
            localStorage.setItem(storageKey, JSON.stringify({
                systemsWidth: Math.round(state.systemsWidth),
                inspectorWidth: Math.round(state.inspectorWidth),
                eventHeight: Math.round(state.eventHeight),
                mapHeight: Math.round(state.mapHeight)
            }));
        } catch {
            // Storage is an enhancement; layout must still work when blocked.
        }
    }

    function defaultState() {
        return {
            systemsWidth: 220,
            inspectorWidth: 320,
            eventHeight: 220,
            mapHeight: clamp(Math.round(window.innerHeight * 0.62), 460, 820)
        };
    }

    function currentState(root) {
        const systems = root.querySelector(".systems-panel");
        const inspector = root.querySelector(".inspector-panel");
        const events = root.querySelector(".event-panel");
        const map = root.querySelector(".station-map-viewport");
        const defaults = defaultState();

        return {
            systemsWidth: systems?.getBoundingClientRect().width || defaults.systemsWidth,
            inspectorWidth: inspector?.getBoundingClientRect().width || defaults.inspectorWidth,
            eventHeight: events?.getBoundingClientRect().height || defaults.eventHeight,
            mapHeight: map?.getBoundingClientRect().height || defaults.mapHeight
        };
    }

    function dynamicSideMaximum(root, side, state) {
        const grid = root.querySelector(".operations-grid");
        if (!grid) return limits[side].max;

        const otherWidth = side === "systems" ? state.inspectorWidth : state.systemsWidth;
        const available = grid.getBoundingClientRect().width
            - otherWidth
            - limits.mapMinimumWidth
            - limits.splitterWidth;

        return Math.max(limits[side].min, Math.min(limits[side].max, available));
    }

    function normalize(root, state) {
        const normalized = { ...state };

        normalized.systemsWidth = clamp(
            Number(normalized.systemsWidth) || 220,
            limits.systems.min,
            limits.systems.max);

        normalized.inspectorWidth = clamp(
            Number(normalized.inspectorWidth) || 320,
            limits.inspector.min,
            limits.inspector.max);

        normalized.systemsWidth = clamp(
            normalized.systemsWidth,
            limits.systems.min,
            dynamicSideMaximum(root, "systems", normalized));

        normalized.inspectorWidth = clamp(
            normalized.inspectorWidth,
            limits.inspector.min,
            dynamicSideMaximum(root, "inspector", normalized));

        const viewportEventMax = Math.max(limits.events.min, Math.min(limits.events.max, Math.floor(window.innerHeight * 0.58)));
        const viewportMapMax = Math.max(limits.map.min, Math.min(limits.map.max, Math.floor(window.innerHeight * 0.86)));

        normalized.eventHeight = clamp(Number(normalized.eventHeight) || 220, limits.events.min, viewportEventMax);
        normalized.mapHeight = clamp(Number(normalized.mapHeight) || defaultState().mapHeight, limits.map.min, viewportMapMax);

        return normalized;
    }

    function updateAria(root, state) {
        const values = {
            systems: state.systemsWidth,
            inspector: state.inspectorWidth,
            events: state.eventHeight,
            map: state.mapHeight
        };

        root.querySelectorAll("[data-splitter]").forEach(splitter => {
            const kind = splitter.dataset.splitter;
            if (values[kind] != null) {
                splitter.setAttribute("aria-valuenow", String(Math.round(values[kind])));
                splitter.setAttribute("aria-valuetext", `${Math.round(values[kind])} pixels`);
            }
        });
    }

    function apply(root, state, persist = false) {
        if (!root?.isConnected) return state;

        const normalized = normalize(root, state);
        root.style.setProperty("--systems-width", `${normalized.systemsWidth}px`);
        root.style.setProperty("--inspector-width", `${normalized.inspectorWidth}px`);
        root.style.setProperty("--event-height", `${normalized.eventHeight}px`);
        root.style.setProperty("--map-height", `${normalized.mapHeight}px`);
        updateAria(root, normalized);

        if (persist) writeStored(normalized);
        return normalized;
    }

    function reset(root, kind = null) {
        const defaults = defaultState();
        const state = currentState(root);

        if (!kind) {
            apply(root, defaults, true);
            return;
        }

        if (kind === "systems") state.systemsWidth = defaults.systemsWidth;
        if (kind === "inspector") state.inspectorWidth = defaults.inspectorWidth;
        if (kind === "events") state.eventHeight = defaults.eventHeight;
        if (kind === "map") state.mapHeight = defaults.mapHeight;

        apply(root, state, true);
    }

    function cursorFor(kind) {
        return kind === "systems" || kind === "inspector" ? "col-resize" : "row-resize";
    }

    function beginDrag(root, splitter, event) {
        if (!desktopQuery.matches || event.button !== 0) return;

        event.preventDefault();
        const kind = splitter.dataset.splitter;
        const start = currentState(root);
        const startX = event.clientX;
        const startY = event.clientY;
        const oldCursor = document.body.style.cursor;
        const oldUserSelect = document.body.style.userSelect;

        splitter.classList.add("is-active");
        root.classList.add("is-resizing");
        document.body.style.cursor = cursorFor(kind);
        document.body.style.userSelect = "none";

        const move = moveEvent => {
            const next = { ...start };
            const deltaX = moveEvent.clientX - startX;
            const deltaY = moveEvent.clientY - startY;

            if (kind === "systems") next.systemsWidth = start.systemsWidth + deltaX;
            if (kind === "inspector") next.inspectorWidth = start.inspectorWidth - deltaX;
            if (kind === "events") next.eventHeight = start.eventHeight - deltaY;
            if (kind === "map") next.mapHeight = start.mapHeight + deltaY;

            apply(root, next, false);
        };

        const end = () => {
            window.removeEventListener("pointermove", move);
            window.removeEventListener("pointerup", end);
            window.removeEventListener("pointercancel", end);
            splitter.classList.remove("is-active");
            root.classList.remove("is-resizing");
            document.body.style.cursor = oldCursor;
            document.body.style.userSelect = oldUserSelect;
            apply(root, currentState(root), true);
        };

        window.addEventListener("pointermove", move);
        window.addEventListener("pointerup", end);
        window.addEventListener("pointercancel", end);
    }

    function nudge(root, splitter, event) {
        if (!desktopQuery.matches) return;

        const kind = splitter.dataset.splitter;
        const horizontal = kind === "systems" || kind === "inspector";
        const step = event.shiftKey ? (horizontal ? 48 : 72) : (horizontal ? 16 : 24);
        const state = currentState(root);
        let handled = true;

        if (event.key === "Enter") {
            reset(root, kind);
        } else if (event.key === "Home") {
            if (kind === "systems") state.systemsWidth = limits.systems.min;
            else if (kind === "inspector") state.inspectorWidth = limits.inspector.min;
            else if (kind === "events") state.eventHeight = limits.events.min;
            else if (kind === "map") state.mapHeight = limits.map.min;
            apply(root, state, true);
        } else if (event.key === "End") {
            if (kind === "systems") state.systemsWidth = limits.systems.max;
            else if (kind === "inspector") state.inspectorWidth = limits.inspector.max;
            else if (kind === "events") state.eventHeight = Math.min(limits.events.max, window.innerHeight * 0.58);
            else if (kind === "map") state.mapHeight = Math.min(limits.map.max, window.innerHeight * 0.86);
            apply(root, state, true);
        } else if (kind === "systems" && event.key === "ArrowLeft") {
            state.systemsWidth -= step;
            apply(root, state, true);
        } else if (kind === "systems" && event.key === "ArrowRight") {
            state.systemsWidth += step;
            apply(root, state, true);
        } else if (kind === "inspector" && event.key === "ArrowLeft") {
            state.inspectorWidth += step;
            apply(root, state, true);
        } else if (kind === "inspector" && event.key === "ArrowRight") {
            state.inspectorWidth -= step;
            apply(root, state, true);
        } else if (kind === "events" && event.key === "ArrowUp") {
            state.eventHeight += step;
            apply(root, state, true);
        } else if (kind === "events" && event.key === "ArrowDown") {
            state.eventHeight -= step;
            apply(root, state, true);
        } else if (kind === "map" && event.key === "ArrowUp") {
            state.mapHeight -= step;
            apply(root, state, true);
        } else if (kind === "map" && event.key === "ArrowDown") {
            state.mapHeight += step;
            apply(root, state, true);
        } else {
            handled = false;
        }

        if (handled) event.preventDefault();
    }

    function bindRoot(root) {
        if (!root || root.dataset.layoutSplittersReady === "true") return;

        root.dataset.layoutSplittersReady = "true";
        roots.add(root);

        const stored = readStored();
        apply(root, { ...defaultState(), ...stored }, false);

        root.querySelectorAll("[data-splitter]").forEach(splitter => {
            splitter.addEventListener("pointerdown", event => beginDrag(root, splitter, event));
            splitter.addEventListener("keydown", event => nudge(root, splitter, event));
            splitter.addEventListener("dblclick", event => {
                event.preventDefault();
                reset(root, splitter.dataset.splitter);
            });
        });

        root.querySelector("[data-layout-reset]")?.addEventListener("click", event => {
            event.preventDefault();
            reset(root);
        });
    }

    function init(selector = "#overseer-workspace") {
        const root = document.querySelector(selector);
        if (!root) return false;

        bindRoot(root);

        if (!resizeListenerInstalled) {
            resizeListenerInstalled = true;
            window.addEventListener("resize", () => {
                for (const candidate of [...roots]) {
                    if (!candidate.isConnected) {
                        roots.delete(candidate);
                        continue;
                    }

                    if (desktopQuery.matches) {
                        apply(candidate, currentState(candidate), false);
                    }
                }
            });
        }

        return true;
    }

    return { init };
})();
