"use strict";
// Full-page submits only (sign-in, sign-out): forms inside the portal shell are sent in the background by ta-shell.js.
document.addEventListener("submit", event => {
    if (!(event.target instanceof HTMLFormElement) || event.defaultPrevented) return;
    // Sent to a new tab (e.g. "Xem thử hóa đơn nháp"): this page stays, so its buttons stay usable.
    const target = event.submitter?.hasAttribute("formtarget") ? event.submitter.getAttribute("formtarget") : event.target.target;
    if (target && target !== "_self") return;
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
// An empty line: no product, quantity 1, first tax rate.
const clearLine = row => {
    row.querySelectorAll("input").forEach(input => {
        input.value = input.hasAttribute("data-cp-qty") ? "1" : input.hasAttribute("data-cp-remember") ? "false" : "";
    });
    row.querySelectorAll("select").forEach(select => { select.selectedIndex = 0; });
    delete row.dataset.cpUsual;
    delete row.dataset.cpCatalog;
    row.querySelector("[data-cp-line-thumb]")?.setAttribute("hidden", "");
};
const reindexLines = table => table.querySelectorAll("tbody tr[data-cp-line]").forEach((row, index) =>
    row.querySelectorAll("[name^='Lines[']").forEach(input => { input.name = input.name.replace(/^Lines\[\d+\]/, `Lines[${index}]`); }));
// Prices are typed including VAT (PricesIncludeTax), like the product catalogue.
const includesTax = form => form.querySelector("input[name=PricesIncludeTax]")?.value === "true";
const updateTotals = form => {
    let total = 0;
    form.querySelectorAll("tr[data-cp-line]").forEach(row => {
        const qty = parseFloat(row.querySelector("[data-cp-qty]")?.value) || 0;
        const typed = parseFloat(row.querySelector("[data-cp-price]")?.value) || 0;
        const rate = Math.max(0, parseInt(row.querySelector("[data-cp-tax]")?.value, 10) || 0);
        // Including VAT the customer pays exactly qty × price (OrderMath.LineWithTax); before tax, tax is added per line.
        const before = Math.round(qty * typed);
        const line = includesTax(form) ? before : before + Math.round(before * rate / 100);
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
        clearLine(clone);
        table.querySelector("tbody").appendChild(clone);
        clone.querySelector("input:not([type=hidden])")?.focus();
    } else if (rows.length > 1) {
        remove.closest("tr").remove();
    } else {
        clearLine(rows[0]);
    }
    reindexLines(table);
    updateTotals(form);
});
document.addEventListener("input", event => {
    const form = event.target.closest?.("form[data-cp-order-form]");
    if (form) updateTotals(form);
});
// Order form, product catalogue: "Chọn sản phẩm" opens a grid of the active products (image, name, price); a click adds
// the product as a line, or one more of it. The price offered is the one kept for the selected customer, else the
// catalogue price (both include VAT). On save, product lines whose price differs from that usual price ask whether to keep
// the new price for this customer (RememberPrice); the server stores the ones ticked.
const readJson = (form, selector, fallback) => { try { return JSON.parse(form.querySelector(selector)?.textContent || ""); } catch { return fallback; } };
const productsOf = form => form._cpProducts ??= readJson(form, "script[data-cp-products]", []);
const buyerPricesOf = form => form._cpBuyerPrices ??= readJson(form, "script[data-cp-buyer-prices]", {});
const usualPrice = (form, product) => buyerPricesOf(form)[product.id] ?? product.price;
const lineProduct = row => row.querySelector("[data-cp-product-id]")?.value || "";
const renderProducts = (form, filter = "") => {
    const grid = form.querySelector("[data-cp-products-grid]");
    if (!grid) return;
    const words = filter.trim().toLowerCase();
    const picked = new Set([...form.querySelectorAll("tr[data-cp-line]")].map(lineProduct));
    const cards = productsOf(form).filter(p => !words || p.name.toLowerCase().includes(words)).map(p => {
        const card = document.createElement("button");
        card.type = "button";
        card.className = "cp-product-card" + (picked.has(p.id) ? " is-picked" : "");
        card.dataset.cpPick = p.id;
        const pic = document.createElement("span");
        pic.className = "cp-product-card__img";
        if (p.image) { const img = document.createElement("img"); img.src = p.image; img.alt = ""; img.loading = "lazy"; pic.append(img); }
        else pic.textContent = p.name.slice(0, 1).toUpperCase();
        const name = document.createElement("strong");
        name.textContent = p.name;
        const price = document.createElement("span");
        price.className = "cp-product-card__price";
        const usual = usualPrice(form, p);
        price.textContent = money(usual) + (p.unit ? " / " + p.unit : "");
        card.append(pic, name, price);
        if (usual !== p.price) { const tag = document.createElement("span"); tag.className = "cp-badge cp-badge--gold"; tag.textContent = "Giá riêng"; card.append(tag); }
        return card;
    });
    if (cards.length) grid.replaceChildren(...cards); else grid.textContent = "Không có sản phẩm phù hợp.";
};
const fillLine = (form, row, p) => {
    const set = (selector, value) => { const el = row.querySelector(selector); if (el) el.value = value; };
    const usual = usualPrice(form, p);
    set("[name$='.ItemName']", p.name);
    set("[name$='.UnitName']", p.unit || "");
    set("[data-cp-qty]", "1");
    set("[data-cp-price]", String(usual));
    set("[data-cp-tax]", p.tax);
    set("[data-cp-product-id]", p.id);
    set("[data-cp-remember]", "false");
    row.dataset.cpUsual = String(usual);
    row.dataset.cpCatalog = String(p.price);
    const thumb = row.querySelector("[data-cp-line-thumb]");
    if (thumb) {
        thumb.removeAttribute("hidden");
        if (p.image) { const img = document.createElement("img"); img.src = p.image; img.alt = ""; thumb.replaceChildren(img); }
    }
};
const pickProduct = (form, id) => {
    const p = productsOf(form).find(x => x.id === id);
    const table = form.querySelector("#orderLines");
    if (!p || !table) return;
    const rows = [...table.querySelectorAll("tbody tr[data-cp-line]")];
    const same = rows.find(r => lineProduct(r) === id);
    if (same) {
        const qty = same.querySelector("[data-cp-qty]");
        qty.value = String((parseFloat(qty.value) || 0) + 1);
    } else {
        let row = rows.find(r => !r.querySelector("[name$='.ItemName']")?.value && !r.querySelector("[data-cp-price]")?.value);
        if (!row) {
            if (rows.length >= 50) return;
            row = rows[rows.length - 1].cloneNode(true);
            clearLine(row);
            table.querySelector("tbody").appendChild(row);
            reindexLines(table);
        }
        fillLine(form, row, p);
    }
    delete form.dataset.cpPricesAsked;
    updateTotals(form);
    renderProducts(form, form.querySelector("[data-cp-products-filter]")?.value || "");
};
document.addEventListener("click", event => {
    const form = event.target.closest?.("form[data-cp-order-form]");
    if (!form) return;
    if (event.target.closest("[data-cp-open-products]")) {
        const dialog = form.querySelector("[data-cp-products-dialog]");
        renderProducts(form);
        dialog?.showModal();
    } else if (event.target.closest("[data-cp-pick]")) {
        pickProduct(form, event.target.closest("[data-cp-pick]").dataset.cpPick);
    } else if (event.target.closest("[data-cp-dialog-close]")) {
        event.target.closest("dialog")?.close();
    } else if (event.target.closest("[data-cp-remember-confirm]")) {
        const dialog = event.target.closest("dialog");
        const rows = form.querySelectorAll("tr[data-cp-line]");
        dialog.querySelectorAll("input[data-cp-remember-row]").forEach(box => {
            const hidden = rows[Number(box.dataset.cpRememberRow)]?.querySelector("[data-cp-remember]");
            if (hidden) hidden.value = box.checked ? "true" : "false";
        });
        dialog.close();
        form.dataset.cpPricesAsked = "true";
        form.requestSubmit(form.querySelector("button[type=submit]"));
    }
});
document.addEventListener("input", event => {
    const form = event.target.closest?.("form[data-cp-order-form]");
    if (!form) return;
    if (event.target.matches("[data-cp-products-filter]")) renderProducts(form, event.target.value);
    // A changed price asks again on the next save.
    if (event.target.matches("[data-cp-price]")) delete form.dataset.cpPricesAsked;
});
// Another customer: their kept prices replace the prices of product lines still at the previous usual price.
document.addEventListener("change", async event => {
    const form = event.target.closest?.("form[data-cp-order-form]");
    if (!form || !event.target.matches("[data-cp-contact]")) return;
    let prices = {};
    if (event.target.value) {
        try {
            const url = new URL(form.dataset.cpPricesUrl, location.href);
            url.searchParams.set("contactId", event.target.value);
            const response = await fetch(url, { headers: { Accept: "application/json" }, credentials: "same-origin" });
            if (response.ok) prices = await response.json();
        } catch { /* keep the catalogue prices */ }
    }
    form._cpBuyerPrices = prices;
    form.querySelectorAll("tr[data-cp-line]").forEach(row => {
        const p = productsOf(form).find(x => x.id === lineProduct(row));
        if (!p) return;
        const price = row.querySelector("[data-cp-price]");
        const next = usualPrice(form, p);
        if (Number(price.value) === Number(row.dataset.cpUsual)) price.value = String(next);
        row.dataset.cpUsual = String(next);
    });
    delete form.dataset.cpPricesAsked;
    updateTotals(form);
});
// Save: product lines of a named customer whose price differs from the usual one → ask which prices to keep. Capture
// phase, before ta-shell.js sends the form in the background.
document.addEventListener("submit", event => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement) || !form.matches("[data-cp-order-form]") || form.dataset.cpPricesAsked === "true") return;
    const contact = form.querySelector("[data-cp-contact]");
    const rows = [...form.querySelectorAll("tr[data-cp-line]")];
    rows.forEach(r => { const h = r.querySelector("[data-cp-remember]"); if (h) h.value = "false"; });
    if (!contact?.value) return;
    const changed = rows.map((row, index) => ({ row, index })).filter(({ row }) => {
        const price = row.querySelector("[data-cp-price]")?.value;
        return lineProduct(row) && price !== "" && row.dataset.cpUsual !== undefined && Number(price) !== Number(row.dataset.cpUsual);
    });
    const dialog = form.querySelector("[data-cp-remember-dialog]");
    if (!changed.length || !dialog) return;
    event.preventDefault();
    event.stopImmediatePropagation();
    dialog.querySelector("[data-cp-remember-buyer]").textContent = contact.selectedOptions[0]?.textContent || "";
    dialog.querySelector("[data-cp-remember-list]").replaceChildren(...changed.map(({ row, index }) => {
        const label = document.createElement("label");
        label.className = "cp-check cp-remember-row";
        const box = document.createElement("input");
        box.type = "checkbox";
        box.checked = true;
        box.dataset.cpRememberRow = String(index);
        const text = document.createElement("span");
        const name = document.createElement("strong");
        name.textContent = row.querySelector("[name$='.ItemName']")?.value || "";
        const detail = document.createElement("span");
        detail.className = "cp-muted";
        detail.style.display = "block";
        const usual = Number(row.dataset.cpUsual), catalog = Number(row.dataset.cpCatalog);
        detail.textContent = `${money(usual)} → ${money(Number(row.querySelector("[data-cp-price]").value))}` + (catalog !== usual ? ` (giá danh mục ${money(catalog)})` : "");
        text.append(name, detail);
        label.append(box, text);
        return label;
    }));
    dialog.showModal();
}, true);
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
// QR lifetime (_QrLifetime partial): shows the total of days/hours/minutes as it is typed. The server adds them up again.
document.addEventListener("input", event => {
    const box = event.target.closest("[data-cp-duration]");
    const total = box?.parentElement.querySelector("[data-cp-duration-total]");
    if (!total) return;
    const [d, h, m] = [...box.querySelectorAll("input")].map(i => Math.max(0, parseInt(i.value, 10) || 0));
    const parts = [d && d + " ngày", h && h + " giờ", m && m + " phút"].filter(Boolean);
    const minutes = d * 1440 + h * 60 + m;
    total.textContent = (parts.length ? parts.join(" ") : "0 phút") + (minutes > 43200 ? " (quá 30 ngày)" : minutes < 1 ? " (tối thiểu 1 phút)" : "");
});// Tax code lookup (sign-up and buyer forms): fills the registered name and address of an enterprise tax code. "Tra cứu"
// replaces the fields; leaving the tax code field only fills fields that are still empty.
const taxLookup = (button, overwrite) => {
    const field = button.closest(".cp-field");
    const input = field?.querySelector("[data-cp-tax-input]");
    const msg = field?.querySelector("[data-cp-tax-msg]");
    const code = (input?.value || "").replace(/\s+/g, "");
    if (!input || !msg) return;
    const say = (text, tone) => { msg.textContent = text; msg.classList.toggle("is-ok", tone === "ok"); msg.classList.toggle("is-error", tone === "error"); };
    if (!/^\d{10}(-\d{3})?$/.test(code)) { if (overwrite) say("Chỉ tra cứu được mã số thuế doanh nghiệp 10 chữ số (hoặc 10-3).", "error"); return; }
    if (button.dataset.busy === code) return;
    button.dataset.busy = code;
    button.disabled = true;
    say("Đang tra cứu mã số thuế…");
    fetch(button.dataset.cpTaxLookup + "?code=" + encodeURIComponent(code), { credentials: "same-origin", headers: { Accept: "application/json" } })
        .then(r => r.ok ? r.json() : Promise.reject(new Error(r.status === 429 ? "Bạn tra cứu quá nhanh, vui lòng đợi một chút." : "Tạm thời chưa tra cứu được. Vui lòng nhập tay.")))
        .then(s => {
            if (!s.ok) { say(s.message, "error"); return; }
            const form = button.closest("form");
            [[button.dataset.cpFillName, s.name], [button.dataset.cpFillAddress, s.address]].forEach(([selector, value]) => {
                const target = selector && value ? form.querySelector(selector) : null;
                if (target && (overwrite || !target.value.trim())) target.value = value;
            });
            say(s.message + " " + s.name, "ok");
        })
        .catch(e => say(e.message, "error"))
        .finally(() => { button.disabled = false; delete button.dataset.busy; });
};
document.addEventListener("click", event => {
    const button = event.target.closest("[data-cp-tax-lookup]");
    if (button) taxLookup(button, true);
});
document.addEventListener("change", event => {
    if (!event.target.matches("[data-cp-tax-input]")) return;
    const button = event.target.closest(".cp-field")?.querySelector("[data-cp-tax-lookup]");
    if (button && !button.closest("[data-cp-when]")?.hidden) taxLookup(button, false);
});
// Buyer form: fields follow the chosen buyer type (data-cp-when="BUSINESS HOUSEHOLD" shows a block only for those types;
// data-cp-required-when marks inputs required only for those types). The server applies the same rules.
const applyBuyerType = root => {
    const type = root.querySelector("input[name='BuyerType']:checked")?.value || "";
    root.querySelectorAll("[data-cp-when]").forEach(el => { el.hidden = !el.dataset.cpWhen.split(" ").includes(type); });
    root.querySelectorAll("[data-cp-required-when]").forEach(el => { el.required = el.dataset.cpRequiredWhen.split(" ").includes(type); });
    root.querySelectorAll("[data-cp-label-when]").forEach(el => { el.classList.toggle("is-required", el.dataset.cpLabelWhen.split(" ").includes(type)); });
};
const initBuyerForms = () => document.querySelectorAll("[data-cp-buyer-form]").forEach(applyBuyerType);
document.addEventListener("change", event => {
    const root = event.target.matches("input[name='BuyerType']") && event.target.closest("[data-cp-buyer-form]");
    if (root) applyBuyerType(root);
});
document.addEventListener("ta:load", initBuyerForms);
if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initBuyerForms); else initBuyerForms();

// Public payment page (/pay/{token}): countdown to the QR expiry. The server's clock is the reference (data-cp-countdown-now),
// so a wrong device clock does not shorten it; at zero the page reloads and the server shows the expired state.
const startCountdowns = () => document.querySelectorAll("[data-cp-countdown]").forEach(box => {
    if (box.dataset.cpTicking) return;
    box.dataset.cpTicking = "1";
    const end = Date.parse(box.dataset.cpCountdown);
    const skew = Date.now() - Date.parse(box.dataset.cpCountdownNow || new Date().toISOString());
    const out = box.querySelector("[data-cp-countdown-value]");
    const two = n => String(n).padStart(2, "0");
    const tick = () => {
        const left = Math.max(0, Math.floor((end - (Date.now() - skew)) / 1000));
        const days = Math.floor(left / 86400), h = Math.floor(left % 86400 / 3600), m = Math.floor(left % 3600 / 60), s = left % 60;
        out.textContent = (days ? days + " ngày " : "") + two(h) + ":" + two(m) + ":" + two(s);
        box.classList.toggle("is-ending", left > 0 && left < 300);
        if (left > 0) return;
        clearInterval(timer);
        box.classList.add("is-over");
        out.textContent = "Đã hết hạn";
        setTimeout(() => location.reload(), 2500);
    };
    const timer = setInterval(tick, 1000);
    tick();
});
// Forms whose small save button only appears once a field differs from what is stored (data-cp-saved).
const syncDirty = form => {
    const dirty = [...form.querySelectorAll("[data-cp-saved]")].some(i => i.value.trim() !== (i.dataset.cpSaved || ""));
    // After a refused save (validation messages shown) the button stays, so the corrected values can be sent again.
    const invalid = !!form.querySelector(".field-validation-error, .validation-summary-errors");
    form.querySelectorAll("[data-cp-dirty-save]").forEach(b => { b.hidden = !dirty && !invalid; });
};
const initDirtyForms = () => document.querySelectorAll("form[data-cp-dirty-form]").forEach(syncDirty);
document.addEventListener("input", event => { const form = event.target.closest?.("form[data-cp-dirty-form]"); if (form) syncDirty(form); });
// The tax code lookup fills the name without an input event: re-check once it answered.
document.addEventListener("click", event => {
    const form = event.target.closest("form[data-cp-dirty-form]");
    if (form && event.target.closest("[data-cp-tax-lookup]")) setTimeout(() => syncDirty(form), 1500);
});
document.addEventListener("change", event => { const form = event.target.closest?.("form[data-cp-dirty-form]"); if (form) setTimeout(() => syncDirty(form), 1500); });
// Order list: a long description shows one line; tapping it opens or closes the rest.
document.addEventListener("click", event => {
    const text = event.target.closest("[data-cp-clamp]");
    if (!text) return;
    const open = text.classList.toggle("is-open");
    text.setAttribute("aria-expanded", open ? "true" : "false");
});
// Order details: copy the payment page link.
document.addEventListener("click", event => {
    const button = event.target.closest("[data-cp-copy]");
    const source = button?.closest("[data-cp-paylink]")?.querySelector("[data-cp-copy-source]");
    if (!source) return;
    const done = () => { const text = button.querySelector("span"); if (text) { text.textContent = "Đã sao chép"; setTimeout(() => { text.textContent = "Sao chép"; }, 2000); } };
    if (navigator.clipboard) navigator.clipboard.writeText(source.value).then(done, () => { source.select(); });
    else { source.select(); document.execCommand?.("copy"); done(); }
});
// Logo pickers: preview the chosen file before saving.
document.addEventListener("change", event => {
    if (!event.target.matches?.("input[type=file][data-cp-logo-input]")) return;
    const file = event.target.files?.[0];
    const box = event.target.closest("[data-cp-logo-picker]");
    const img = box?.querySelector("[data-cp-logo-preview]");
    if (!file || !img) return;
    img.src = URL.createObjectURL(file);
    img.hidden = false;
    box.querySelector("[data-cp-logo-empty]")?.setAttribute("hidden", "");
});
// Status cards that slide on phones: bring the selected one into view without scrolling the page.
const showCurrentStat = () => document.querySelectorAll(".cp-stats").forEach(row => {
    const current = row.querySelector("[aria-current=page]");
    if (current && row.scrollWidth > row.clientWidth) row.scrollLeft = current.offsetLeft - row.offsetLeft - 12;
});
const initPayPage = () => { startCountdowns(); initDirtyForms(); showCurrentStat(); };
document.addEventListener("ta:load", initPayPage);
if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initPayPage); else initPayPage();

// Sign-up (operator 2026-11-12): "Cá nhân" makes the tax code optional, hides the lookup and calls the name "Họ và tên".
// The server applies the same rules.
const applySignupType = form => {
    const individual = form.querySelector("input[name='CustomerType']:checked")?.value === "INDIVIDUAL";
    const tax = form.querySelector("[data-cp-tax-input]");
    if (tax) tax.required = !individual;
    const lookup = form.querySelector("[data-cp-tax-lookup]");
    if (lookup) lookup.hidden = individual;
    const taxLabel = form.querySelector("[data-cp-signup-tax-label]");
    if (taxLabel) taxLabel.textContent = individual ? "Mã số thuế cá nhân (không bắt buộc)" : "Mã số thuế";
    const nameLabel = form.querySelector("[data-cp-signup-name-label]");
    if (nameLabel) nameLabel.textContent = individual ? "Họ và tên" : "Tên doanh nghiệp / hộ kinh doanh";
    const msg = form.querySelector("[data-cp-tax-msg]");
    if (msg && !msg.classList.contains("is-ok") && !msg.classList.contains("is-error"))
        msg.textContent = individual
            ? "Cá nhân không có mã số thuế vẫn đăng ký được; khi đó chỉ dùng phần thu tiền, chưa xuất hóa đơn điện tử đứng tên mình."
            : "Nhập MST rồi bấm Tra cứu (hoặc rời ô) để tự điền tên và địa chỉ.";
};
document.addEventListener("change", event => {
    const form = event.target.matches("input[name='CustomerType']") && event.target.closest("[data-cp-signup]");
    if (form) applySignupType(form);
});
// Terms of service: the link opens the dialog (the page /account/terms without script); "Tôi đồng ý" ticks the box.
document.addEventListener("click", event => {
    const dialog = document.querySelector("[data-cp-terms-dialog]");
    if (!dialog) return;
    if (event.target.closest("[data-cp-terms-open]") && typeof dialog.showModal === "function") {
        event.preventDefault();
        dialog.showModal();
    } else if (event.target.closest("[data-cp-terms-close]")) {
        dialog.close();
    } else if (event.target.closest("[data-cp-terms-agree]")) {
        const box = document.querySelector("[data-cp-terms-accept]");
        if (box) box.checked = true;
        dialog.close();
    } else if (event.target === dialog) {
        dialog.close();
    }
});
