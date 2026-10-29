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
// Account menu (<details>): close after navigation, on outside click and on Escape.
const closeMenus = except => document.querySelectorAll("details[data-cp-menu][open]").forEach(menu => { if (menu !== except) menu.open = false; });
document.addEventListener("ta:load", () => closeMenus(null));
document.addEventListener("click", event => closeMenus(event.target.closest("details[data-cp-menu]")));
document.addEventListener("keydown", event => { if (event.key === "Escape") closeMenus(null); });
