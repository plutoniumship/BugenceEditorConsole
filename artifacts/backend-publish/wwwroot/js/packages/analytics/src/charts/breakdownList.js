export function renderBreakdownList(host, entries, emptyLabel) {
    host.innerHTML = "";
    if (!entries.length) {
        const empty = document.createElement("p");
        empty.className = "insights-analytics__empty";
        empty.textContent = emptyLabel;
        host.appendChild(empty);
        return;
    }
    const total = entries.reduce((sum, entry) => sum + entry.changes, 0);
    const list = document.createElement("ul");
    list.className = "insights-analytics__list";
    entries.forEach((entry) => {
        const item = document.createElement("li");
        item.className = "insights-analytics__list-item";
        const label = document.createElement("span");
        label.className = "insights-analytics__list-label";
        label.textContent = entry.label;
        const value = document.createElement("strong");
        value.className = "insights-analytics__list-value";
        value.textContent = entry.changes.toLocaleString();
        const bar = document.createElement("span");
        bar.className = "insights-analytics__list-meter";
        const percentage = total > 0 ? Math.round((entry.changes / total) * 100) : 0;
        bar.style.setProperty("--meter", `${percentage}`);
        bar.textContent = `${percentage}%`;
        item.append(label, value, bar);
        list.appendChild(item);
    });
    host.appendChild(list);
}
