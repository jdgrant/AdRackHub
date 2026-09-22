(function () {
    const board = document.querySelector(".pipeline[data-status-url]");
    if (!board) return;

    const url = board.getAttribute("data-status-url");
    const token = board.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const subnoteUrl = board.getAttribute("data-subnote-url");
    const threshold = 8;
    let session = null;

    const modalEl = document.getElementById("pipelineSubNoteModal");
    const modal = modalEl && window.bootstrap ? bootstrap.Modal.getOrCreateInstance(modalEl) : null;
    const targetEl = document.getElementById("pipelineSubNoteTarget");
    const bodyEl = document.getElementById("pipelineSubNoteBody");
    const errorEl = document.getElementById("pipelineSubNoteError");
    const saveEl = document.getElementById("pipelineSubNoteSave");
    let subNoteId = "";

    function laneCount(lane) {
        return lane.querySelectorAll(".pipeline-card").length;
    }

    function refreshCounts() {
        board.querySelectorAll(".pipeline-lane").forEach((lane) => {
            const count = lane.querySelector(".pipeline-lane-count");
            if (count) count.textContent = String(laneCount(lane));
        });
    }

    function clearTargets() {
        board.querySelectorAll(".pipeline-lane.is-target").forEach((lane) => lane.classList.remove("is-target"));
    }

    function laneFromPoint(x, y) {
        const el = document.elementFromPoint(x, y);
        return el ? el.closest(".pipeline-lane") : null;
    }

    function applyOverdue(card, status) {
        if (card.dataset.kind === "brochures") return;
        const due = card.querySelector(".pipeline-card-due");
        const label = card.querySelector(".pipeline-overdue-label");
        if (!due) return;
        const overdue = card.dataset.pastDue === "true" && status !== "Done";
        due.classList.toggle("is-overdue", overdue);
        if (label) label.classList.toggle("d-none", !overdue);
    }

    function startDrag(sessionState, event) {
        const card = sessionState.card;
        sessionState.dragging = true;
        const rect = card.getBoundingClientRect();
        const ghost = card.cloneNode(true);
        ghost.classList.add("pipeline-card-ghost");
        ghost.style.width = rect.width + "px";
        ghost.style.left = rect.left + "px";
        ghost.style.top = rect.top + "px";
        document.body.appendChild(ghost);
        sessionState.ghost = ghost;
        sessionState.offsetX = event.clientX - rect.left;
        sessionState.offsetY = event.clientY - rect.top;
        card.classList.add("is-dragging");
        card.style.pointerEvents = "none";
        try {
            card.setPointerCapture(event.pointerId);
        } catch (err) {
            /* ignore */
        }
    }

    function moveGhost(sessionState, event) {
        if (!sessionState.ghost) return;
        sessionState.ghost.style.left = (event.clientX - sessionState.offsetX) + "px";
        sessionState.ghost.style.top = (event.clientY - sessionState.offsetY) + "px";
        clearTargets();
        const lane = laneFromPoint(event.clientX, event.clientY);
        if (lane) lane.classList.add("is-target");
    }

    function endDrag(sessionState) {
        if (sessionState.ghost) sessionState.ghost.remove();
        sessionState.card.classList.remove("is-dragging");
        sessionState.card.style.pointerEvents = "";
        clearTargets();
    }

    async function saveStatus(card, status) {
        if (!url || !token) return false;
        const body = new URLSearchParams();
        body.set("id", card.dataset.noteId || card.dataset.taskId || "");
        body.set("status", status);
        body.set("__RequestVerificationToken", token);
        const response = await fetch(url, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                RequestVerificationToken: token
            },
            body
        });
        return response.ok;
    }

    function showSubNoteError(message) {
        if (!errorEl) return;
        errorEl.textContent = message || "Could not save the sub-note.";
        errorEl.classList.remove("d-none");
    }

    function openSubNote(card) {
        subNoteId = card.dataset.noteId || "";
        if (targetEl) {
            const kind = card.querySelector(".pipeline-card-type")?.textContent?.trim() || "Activity";
            const name = card.dataset.customer || card.querySelector(".pipeline-card-name")?.textContent?.trim() || "";
            targetEl.textContent = name ? `${kind} · ${name}` : kind;
        }
        if (bodyEl) bodyEl.value = "";
        if (errorEl) {
            errorEl.textContent = "";
            errorEl.classList.add("d-none");
        }
        if (modal) modal.show();
        else if (bodyEl) bodyEl.focus();
        window.setTimeout(() => bodyEl?.focus(), 200);
    }

    async function saveSubNote() {
        if (!subnoteUrl || !token || !subNoteId) return;
        const text = (bodyEl?.value || "").trim();
        if (!text) {
            showSubNoteError("Enter a sub-note before saving.");
            return;
        }

        const body = new URLSearchParams();
        body.set("id", subNoteId);
        body.set("body", text);
        body.set("__RequestVerificationToken", token);
        const response = await fetch(subnoteUrl, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                RequestVerificationToken: token
            },
            body
        });
        if (!response.ok) {
            showSubNoteError("Could not save the sub-note.");
            return;
        }
        if (modal) modal.hide();
    }

    board.addEventListener("pointerdown", (event) => {
        if (event.button !== 0) return;
        if (event.target.closest(".pipeline-card-note")) {
            event.stopPropagation();
            return;
        }
        const card = event.target.closest(".pipeline-card");
        if (!card || card.classList.contains("pipeline-card-ghost")) return;
        session = {
            card,
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            dragging: false,
            ghost: null,
            offsetX: 0,
            offsetY: 0
        };
    });

    window.addEventListener("pointermove", (event) => {
        if (!session || event.pointerId !== session.pointerId) return;
        if (!session.dragging) {
            const distance = Math.hypot(event.clientX - session.startX, event.clientY - session.startY);
            if (distance < threshold) return;
            startDrag(session, event);
        }
        event.preventDefault();
        moveGhost(session, event);
    });

    async function finish(event) {
        if (!session || event.pointerId !== session.pointerId) return;
        const current = session;
        session = null;
        if (!current.dragging) {
            const href = current.card.dataset.href;
            if (href) window.location.href = href;
            return;
        }

        const lane = laneFromPoint(event.clientX, event.clientY);
        const source = current.card.closest(".pipeline-lane");
        endDrag(current);
        if (!lane || lane === source) return;

        const status = lane.dataset.status;
        const cards = lane.querySelector(".pipeline-lane-cards");
        if (!status || !cards) return;

        const ok = await saveStatus(current.card, status);
        if (!ok) return;

        cards.appendChild(current.card);
        applyOverdue(current.card, status);
        refreshCounts();
    }

    window.addEventListener("pointerup", finish);
    window.addEventListener("pointercancel", (event) => {
        if (!session || event.pointerId !== session.pointerId) return;
        const current = session;
        session = null;
        if (current.dragging) endDrag(current);
    });

    board.addEventListener("click", (event) => {
        const noteBtn = event.target.closest(".pipeline-card-note");
        if (!noteBtn) return;
        event.preventDefault();
        event.stopPropagation();
        const card = noteBtn.closest(".pipeline-card");
        if (card) openSubNote(card);
    });

    saveEl?.addEventListener("click", () => { saveSubNote(); });
    bodyEl?.addEventListener("keydown", (event) => {
        if (event.key === "Enter" && (event.metaKey || event.ctrlKey)) {
            event.preventDefault();
            saveSubNote();
        }
    });
})();
