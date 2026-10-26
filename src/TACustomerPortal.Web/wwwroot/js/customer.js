"use strict";
// Prevent accidental duplicate POSTs while the server processes a form.
document.addEventListener("submit", event => {
    if (!(event.target instanceof HTMLFormElement)) return;
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
