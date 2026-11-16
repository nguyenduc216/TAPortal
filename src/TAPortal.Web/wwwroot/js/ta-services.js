/* Danh mục dịch vụ (operator 2026-11-16): the service form (Views/ServiceCatalog/Edit.cshtml).
   - "Cách tính phí" shows the fields of the chosen mode ([data-ta-svc-when="TIER FIXED"]).
   - "Hạn mức & phí": measures (label + unit, up to 3), tiers with a range per measure and the fee, or one fee per industry.
     The table is drawn from the hidden Input.PricingJson and written back on every change; the server checks it again
     (ServicePricingPolicy.Validate). Numbers are typed the Vietnamese way: 1.200 = một nghìn hai trăm, 1,5 = một phẩy năm.
   The shell swaps <main>, so listeners are delegated and the form is set up on ta:load. */
(function () {
  'use strict';
  const MAX_METRICS = 3;
  const nf = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 });

  function parseVi(text) {
    let s = String(text == null ? '' : text).replace(/\s|đ|Đ/g, '');
    if (!s) return null;
    if (/^\d{1,3}(\.\d{3})+(,\d+)?$/.test(s)) s = s.replace(/\./g, '').replace(',', '.');
    else s = s.replace(',', '.');
    const n = Number(s);
    return Number.isFinite(n) && n >= 0 ? n : NaN;
  }
  const show = (n) => (n == null || Number.isNaN(n) ? '' : nf.format(n));

  // Same rule as ServicePricingPolicy.AutoCode: "Số lượng chứng từ" -> "SLCT".
  const plain = (s) => String(s || '').replace(/đ/g, 'd').replace(/Đ/g, 'D').normalize('NFD').replace(/[\u0300-\u036f]/g, '');
  const autoCode = (label) => plain(label).split(/\s+/).map((w) => (w.match(/[A-Za-z0-9]/) || [''])[0]).join('').toUpperCase().slice(0, 6) || 'CS';
  const cleanCode = (code) => plain(code).toUpperCase().replace(/[^A-Z0-9_]/g, '').slice(0, 10);

  function el(tag, attrs, children) {
    const e = document.createElement(tag);
    Object.entries(attrs || {}).forEach(([k, v]) => {
      if (v == null || v === false) return;
      if (k === 'text') e.textContent = v;
      else if (k === 'dataset') Object.assign(e.dataset, v);
      else e.setAttribute(k, v === true ? '' : v);
    });
    (children || []).forEach((c) => c && e.appendChild(c));
    return e;
  }

  // ---- state ----------------------------------------------------------------------------------------------------------
  const states = new WeakMap();
  let seq = 0;

  function load(root) {
    let data = {};
    try { data = JSON.parse(root.querySelector('[data-ta-pricing-json]').value || '{}') || {}; } catch (e) { data = {}; }
    const metrics = (data.metrics || []).map((m) => ({ key: m.key || 'k' + (++seq), code: m.code || '', label: m.label || '', unit: m.unit || '' }));
    if (!metrics.length) metrics.push({ key: 'k' + (++seq), code: '', label: '', unit: '' });
    const tiers = (data.tiers || []).map((t) => ({ label: t.label || '', ranges: t.ranges || {}, fee: t.fee == null ? null : Number(t.fee), fees: t.fees || {} }));
    if (!tiers.length) tiers.push({ label: '', ranges: {}, fee: null, fees: {} });
    let industries = [];
    try { industries = JSON.parse(root.dataset.industries || '[]'); } catch (e) { industries = []; }
    return { metrics, byIndustry: !!data.byIndustry && industries.length > 0, tiers, industries };
  }

  function save(root) {
    const s = states.get(root);
    const out = {
      metrics: s.metrics.map((m) => ({ key: m.key, code: cleanCode(m.code) || autoCode(m.label), label: m.label.trim(), unit: m.unit.trim() || null })),
      byIndustry: s.byIndustry,
      tiers: s.tiers.map((t) => {
        const ranges = {};
        s.metrics.forEach((m) => { const r = t.ranges[m.key]; if (r && (r.min != null || r.max != null)) ranges[m.key] = { min: r.min, max: r.max }; });
        return { label: t.label.trim() || null, ranges, fee: s.byIndustry ? null : t.fee, fees: s.byIndustry ? t.fees : {} };
      })
    };
    root.querySelector('[data-ta-pricing-json]').value = JSON.stringify(out);
  }

  // ---- drawing --------------------------------------------------------------------------------------------------------
  function drawMetrics(root) {
    const s = states.get(root);
    const box = root.querySelector('[data-ta-pricing-metrics]');
    box.replaceChildren(...s.metrics.map((m, i) => el('div', { class: 'ta-pricing__metric', dataset: { key: m.key } }, [
      el('span', { class: 'ta-pricing__metric-no', text: String(i + 1) }),
      el('input', { class: 'form-control form-control-sm ta-pricing__code', value: m.code, maxlength: '10', placeholder: autoCode(m.label), title: 'Mã chỉ số: để trống thì lấy chữ đầu của từng từ', 'aria-label': 'Mã chỉ số ' + (i + 1), dataset: { taMetric: 'code' } }),
      el('input', { class: 'form-control form-control-sm', value: m.label, maxlength: '150', placeholder: 'Tên chỉ số, ví dụ: Số lượng chứng từ', 'aria-label': 'Tên chỉ số ' + (i + 1), dataset: { taMetric: 'label' } }),
      el('input', { class: 'form-control form-control-sm ta-pricing__unit', value: m.unit, maxlength: '50', placeholder: 'Đơn vị, ví dụ: chứng từ', 'aria-label': 'Đơn vị chỉ số ' + (i + 1), dataset: { taMetric: 'unit' } }),
      el('button', { type: 'button', class: 'ta-icon-btn', title: 'Bỏ chỉ số', 'aria-label': 'Bỏ chỉ số ' + (i + 1), dataset: { taMetricRemove: '' }, text: '×', disabled: s.metrics.length === 1 })
    ])));
    root.querySelector('[data-ta-pricing-add-metric]').hidden = s.metrics.length >= MAX_METRICS;
    root.querySelector('[data-ta-pricing-industry]').checked = s.byIndustry;
  }

  function drawTable(root) {
    const s = states.get(root);
    const form = root.closest('form');
    const feeLabel = (form && form.querySelector('[name="Input.FeeLabel"]')?.value.trim()) || 'Phí dịch vụ';
    const feeUnit = (form && form.querySelector('[name="Input.FeeUnit"]')?.value.trim()) || '';
    const feeCols = s.byIndustry ? s.industries : [{ id: '', name: feeLabel + (feeUnit ? ' (' + feeUnit + ')' : '') }];
    const head1 = el('tr', {}, [
      el('th', { scope: 'col', rowspan: '2', text: 'Mô tả bậc' }),
      ...s.metrics.map((m) => el('th', { scope: 'colgroup', colspan: '2', class: 'ta-pricing__metric-head', text: (m.label.trim() || 'Chỉ số') + (m.unit.trim() ? ' (' + m.unit.trim() + ')' : '') + ' · ' + (cleanCode(m.code) || autoCode(m.label)) })),
      el('th', { scope: 'colgroup', colspan: String(feeCols.length), class: 'ta-pricing__fee-head', text: s.byIndustry ? feeLabel + (feeUnit ? ' (' + feeUnit + ')' : '') + ' theo ngành nghề' : feeCols[0].name }),
      el('th', { scope: 'col', rowspan: '2', class: 'ta-pricing__actions' }, [el('span', { class: 'visually-hidden', text: 'Thao tác' })])
    ]);
    const head2 = el('tr', {}, [
      ...s.metrics.flatMap(() => [el('th', { scope: 'col', class: 'ta-pricing__sub', text: 'Từ' }), el('th', { scope: 'col', class: 'ta-pricing__sub', text: 'Đến' })]),
      ...feeCols.map((c) => el('th', { scope: 'col', class: 'ta-pricing__sub', text: s.byIndustry ? c.name : 'đồng' }))
    ]);
    const body = s.tiers.map((t, row) => el('tr', { dataset: { row: String(row) } }, [
      el('td', {}, [el('input', { class: 'form-control form-control-sm ta-pricing__label', value: t.label, maxlength: '150', placeholder: 'Tự ghi', 'aria-label': 'Mô tả bậc ' + (row + 1), dataset: { taTier: 'label' } })]),
      ...s.metrics.flatMap((m) => ['min', 'max'].map((side) => el('td', {}, [el('input', {
        class: 'form-control form-control-sm ta-pricing__num', inputmode: 'decimal', value: show(t.ranges[m.key] ? t.ranges[m.key][side] : null),
        'aria-label': (side === 'min' ? 'Từ ' : 'Đến ') + (m.label || 'chỉ số') + ', bậc ' + (row + 1), dataset: { taTier: side, metric: m.key }
      })]))),
      ...feeCols.map((c) => el('td', {}, [el('input', {
        class: 'form-control form-control-sm ta-pricing__num ta-pricing__fee', inputmode: 'numeric',
        value: show(s.byIndustry ? t.fees[c.id] : t.fee), 'aria-label': 'Phí ' + (s.byIndustry ? c.name : '') + ', bậc ' + (row + 1),
        dataset: { taTier: 'fee', industry: c.id }
      })])),
      el('td', { class: 'ta-pricing__actions' }, [el('div', { class: 'ta-actions' }, [
        el('button', { type: 'button', class: 'ta-icon-btn', title: 'Lên', 'aria-label': 'Đưa bậc ' + (row + 1) + ' lên', dataset: { taTierMove: '-1' }, text: '↑', disabled: row === 0 }),
        el('button', { type: 'button', class: 'ta-icon-btn', title: 'Xuống', 'aria-label': 'Đưa bậc ' + (row + 1) + ' xuống', dataset: { taTierMove: '1' }, text: '↓', disabled: row === s.tiers.length - 1 }),
        el('button', { type: 'button', class: 'ta-icon-btn', title: 'Xóa bậc', 'aria-label': 'Xóa bậc ' + (row + 1), dataset: { taTierRemove: '' }, text: '×' })
      ])])
    ]));
    root.querySelector('[data-ta-pricing-table]').replaceChildren(el('thead', {}, [head1, head2]), el('tbody', {}, body));
  }

  function draw(root) { drawMetrics(root); drawTable(root); save(root); }

  // ---- mode ------------------------------------------------------------------------------------------------------------
  function applyMode(form) {
    const mode = form.querySelector('[data-ta-svc-mode]:checked')?.value || 'TIER';
    form.querySelectorAll('[data-ta-svc-when]').forEach((x) => { x.hidden = !x.dataset.taSvcWhen.split(' ').includes(mode); });
  }

  function init() {
    document.querySelectorAll('[data-ta-svc-form]:not([data-ready])').forEach((form) => {
      form.dataset.ready = '1';
      applyMode(form);
      const root = form.querySelector('[data-ta-pricing]');
      if (root) { states.set(root, load(root)); draw(root); }
    });
  }

  // ---- events ----------------------------------------------------------------------------------------------------------
  document.addEventListener('input', (e) => {
    const t = e.target;
    const root = t.closest && t.closest('[data-ta-pricing]');
    if (!root || !states.has(root)) return;
    const s = states.get(root);
    if (t.dataset.taMetric) {
      const row = t.closest('[data-key]');
      const m = s.metrics.find((x) => x.key === row.dataset.key);
      m[t.dataset.taMetric] = t.value;
      if (t.dataset.taMetric === 'label') row.querySelector('[data-ta-metric="code"]').placeholder = autoCode(t.value);
      if (t.dataset.taMetric === 'code') { const c = cleanCode(t.value); if (c !== t.value) t.value = c; m.code = c; }
    } else if (t.dataset.taTier) {
      const tier = s.tiers[Number(t.closest('tr').dataset.row)];
      if (t.dataset.taTier === 'label') tier.label = t.value;
      else {
        const n = parseVi(t.value);
        t.classList.toggle('is-invalid', Number.isNaN(n));
        const v = Number.isNaN(n) ? null : n;
        if (t.dataset.taTier === 'fee') { if (t.dataset.industry) tier.fees[t.dataset.industry] = v; else tier.fee = v; }
        else { const r = tier.ranges[t.dataset.metric] || (tier.ranges[t.dataset.metric] = { min: null, max: null }); r[t.dataset.taTier] = v; }
      }
    }
    save(root);
  });

  document.addEventListener('change', (e) => {
    const t = e.target;
    const form = t.closest && t.closest('[data-ta-svc-form]');
    if (!form) return;
    if (t.matches('[data-ta-svc-mode]')) { applyMode(form); return; }
    const root = form.querySelector('[data-ta-pricing]');
    if (!root || !states.has(root)) return;
    if (t.matches('[data-ta-pricing-industry]')) { states.get(root).byIndustry = t.checked; draw(root); return; }
    // Headers follow the measure names and the fee label; numbers are shown formatted once typed.
    if (t.dataset.taMetric || t.name === 'Input.FeeLabel' || t.name === 'Input.FeeUnit') { drawTable(root); save(root); return; }
    if (t.dataset.taTier && t.dataset.taTier !== 'label') { const n = parseVi(t.value); if (!Number.isNaN(n)) t.value = show(n); }
  });

  document.addEventListener('click', (e) => {
    const t = e.target.closest && e.target.closest('button');
    if (!t) return;
    const root = t.closest('[data-ta-pricing]');
    if (!root || !states.has(root)) return;
    const s = states.get(root);
    if (t.matches('[data-ta-pricing-add-metric]') && s.metrics.length < MAX_METRICS) s.metrics.push({ key: 'k' + (++seq), code: '', label: '', unit: '' });
    else if (t.matches('[data-ta-metric-remove]') && s.metrics.length > 1) s.metrics = s.metrics.filter((m) => m.key !== t.closest('[data-key]').dataset.key);
    else if (t.matches('[data-ta-pricing-add-tier]')) s.tiers.push({ label: '', ranges: {}, fee: null, fees: {} });
    else if (t.matches('[data-ta-tier-remove]')) { s.tiers.splice(Number(t.closest('tr').dataset.row), 1); if (!s.tiers.length) s.tiers.push({ label: '', ranges: {}, fee: null, fees: {} }); }
    else if (t.matches('[data-ta-tier-move]')) {
      const i = Number(t.closest('tr').dataset.row), j = i + Number(t.dataset.taTierMove);
      if (j < 0 || j >= s.tiers.length) return;
      [s.tiers[i], s.tiers[j]] = [s.tiers[j], s.tiers[i]];
    } else return;
    e.preventDefault();
    draw(root);
    if (t.matches('[data-ta-pricing-add-tier]')) root.querySelector('tbody tr:last-child input')?.focus();
  });

  // Phí trọn gói: 10000000 -> 10.000.000.
  document.addEventListener('change', (e) => {
    const t = e.target;
    if (!t.matches || !t.matches('[data-ta-money]')) return;
    const n = parseVi(t.value);
    if (n != null && !Number.isNaN(n)) t.value = show(Math.round(n));
  });

  // A required field in a hidden tab: show that tab so the browser can point at it.
  document.addEventListener('invalid', (e) => {
    const form = e.target.closest && e.target.closest('[data-ta-svc-form]');
    const panel = e.target.closest && e.target.closest('[data-ta-tabpanel]');
    if (form && panel && panel.hidden) form.querySelector('[data-ta-tabbtn="' + panel.dataset.taTabpanel + '"]')?.click();
  }, true);

  // ---- Danh sách công việc of the service: add from the work catalogue, order with ↑ ↓ or by dragging, remove. ----------
  function renumber(box) {
    const items = [...box.querySelectorAll('[data-ta-svc-task-list] > li')];
    items.forEach((li, i) => {
      li.querySelector('.ta-svc-task__no').textContent = String(i + 1);
      li.querySelector('[data-ta-svc-task-move="-1"]').disabled = i === 0;
      li.querySelector('[data-ta-svc-task-move="1"]').disabled = i === items.length - 1;
    });
    box.querySelector('[data-ta-svc-task-empty]').hidden = items.length > 0;
    const count = box.closest('form')?.querySelector('[data-ta-svc-task-count]');
    if (count) count.textContent = String(items.length);
    const pick = box.querySelector('[data-ta-svc-task-pick]');
    const used = new Set(items.map((li) => li.dataset.id));
    [...pick.options].forEach((o) => { if (o.value) o.disabled = used.has(o.value); });
  }

  document.addEventListener('click', (e) => {
    const t = e.target.closest && e.target.closest('button');
    const box = t && t.closest('[data-ta-svc-tasks]');
    if (!box) return;
    const list = box.querySelector('[data-ta-svc-task-list]');
    if (t.matches('[data-ta-svc-task-add]')) {
      e.preventDefault();
      const pick = box.querySelector('[data-ta-svc-task-pick]');
      const o = pick.selectedOptions[0];
      if (!o || !o.value || list.querySelector('li[data-id="' + o.value + '"]')) return;
      const li = box.querySelector('[data-ta-svc-task-template]').content.firstElementChild.cloneNode(true);
      li.dataset.id = o.value;
      li.querySelector('[data-name]').textContent = o.dataset.name;
      li.querySelector('[data-meta]').textContent = o.dataset.meta;
      li.querySelector('input[type=hidden]').value = o.value;
      list.appendChild(li);
      pick.value = '';
      pick.dispatchEvent(new Event('change', { bubbles: true }));
    } else if (t.matches('[data-ta-svc-task-remove]')) {
      e.preventDefault();
      t.closest('li').remove();
    } else if (t.matches('[data-ta-svc-task-move]')) {
      e.preventDefault();
      const li = t.closest('li');
      if (t.dataset.taSvcTaskMove === '-1' && li.previousElementSibling) li.parentNode.insertBefore(li, li.previousElementSibling);
      else if (t.dataset.taSvcTaskMove === '1' && li.nextElementSibling) li.parentNode.insertBefore(li.nextElementSibling, li);
    } else return;
    renumber(box);
  });

  let dragged = null;
  document.addEventListener('dragstart', (e) => {
    const li = e.target.closest && e.target.closest('[data-ta-svc-task-list] > li');
    if (!li) return;
    dragged = li;
    li.classList.add('is-dragging');
    e.dataTransfer.effectAllowed = 'move';
    try { e.dataTransfer.setData('text/plain', li.dataset.id); } catch (err) { /* old browsers */ }
  });
  document.addEventListener('dragover', (e) => {
    if (!dragged) return;
    const over = e.target.closest && e.target.closest('[data-ta-svc-task-list] > li');
    if (!over || over === dragged || over.parentNode !== dragged.parentNode) return;
    e.preventDefault();
    const box = over.getBoundingClientRect();
    over.parentNode.insertBefore(dragged, e.clientY > box.top + box.height / 2 ? over.nextSibling : over);
  });
  document.addEventListener('dragend', () => {
    if (!dragged) return;
    dragged.classList.remove('is-dragging');
    renumber(dragged.closest('[data-ta-svc-tasks]'));
    dragged = null;
  });

  function initTasks() { document.querySelectorAll('[data-ta-svc-tasks]').forEach(renumber); }
  document.addEventListener('ta:load', () => { init(); initTasks(); });
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', () => { init(); initTasks(); }); else { init(); initTasks(); }
})();
