import { getTimelineSnapshot, useDashboardStore } from "../store/dashboardStore";
const DEFAULT_TASKS = [
    { id: "copy-review", label: "Confirm copy review completed", completed: false, origin: "default" },
    { id: "accessibility-audit", label: "Run accessibility spot-check", completed: false, origin: "default" }
];
export function initPublishConsole() {
    const summaryPanel = document.querySelector("[data-publish-summary-panel]");
    const consoleRoot = document.querySelector("[data-publish-console]");
    const notificationsHost = document.querySelector("[data-dashboard-notifications]");
    const publishForm = document.querySelector("[data-publish-form]");
    const pageId = document.querySelector("[data-dashboard-page]")?.getAttribute("data-dashboard-page");
    if (!summaryPanel || !consoleRoot || !pageId || !publishForm) {
        return;
    }
    const openButton = summaryPanel.querySelector("[data-publish-console-open]");
    const previewList = summaryPanel.querySelector("[data-publish-task-preview]");
    const previewEmpty = summaryPanel.querySelector("[data-publish-task-empty]");
    const summaryList = consoleRoot.querySelector("[data-publish-console-summary]");
    const summaryEmpty = consoleRoot.querySelector("[data-publish-console-summary-empty]");
    const summaryCount = consoleRoot.querySelector("[data-publish-console-summary-count]");
    const taskList = consoleRoot.querySelector("[data-publish-console-task-list]");
    const taskEmpty = consoleRoot.querySelector("[data-publish-console-task-empty]");
    const taskProgress = consoleRoot.querySelector("[data-publish-console-task-progress]");
    const taskForm = consoleRoot.querySelector("[data-publish-console-task-form]");
    const notesInput = consoleRoot.querySelector("[data-publish-console-notes]");
    const statusHost = consoleRoot.querySelector("[data-publish-console-status]");
    const confirmButton = consoleRoot.querySelector("[data-publish-console-confirm]");
    const closeButtons = consoleRoot.querySelectorAll("[data-publish-console-close]");
    const state = {
        tasks: DEFAULT_TASKS.map((task) => ({ ...task })),
        summary: null,
        notes: ""
    };
    const store = useDashboardStore();
    const render = () => {
        const snapshot = getTimelineSnapshot(pageId);
        state.summary = snapshot.publishSummary ?? null;
        updateSummary(state, summaryList, summaryEmpty, summaryCount);
        updateTaskViews(state, { previewList, previewEmpty, taskList, taskEmpty, taskProgress });
        updateStatus(state, statusHost, confirmButton);
    };
    render();
    const unsubscribe = store.subscribe(render);
    openButton?.addEventListener("click", () => openConsole(consoleRoot));
    closeButtons.forEach((button) => button.addEventListener("click", () => closeConsole(consoleRoot)));
    consoleRoot.addEventListener("keydown", (event) => {
        if (event.key === "Escape") {
            closeConsole(consoleRoot);
        }
    });
    confirmButton?.addEventListener("click", () => {
        if (confirmButton.disabled) {
            return;
        }
        publishForm.requestSubmit();
        closeConsole(consoleRoot);
        if (notificationsHost) {
            pushNotification(notificationsHost, "Publishing request dispatched", "Concierge automation is processing your publish request.");
        }
    });
    taskList?.addEventListener("change", (event) => {
        const target = event.target;
        if (!target || target.type !== "checkbox") {
            return;
        }
        const taskId = target.getAttribute("data-task-id");
        if (!taskId) {
            return;
        }
        const task = state.tasks.find((item) => item.id === taskId);
        if (!task) {
            return;
        }
        task.completed = target.checked;
        updateTaskViews(state, { previewList, previewEmpty, taskList, taskEmpty, taskProgress });
        updateStatus(state, statusHost, confirmButton);
    });
    taskList?.addEventListener("click", (event) => {
        const button = event.target?.closest("[data-task-remove]");
        if (!button) {
            return;
        }
        const taskId = button.getAttribute("data-task-remove");
        if (!taskId) {
            return;
        }
        const index = state.tasks.findIndex((task) => task.id === taskId);
        if (index >= 0 && state.tasks[index].origin === "manual") {
            state.tasks.splice(index, 1);
            updateTaskViews(state, { previewList, previewEmpty, taskList, taskEmpty, taskProgress });
            updateStatus(state, statusHost, confirmButton);
        }
    });
    taskForm?.addEventListener("submit", (event) => {
        event.preventDefault();
        const input = taskForm.querySelector("input[type='text']");
        if (!input) {
            return;
        }
        const label = input.value.trim();
        if (!label) {
            return;
        }
        const id = `manual-${Date.now()}`;
        state.tasks.push({ id, label, completed: false, origin: "manual" });
        input.value = "";
        updateTaskViews(state, { previewList, previewEmpty, taskList, taskEmpty, taskProgress });
        updateStatus(state, statusHost, confirmButton);
    });
    notesInput?.addEventListener("input", () => {
        state.notes = notesInput.value;
    });
    document.addEventListener("dashboard:task:add", (event) => {
        const detail = event.detail;
        if (!detail) {
            return;
        }
        if (state.tasks.some((task) => task.id === detail.id)) {
            return;
        }
        state.tasks.push({
            id: detail.id,
            label: detail.label,
            completed: false,
            origin: "event"
        });
        updateTaskViews(state, { previewList, previewEmpty, taskList, taskEmpty, taskProgress });
        updateStatus(state, statusHost, confirmButton);
        if (notificationsHost) {
            pushNotification(notificationsHost, "Task added", detail.label);
        }
    });
    window.addEventListener("beforeunload", () => {
        unsubscribe();
    }, { once: true });
}
function openConsole(root) {
    root.hidden = false;
    requestAnimationFrame(() => {
        root.dataset.state = "open";
        const closeButton = root.querySelector(".publish-console__close");
        closeButton?.focus({ preventScroll: true });
        document.body.style.setProperty("overflow", "hidden");
    });
}
function closeConsole(root) {
    root.dataset.state = "idle";
    document.body.style.removeProperty("overflow");
    setTimeout(() => {
        root.hidden = true;
    }, 180);
}
function updateSummary(state, list, emptyState, summaryCount) {
    if (!list) {
        return;
    }
    list.innerHTML = "";
    const summary = state.summary;
    if (!summary || summary.entries.length === 0) {
        if (emptyState) {
            emptyState.hidden = false;
        }
        if (summaryCount) {
            summaryCount.textContent = "0 sections";
        }
        return;
    }
    if (emptyState) {
        emptyState.hidden = true;
    }
    if (summaryCount) {
        summaryCount.textContent = `${summary.entries.length} section${summary.entries.length === 1 ? "" : "s"}`;
    }
    const fragment = document.createDocumentFragment();
    summary.entries.forEach((entry) => {
        const item = document.createElement("li");
        item.className = "publish-console__summary-item";
        item.dataset.changeType = entry.changeType;
        const title = document.createElement("div");
        title.className = "publish-console__summary-head";
        const key = document.createElement("strong");
        key.textContent = entry.sectionKey ?? "Section";
        const meta = document.createElement("span");
        meta.textContent = `${entry.changeType} · ${entry.contentType ?? "Content"}`;
        title.append(key, meta);
        const notes = document.createElement("p");
        notes.className = "publish-console__summary-notes";
        notes.textContent = entry.diffSummary ?? "No diff summary recorded.";
        item.append(title, notes);
        fragment.appendChild(item);
    });
    list.appendChild(fragment);
}
function updateTaskViews(state, elements) {
    const { tasks } = state;
    const completed = tasks.filter((task) => task.completed).length;
    if (elements.previewList) {
        elements.previewList.innerHTML = "";
        const limited = tasks.slice(0, 3);
        if (!limited.length) {
            if (elements.previewEmpty)
                elements.previewEmpty.hidden = false;
        }
        else {
            if (elements.previewEmpty)
                elements.previewEmpty.hidden = true;
            limited.forEach((task) => {
                const item = document.createElement("li");
                item.className = "publish-console__preview-item";
                item.dataset.state = task.completed ? "done" : "pending";
                item.textContent = task.label;
                elements.previewList?.appendChild(item);
            });
        }
    }
    if (elements.taskList) {
        elements.taskList.innerHTML = "";
        if (!tasks.length) {
            if (elements.taskEmpty)
                elements.taskEmpty.hidden = false;
        }
        else {
            if (elements.taskEmpty)
                elements.taskEmpty.hidden = true;
            const fragment = document.createDocumentFragment();
            tasks.forEach((task) => {
                const item = document.createElement("li");
                item.className = "publish-console__task";
                const label = document.createElement("label");
                label.className = "publish-console__task-label";
                const checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.checked = task.completed;
                checkbox.setAttribute("data-task-id", task.id);
                const text = document.createElement("span");
                text.textContent = task.label;
                label.append(checkbox, text);
                item.appendChild(label);
                if (task.origin === "manual") {
                    const remove = document.createElement("button");
                    remove.type = "button";
                    remove.className = "publish-console__task-remove";
                    remove.setAttribute("data-task-remove", task.id);
                    remove.innerHTML = `<span class="fa-solid fa-xmark" aria-hidden="true"></span><span class="sr-only">Remove task</span>`;
                    item.appendChild(remove);
                }
                fragment.appendChild(item);
            });
            elements.taskList.appendChild(fragment);
        }
    }
    if (elements.taskProgress) {
        elements.taskProgress.textContent = `${completed} of ${tasks.length} complete`;
    }
}
function updateStatus(state, statusHost, confirmButton) {
    const hasOutstandingTasks = state.tasks.some((task) => !task.completed);
    const hasSummary = Boolean(state.summary && state.summary.entries.length);
    if (statusHost) {
        if (!hasSummary) {
            statusHost.textContent = "Generate a diff summary before publishing.";
        }
        else if (hasOutstandingTasks) {
            statusHost.textContent = "Complete all pre-flight tasks to enable publishing.";
        }
        else {
            statusHost.textContent = "All tasks completed. Ready for publish.";
        }
    }
    if (confirmButton) {
        confirmButton.disabled = hasOutstandingTasks || !hasSummary;
    }
}
function pushNotification(host, title, message) {
    const item = document.createElement("div");
    item.className = "dashboard-notification";
    const heading = document.createElement("strong");
    heading.textContent = title;
    const body = document.createElement("p");
    body.textContent = message;
    item.append(heading, body);
    host.appendChild(item);
    setTimeout(() => {
        item.classList.add("dashboard-notification--hide");
        setTimeout(() => {
            item.remove();
        }, 300);
    }, 4000);
}
