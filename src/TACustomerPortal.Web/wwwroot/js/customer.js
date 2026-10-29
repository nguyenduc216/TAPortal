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
// "Chọn doanh nghiệp": live, diacritic-insensitive filter by company name or code.
window.taFilterCompanies = query => {
    const fold = text => text.normalize("NFD").replace(/[̀-ͯ]/g, "").replace(/đ/g, "d").replace(/Đ/g, "D").toLocaleLowerCase("vi");
    const value = fold(query.trim());
    let visible = 0;
    document.querySelectorAll("[data-ta-company]").forEach(form => {
        const match = fold(form.textContent).includes(value);
        form.hidden = !match;
        if (match) visible += 1;
    });
    document.querySelector("[data-ta-no-company]")?.classList.toggle("d-none", visible !== 0);
};
