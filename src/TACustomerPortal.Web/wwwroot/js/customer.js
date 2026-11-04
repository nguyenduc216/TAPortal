"use strict";
// Full-page submits only (sign-in, sign-out): forms inside the portal shell are sent in the background by ta-shell.js.
document.addEventListener("submit", event => {
    if (!(event.target instanceof HTMLFormElement) || event.defaultPrevented) return;
    event.target.setAttribute("aria-busy", "true");
    event.target.querySelectorAll("button[type=submit], button:not([type])").forEach(button => {
        button.disabled = true;
        button.dataset.pending = "true";
    });
});
window.addEventListener("pageshow", () => {
    document.querySelectorAll("button[data-pending]").forEach(button => {
        button.disabled = false;
        delete button.dataset.pending;
    });
    document.querySelectorAll("form[aria-busy]").forEach(form => form.removeAttribute("aria-busy"));
});
// Order form: add/remove item rows (names re-indexed for model binding) and a live total preview. The server recomputes
// every amount; this is display only. Delegated so it keeps working after soft navigation swaps the page.
const money = value => new Intl.NumberFormat("vi-VN").format(Math.round(value)) + " ₫";
const reindexLines = table => table.querySelectorAll("tbody tr[data-cp-line]").forEach((row, index) =>
    row.querySelectorAll("[name^='Lines[']").forEach(input => { input.name = input.name.replace(/^Lines\[\d+\]/, `Lines[${index}]`); }));
const updateTotals = form => {
    let total = 0;
    form.querySelectorAll("tr[data-cp-line]").forEach(row => {
        const qty = parseFloat(row.querySelector("[data-cp-qty]")?.value) || 0;
        const price = parseFloat(row.querySelector("[data-cp-price]")?.value) || 0;
        const rate = Math.max(0, parseInt(row.querySelector("[data-cp-tax]")?.value, 10) || 0);
        const before = Math.round(qty * price);
        const line = before + Math.round(before * rate / 100);
        total += line;
        const cell = row.querySelector("[data-cp-line-total]");
        if (cell) cell.textContent = line > 0 ? money(line) : "—";
    });
    const out = form.querySelector("[data-cp-order-total]");
    if (out) out.textContent = total > 0 ? money(total) : "—";
};
document.addEventListener("click", event => {
    const add = event.target.closest("[data-cp-add-line]");
    const remove = event.target.closest("[data-cp-remove-line]");
    const form = event.target.closest("form[data-cp-order-form]");
    if (!form || (!add && !remove)) return;
    const table = form.querySelector("#orderLines");
    const rows = table.querySelectorAll("tbody tr[data-cp-line]");
    if (add) {
        if (rows.length >= 50) return;
        const clone = rows[rows.length - 1].cloneNode(true);
        clone.querySelectorAll("input").forEach(input => { input.value = input.hasAttribute("data-cp-qty") ? "1" : ""; });
        clone.querySelectorAll("select").forEach(select => { select.selectedIndex = 0; });
        table.querySelector("tbody").appendChild(clone);
        clone.querySelector("input")?.focus();
    } else if (rows.length > 1) {
        remove.closest("tr").remove();
    } else {
        rows[0].querySelectorAll("input").forEach(input => { input.value = input.hasAttribute("data-cp-qty") ? "1" : ""; });
    }
    reindexLines(table);
    updateTotals(form);
});
document.addEventListener("input", event => {
    const form = event.target.closest?.("form[data-cp-order-form]");
    if (form) updateTotals(form);
});
// Order details: while the QR is open, poll the order status and reload when a payment arrives.
let pollTimer = null;
const startPolling = () => {
    clearInterval(pollTimer);
    const box = document.querySelector("[data-cp-poll]");
    if (!box) return;
    pollTimer = setInterval(async () => {
        if (!document.body.contains(box)) { clearInterval(pollTimer); return; }
        try {
            const response = await fetch(box.dataset.cpPoll, { headers: { Accept: "application/json" }, credentials: "same-origin" });
            if (!response.ok) return;
            const state = await response.json();
            if (state.status !== box.dataset.cpPollStatus || String(state.paid) !== box.dataset.cpPollPaid) location.reload();
        } catch { /* network hiccup: try again on the next tick */ }
    }, 10000);
};
const initPage = () => {
    document.querySelectorAll("form[data-cp-order-form]").forEach(updateTotals);
    startPolling();
};
document.addEventListener("ta:load", initPage);
if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initPage); else initPage();
// Account menu (<details>): close after navigation, on outside click and on Escape.
const closeMenus = except => document.querySelectorAll("details[data-cp-menu][open]").forEach(menu => { if (menu !== except) menu.open = false; });
document.addEventListener("ta:load", () => closeMenus(null));
document.addEventListener("click", event => closeMenus(event.target.closest("details[data-cp-menu]")));
document.addEventListener("keydown", event => { if (event.key === "Escape") closeMenus(null); });

// Bank linking: a bank logo opens the partner's linking popup (its web SDK) instead of the step-by-step form. The server
// opens the session (LinkSession: SDK URL + script); on success the hidden Sync form adds the new account to the list.
// Capture phase, so the shell's soft navigation never follows the logo's fallback link.
const sdkScripts = {};
const loadBankLinkSdk = src => sdkScripts[src] ??= new Promise((resolve, reject) => {
    const tag = document.createElement("script");
    tag.src = src;
    tag.async = true;
    tag.onload = () => window.TingeeBankLink ? resolve(window.TingeeBankLink) : reject(new Error("load"));
    tag.onerror = () => { delete sdkScripts[src]; reject(new Error("load")); };
    document.head.appendChild(tag);
});
document.addEventListener("click", event => {
    const logo = event.target.closest("[data-cp-banklink] [data-cp-bank]");
    if (!logo || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
    event.preventDefault();
    event.stopPropagation();
    const card = logo.closest(".cp-card");
    const done = card.querySelector("[data-cp-banklink-done]");
    const status = card.querySelector("[data-cp-banklink-status]");
    const say = (text, tone) => {
        status.hidden = false;
        status.classList.toggle("cp-alert", tone === "error");
        status.querySelector("[data-cp-banklink-text]").textContent = text;
    };
    if (card.dataset.busy === "true") return;
    card.dataset.busy = "true";
    card.querySelectorAll("[data-cp-bank]").forEach(a => a.removeAttribute("aria-current"));
    logo.setAttribute("aria-current", "true");
    say("Đang mở cửa sổ liên kết " + logo.title + "…");
    const body = new FormData(done);
    body.set("bin", logo.dataset.cpBank);
    fetch(logo.closest("[data-cp-banklink]").dataset.cpBanklink, { method: "POST", body, credentials: "same-origin", headers: { "Accept": "application/json" } })
        .then(r => r.json())
        .then(s => {
            if (!s.ok) throw new Error(s.error);
            return loadBankLinkSdk(s.script).then(sdk => {
                const color = getComputedStyle(document.documentElement).getPropertyValue("--cp-blue-600").trim() || "#206bc4";
                let linked = false;
                sdk.Setup({
                    size: "medium",
                    theme: { primaryColor: color },
                    onSuccess: () => { linked = true; say("Liên kết thành công, đang cập nhật danh sách tài khoản…"); done.requestSubmit(); },
                    onFailed: d => say("Liên kết không thành công" + (d && d.errorMessage ? ": " + d.errorMessage : ".") + " Bạn có thể chọn lại ngân hàng để thử lại.", "error"),
                    onCancelled: () => say("Đã đóng cửa sổ liên kết, chưa có tài khoản nào được thêm."),
                    onError: () => say("Cửa sổ liên kết gặp lỗi. Vui lòng thử lại sau.", "error"),
                    onClose: () => { if (!linked) delete card.dataset.busy; }
                });
                sdk.Url.Open(s.url);
                say("Hoàn tất các bước trong cửa sổ liên kết " + logo.title + ".");
            });
        })
        .catch(e => say(e.message && e.message !== "load" ? e.message : "Không mở được cửa sổ liên kết. Vui lòng thử lại.", "error"))
        .finally(() => { if (status.classList.contains("cp-alert")) delete card.dataset.busy; });
}, true);