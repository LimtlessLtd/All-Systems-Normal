window.overseerLayout = (() => {
    const storageKey = "all-systems-normal.workspace-layout.v2";
    const desktopQuery = window.matchMedia("(min-width: 1181px)");
    const roots = new Set();
    const cameraStates = new WeakMap();
    const limits = {
        inspector: { min: 300, max: 560 },
        map: { min: 420, max: 980 },
        mapMinimumWidth: 680,
        splitterWidth: 12
    };

    const cameraLimits = {
        minZoom: 0.15,
        maxZoom: 3.0,
        zoomStep: 0.05,
        keyboardStep: 42
    };

    let resizeListenerInstalled = false;
    let keyboardListenerInstalled = false;
    let lastActiveCamera = null;

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
                inspectorWidth: Math.round(state.inspectorWidth),
                mapHeight: Math.round(state.mapHeight)
            }));
        } catch {
            // Storage is an enhancement; layout must still work when blocked.
        }
    }

    function defaultState() {
        return {
            inspectorWidth: 360,
            mapHeight: clamp(Math.round(window.innerHeight * 0.72), 520, 900)
        };
    }

    function currentState(root) {
        const inspector = root.querySelector(".inspector-panel");
        const map = root.querySelector(".station-map-viewport");
        const defaults = defaultState();

        return {
            inspectorWidth: inspector?.getBoundingClientRect().width || defaults.inspectorWidth,
            mapHeight: map?.getBoundingClientRect().height || defaults.mapHeight
        };
    }

    function dynamicInspectorMaximum(root) {
        const grid = root.querySelector(".operations-grid");
        if (!grid) return limits.inspector.max;

        const available = grid.getBoundingClientRect().width
            - limits.mapMinimumWidth
            - limits.splitterWidth;

        return Math.max(
            limits.inspector.min,
            Math.min(limits.inspector.max, available));
    }

    function normalize(root, state) {
        const normalized = { ...state };
        normalized.inspectorWidth = clamp(
            Number(normalized.inspectorWidth) || defaultState().inspectorWidth,
            limits.inspector.min,
            dynamicInspectorMaximum(root));

        const viewportMapMax = Math.max(
            limits.map.min,
            Math.min(limits.map.max, Math.floor(window.innerHeight * 0.9)));

        normalized.mapHeight = clamp(
            Number(normalized.mapHeight) || defaultState().mapHeight,
            limits.map.min,
            viewportMapMax);

        return normalized;
    }

    function updateAria(root, state) {
        const values = {
            inspector: state.inspectorWidth,
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
        root.style.setProperty("--inspector-width", `${normalized.inspectorWidth}px`);
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
            root.querySelectorAll("[data-map-camera]").forEach(resetCamera);
            return;
        }

        if (kind === "inspector") state.inspectorWidth = defaults.inspectorWidth;
        if (kind === "map") state.mapHeight = defaults.mapHeight;

        apply(root, state, true);
    }

    function cursorFor(kind) {
        return kind === "inspector" ? "col-resize" : "row-resize";
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

            if (kind === "inspector") next.inspectorWidth = start.inspectorWidth - deltaX;
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
        const horizontal = kind === "inspector";
        const step = event.shiftKey ? (horizontal ? 48 : 72) : (horizontal ? 16 : 24);
        const state = currentState(root);
        let handled = true;

        if (event.key === "Enter") {
            reset(root, kind);
        } else if (event.key === "Home") {
            if (kind === "inspector") state.inspectorWidth = limits.inspector.min;
            else if (kind === "map") state.mapHeight = limits.map.min;
            apply(root, state, true);
        } else if (event.key === "End") {
            if (kind === "inspector") state.inspectorWidth = limits.inspector.max;
            else if (kind === "map") state.mapHeight = Math.min(limits.map.max, window.innerHeight * 0.9);
            apply(root, state, true);
        } else if (kind === "inspector" && event.key === "ArrowLeft") {
            state.inspectorWidth += step;
            apply(root, state, true);
        } else if (kind === "inspector" && event.key === "ArrowRight") {
            state.inspectorWidth -= step;
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

    function cameraState(viewport) {
        let state = cameraStates.get(viewport);
        if (!state) {
            state = { x: 0, y: 0, zoom: 1, suppressClick: false };
            cameraStates.set(viewport, state);
        }
        return state;
    }

    function applyCamera(viewport) {
        if (!viewport?.isConnected) return;

        const state = cameraState(viewport);
        const content = viewport.querySelector("[data-map-camera-content]");
        if (!content) return;

        const rect = viewport.getBoundingClientRect();
        state.zoom = clamp(state.zoom, cameraLimits.minZoom, cameraLimits.maxZoom);

        // The camera content is intentionally much larger than the viewport.
        // Clamp against its scaled physical dimensions so every edge of a
        // sprawling station remains reachable by drag/WASD.
        const contentWidth = content.offsetWidth || rect.width;
        const contentHeight = content.offsetHeight || rect.height;
        const maxX = Math.max(
            80,
            ((contentWidth * state.zoom) - rect.width) / 2 + (rect.width * 0.12));
        const maxY = Math.max(
            80,
            ((contentHeight * state.zoom) - rect.height) / 2 + (rect.height * 0.12));
        state.x = clamp(state.x, -maxX, maxX);
        state.y = clamp(state.y, -maxY, maxY);

        content.style.transform =
            `translate3d(${state.x.toFixed(1)}px, ${state.y.toFixed(1)}px, 0) scale(${state.zoom.toFixed(2)})`;

        const map = content.closest(".station-map");
        if (map) {
            map.classList.toggle("zoom-wide", state.zoom < 0.9);
            map.classList.toggle("zoom-normal", state.zoom >= 0.9 && state.zoom < 1.35);
            map.classList.toggle("zoom-close", state.zoom >= 1.35);
        }

        // The zoom toolbar lives in the panel heading, outside the viewport.
        const controls = viewport.closest(".station-panel") ?? viewport;

        controls.querySelectorAll("[data-map-zoom-readout]").forEach(readout => {
            readout.textContent = `${Math.round(state.zoom * 100)}%`;
        });

        controls.querySelectorAll("[data-map-zoom-out]").forEach(button => {
            button.disabled = state.zoom <= cameraLimits.minZoom + 0.001;
        });
        controls.querySelectorAll("[data-map-zoom-in]").forEach(button => {
            button.disabled = state.zoom >= cameraLimits.maxZoom - 0.001;
        });
    }

    function panCamera(viewport, dx, dy) {
        const state = cameraState(viewport);
        state.x += dx;
        state.y += dy;
        applyCamera(viewport);
    }

    function zoomCamera(viewport, delta) {
        const state = cameraState(viewport);
        state.zoom = clamp(
            Math.round((state.zoom + delta) * 100) / 100,
            cameraLimits.minZoom,
            cameraLimits.maxZoom);
        applyCamera(viewport);
    }

    function resetCamera(viewport) {
        const state = cameraState(viewport);
        state.x = 0;
        state.y = 0;
        state.zoom = 1;
        state.suppressClick = false;
        applyCamera(viewport);
    }

    function isStationInteractiveTarget(target) {
        return target instanceof Element
            && target.closest("[data-station-interactive], button, a, input, select, textarea, summary");
    }

    function isFormControlTarget(target) {
        return target instanceof Element
            && target.closest("input, select, textarea");
    }

    function bindMapCamera(viewport) {
        if (!viewport || viewport.dataset.mapCameraReady === "true") {
            if (viewport) applyCamera(viewport);
            return;
        }

        viewport.dataset.mapCameraReady = "true";
        viewport.tabIndex = viewport.tabIndex >= 0 ? viewport.tabIndex : 0;
        lastActiveCamera = lastActiveCamera?.isConnected ? lastActiveCamera : viewport;
        applyCamera(viewport);

        let drag = null;

        viewport.addEventListener("pointerdown", event => {
            if (event.button !== 0) return;
            lastActiveCamera = viewport;
            if (isFormControlTarget(event.target)) return;

            // Rooms and corridors are buttons that cover most of the map, so a
            // drag must be able to start on one. The pointer is only captured
            // once the press moves far enough to be a pan: capturing on
            // pointerdown retargeted plain clicks to the viewport and made
            // rooms/crew/doors/robots look unclickable.
            if (!isStationInteractiveTarget(event.target)) {
                viewport.focus({ preventScroll: true });
            }

            drag = {
                id: event.pointerId,
                startX: event.clientX,
                startY: event.clientY,
                lastX: event.clientX,
                lastY: event.clientY,
                moved: false
            };
        });

        viewport.addEventListener("pointermove", event => {
            if (!drag || drag.id !== event.pointerId) return;

            // Without early capture a release outside the viewport is never
            // seen here, so drop a pending drag once the button is up.
            if ((event.buttons & 1) === 0) {
                endDrag(event);
                return;
            }

            const totalX = event.clientX - drag.startX;
            const totalY = event.clientY - drag.startY;
            if (!drag.moved && Math.hypot(totalX, totalY) < 4) return;

            if (!drag.moved) {
                drag.moved = true;
                drag.previousUserSelect = document.body.style.userSelect;
                document.body.style.userSelect = "none";
                try {
                    viewport.setPointerCapture?.(event.pointerId);
                } catch {
                    // The pointer may already be released; panning still works.
                }
            }
            const dx = event.clientX - drag.lastX;
            const dy = event.clientY - drag.lastY;
            drag.lastX = event.clientX;
            drag.lastY = event.clientY;
            viewport.classList.add("is-panning");
            panCamera(viewport, dx, dy);
            event.preventDefault();
        });

        const endDrag = event => {
            if (!drag || (event.pointerId != null && drag.id !== event.pointerId)) return;
            if (drag.moved) {
                const state = cameraState(viewport);
                state.suppressClick = true;
                window.setTimeout(() => {
                    state.suppressClick = false;
                }, 0);
            }
            viewport.classList.remove("is-panning");
            if (drag.previousUserSelect !== undefined) {
                document.body.style.userSelect = drag.previousUserSelect;
            }
            drag = null;
        };

        viewport.addEventListener("pointerup", endDrag);
        viewport.addEventListener("pointercancel", endDrag);

        // lostpointercapture bubbles. When a touch drag begins on a room, the
        // browser's implicit capture on that button is handed to the viewport,
        // and the button's lostpointercapture must not end the pan it started.
        viewport.addEventListener("lostpointercapture", event => {
            if (event.target === viewport) endDrag(event);
        });

        // A press released outside the map before it became a pan is never
        // seen by the viewport; clear it so a later press cannot inherit it.
        window.addEventListener("pointerup", endDrag);
        window.addEventListener("pointercancel", endDrag);

        viewport.addEventListener("click", event => {
            if (!cameraState(viewport).suppressClick) return;
            event.preventDefault();
            event.stopImmediatePropagation();
        }, true);

        viewport.addEventListener("wheel", event => {
            // Hovering the station owns the wheel: scroll up zooms in, scroll
            // down zooms out. No Ctrl/Cmd chord is required.
            event.preventDefault();
            lastActiveCamera = viewport;
            viewport.focus({ preventScroll: true });
            zoomCamera(
                viewport,
                event.deltaY < 0
                    ? cameraLimits.zoomStep
                    : -cameraLimits.zoomStep);
        }, { passive: false });

        const panel = viewport.closest(".station-panel");
        panel?.querySelector("[data-map-zoom-in]")?.addEventListener("click", event => {
            event.preventDefault();
            lastActiveCamera = viewport;
            zoomCamera(viewport, cameraLimits.zoomStep);
        });
        panel?.querySelector("[data-map-zoom-out]")?.addEventListener("click", event => {
            event.preventDefault();
            lastActiveCamera = viewport;
            zoomCamera(viewport, -cameraLimits.zoomStep);
        });
        panel?.querySelector("[data-map-camera-reset]")?.addEventListener("click", event => {
            event.preventDefault();
            lastActiveCamera = viewport;
            resetCamera(viewport);
        });
        panel?.querySelector("[data-map-zoom-fit]")?.addEventListener("click", event => {
            event.preventDefault();
            lastActiveCamera = viewport;
            fitCamera(viewport);
        });
    }

    function centreOn(viewport, screenX, screenY) {
        const state = cameraState(viewport);
        const rect = viewport.getBoundingClientRect();
        state.x += (rect.left + rect.width / 2) - screenX;
        state.y += (rect.top + rect.height / 2) - screenY;
        applyCamera(viewport);
    }

    // Presentation only: frames the real room/corridor footprint on request.
    // The deck is never auto-fitted; this is an explicit player action.
    function fitCamera(viewport) {
        const rooms = [...viewport.querySelectorAll(".station-authority-layer > .room-node")];
        if (rooms.length === 0) return;

        const state = cameraState(viewport);
        state.x = 0;
        state.y = 0;
        state.zoom = 1;
        applyCamera(viewport);

        let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity;
        for (const room of rooms) {
            const box = room.getBoundingClientRect();
            left = Math.min(left, box.left);
            top = Math.min(top, box.top);
            right = Math.max(right, box.right);
            bottom = Math.max(bottom, box.bottom);
        }

        const rect = viewport.getBoundingClientRect();
        const width = Math.max(1, right - left);
        const height = Math.max(1, bottom - top);
        state.zoom = clamp(
            Math.floor(Math.min(rect.width / width, rect.height / height) * 0.92 * 100) / 100,
            cameraLimits.minZoom,
            cameraLimits.maxZoom);
        applyCamera(viewport);

        // Re-measure after scaling and centre the footprint.
        left = Infinity; top = Infinity; right = -Infinity; bottom = -Infinity;
        for (const room of rooms) {
            const box = room.getBoundingClientRect();
            left = Math.min(left, box.left);
            top = Math.min(top, box.top);
            right = Math.max(right, box.right);
            bottom = Math.max(bottom, box.bottom);
        }

        centreOn(viewport, (left + right) / 2, (top + bottom) / 2);
    }

    function focusEntity(selector) {
        const target = document.querySelector(selector);
        const viewport = target?.closest("[data-map-camera]");
        if (!target || !viewport) return false;

        lastActiveCamera = viewport;
        const box = target.getBoundingClientRect();
        centreOn(viewport, box.left + box.width / 2, box.top + box.height / 2);
        return true;
    }

    function revealStationPanel() {
        document.querySelector(".station-panel")?.scrollIntoView({ block: "start", behavior: "smooth" });
    }

    let pauseHandler = null;

    function registerPauseHandler(dotNetReference) {
        pauseHandler = dotNetReference;
    }

    function unregisterPauseHandler() {
        pauseHandler = null;
    }

    function bindMapCameras(root) {
        root.querySelectorAll("[data-map-camera]").forEach(bindMapCamera);
    }

    function isTypingTarget(target) {
        return target instanceof HTMLElement
            && (target.matches("input, textarea, select")
                || target.isContentEditable);
    }

    function installKeyboardListener() {
        if (keyboardListenerInstalled) return;
        keyboardListenerInstalled = true;

        document.addEventListener("keydown", event => {
            if (event.defaultPrevented
                || event.ctrlKey
                || event.metaKey
                || event.altKey
                || isTypingTarget(event.target)) {
                return;
            }

            // Space pauses/resumes the station, except where Space already
            // means "press this control".
            if (event.code === "Space"
                && !event.repeat
                && pauseHandler
                && !(event.target instanceof Element
                    && event.target.closest("button, a, summary, [role='button']"))) {
                event.preventDefault();
                pauseHandler.invokeMethodAsync("TogglePauseFromKeyboard");
                return;
            }

            if (!lastActiveCamera?.isConnected) {
                return;
            }

            const step = cameraLimits.keyboardStep * (event.shiftKey ? 2 : 1);
            const key = event.key.toLowerCase();
            let dx = 0;
            let dy = 0;

            if (key === "w" || key === "arrowup") dy = step;
            else if (key === "s" || key === "arrowdown") dy = -step;
            else if (key === "a" || key === "arrowleft") dx = step;
            else if (key === "d" || key === "arrowright") dx = -step;
            else return;

            event.preventDefault();
            panCamera(lastActiveCamera, dx, dy);
        });
    }

    function bindRoot(root) {
        if (!root) return;

        roots.add(root);

        if (root.dataset.layoutSplittersReady !== "true") {
            root.dataset.layoutSplittersReady = "true";
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

        bindMapCameras(root);
        installKeyboardListener();
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

                    bindMapCameras(candidate);
                    candidate.querySelectorAll("[data-map-camera]").forEach(applyCamera);
                }
            });
        }

        return true;
    }

    return { init, fitCamera: selector => {
        const viewport = document.querySelector(selector);
        if (viewport) fitCamera(viewport);
    }, focusEntity, revealStationPanel, registerPauseHandler, unregisterPauseHandler };
})();
