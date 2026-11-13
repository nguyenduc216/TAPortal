// T.A website: mobile menu (map loads on first open), testimonial slider, 3-step quote form (sent to POST /lien-he),
// article reading progress / table of contents. Ported from the approved mockups (dich-vu.js, trang-phu.js, script.js).
(() => {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => [...r.querySelectorAll(s)];

  /* ---------- Mobile menu ---------- */
  const toggle = $('.menu-toggle');
  const nav = $('#nav');
  toggle?.addEventListener('click', () => {
    const opened = nav.classList.toggle('open');
    toggle.setAttribute('aria-expanded', String(opened));
    const map = $('.nav-visit-map', nav);
    if (opened && map && !map.src && map.dataset.src && getComputedStyle($('.nav-visit', nav)).display !== 'none') map.src = map.dataset.src;
  });
  nav?.querySelectorAll('a').forEach(a => a.addEventListener('click', () => {
    nav.classList.remove('open');
    toggle?.setAttribute('aria-expanded', 'false');
  }));

  /* ---------- Tabs (service finder, scope by period) ---------- */
  $$('[role="tablist"]').forEach(list => {
    const tabs = $$('[role="tab"]', list);
    const select = tab => tabs.forEach(t => {
      const on = t === tab;
      t.setAttribute('aria-selected', String(on));
      t.tabIndex = on ? 0 : -1;
      const panel = document.getElementById(t.getAttribute('aria-controls'));
      if (panel) panel.hidden = !on;
    });
    tabs.forEach((tab, i) => {
      tab.addEventListener('click', () => select(tab));
      tab.addEventListener('keydown', e => {
        const dir = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[e.key];
        if (!dir) return;
        e.preventDefault();
        const next = tabs[(i + dir + tabs.length) % tabs.length];
        select(next); next.focus();
      });
    });
  });

  /* ---------- Checklist after company setup ---------- */
  const todo = $('.dv-todo');
  if (todo) {
    const boxes = $$('input[type=checkbox]', todo);
    const bar = $('.dv-progress-bar span'), label = $('.dv-progress-count');
    const update = () => {
      const done = boxes.filter(b => b.checked).length;
      if (bar) bar.style.width = (done / boxes.length * 100) + '%';
      if (label) label.textContent = done + '/' + boxes.length;
    };
    boxes.forEach(b => b.addEventListener('change', update));
    update();
  }

  /* ---------- Testimonials ---------- */
  const box = $('.v9-testimonials[data-quotes]');
  if (box) {
    const quotes = JSON.parse(box.dataset.quotes || '[]');
    const note = $('.v9-quote-author', box)?.textContent || '';
    let i = 0;
    const show = () => {
      $('.v9-quote-text', box).textContent = quotes[i].text;
      const author = $('.v9-quote-author', box);
      author.textContent = quotes[i].author ? '— ' + quotes[i].author : note;
      author.classList.toggle('ta-todo', !quotes[i].author && note.startsWith('T.A bổ sung'));
      $$('.v9-dots span', box).forEach((d, k) => d.classList.toggle('active', k === i));
    };
    $('.v9-slide-prev', box)?.addEventListener('click', () => { i = (i + quotes.length - 1) % quotes.length; show(); });
    $('.v9-slide-next', box)?.addEventListener('click', () => { i = (i + 1) % quotes.length; show(); });
  }

  /* ---------- Quote request form ---------- */
  const form = $('#quote-form');
  if (form) {
    const panels = $$('.ct-panel', form);
    const stepsUi = $$('.ct-steps li');
    const prev = $('#ct-prev'), next = $('#ct-next'), submit = $('#ct-submit');
    const err = $('#ct-error'), counter = $('#ct-counter');
    let step = 0;

    const labelOf = i => i.closest('label')?.querySelector('b')?.textContent.trim() || i.value;
    const summary = () => {
      $('#sum-service').textContent = $$('input[name=service]:checked', form).map(labelOf).join(', ');
      const type = $('input[name=type]:checked', form);
      $('#sum-type').textContent = type ? labelOf(type) : '';
      const vol = $('#f-volume'), staff = $('#f-staff');
      $('#sum-scale').textContent = [vol.value, staff.value].filter(Boolean).join(' · ');
      $('#sum-time').textContent = $('#f-time').value;
    };

    const show = (i, focus = true) => {
      step = i;
      panels.forEach((p, k) => (p.hidden = k !== i));
      stepsUi.forEach((li, k) => {
        li.classList.toggle('is-active', k === i);
        li.classList.toggle('is-done', k < i);
        if (k === i) li.setAttribute('aria-current', 'step'); else li.removeAttribute('aria-current');
      });
      prev.hidden = i === 0;
      next.hidden = i === panels.length - 1;
      submit.hidden = i !== panels.length - 1;
      counter.textContent = `Bước ${i + 1}/${panels.length}`;
      err.textContent = '';
      if (focus) {
        panels[i].querySelector('h2')?.focus({ preventScroll: true });
        if (form.getBoundingClientRect().top < 100) form.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    };

    const validate = () => {
      $$('.ct-field.invalid', form).forEach(f => f.classList.remove('invalid'));
      if (step === 0 && !$('input[name=service]:checked', form)) return 'Vui lòng chọn ít nhất một dịch vụ quan tâm.';
      if (step === 1 && !$('input[name=type]:checked', form)) return 'Vui lòng chọn loại hình / tình trạng doanh nghiệp.';
      if (step === 2) {
        const bad = [];
        const name = $('#f-name'), phone = $('#f-phone'), email = $('#f-email');
        if (!name.value.trim()) bad.push(name);
        if (!/^(0|\+84)[0-9 .]{8,12}$/.test(phone.value.trim())) bad.push(phone);
        if (email.value && !email.checkValidity()) bad.push(email);
        bad.forEach(el => el.closest('.ct-field').classList.add('invalid'));
        if (bad.length) { bad[0].focus(); return 'Vui lòng nhập họ tên và số điện thoại hợp lệ; email (nếu có) cần đúng định dạng.'; }
        if (!$('#f-consent').checked) return 'Vui lòng đồng ý để T.A sử dụng thông tin nhằm liên hệ tư vấn.';
      }
      return '';
    };

    next.addEventListener('click', () => { const m = validate(); if (m) { err.textContent = m; return; } show(step + 1); });
    prev.addEventListener('click', () => show(step - 1));
    form.addEventListener('change', summary);
    form.addEventListener('submit', async e => {
      e.preventDefault();
      const m = validate(); if (m) { err.textContent = m; return; }
      submit.disabled = true;
      submit.textContent = 'ĐANG GỬI…';
      try {
        const response = await fetch(form.action, { method: 'POST', body: new FormData(form), headers: { Accept: 'application/json' }, credentials: 'same-origin' });
        const data = await response.json().catch(() => ({}));
        if (!response.ok || !data.ok) { err.textContent = data.error || 'Chưa gửi được yêu cầu. Vui lòng thử lại hoặc gọi hotline.'; return; }
        $('#ct-done-name').textContent = $('#f-name').value.trim();
        $('.ct-wizard').hidden = true;
        $('#ct-done').hidden = false;
        $('#ct-done h2').focus();
      } catch {
        err.textContent = 'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.';
      } finally {
        submit.disabled = false;
        submit.textContent = 'GỬI YÊU CẦU';
      }
    });
    $('#ct-restart')?.addEventListener('click', () => {
      const token = $('input[name=__RequestVerificationToken]', form)?.value;
      form.reset();
      if (token) $('input[name=__RequestVerificationToken]', form).value = token;
      summary();
      $('.ct-wizard').hidden = false; $('#ct-done').hidden = true; show(0);
    });
    summary(); show(0, false);
  }

  /* ---------- Article ---------- */
  const article = $('.ar-body');
  if (article) {
    const bar = $('.ar-progress');
    const links = $$('.ar-toc a');
    const heads = links.map(a => document.getElementById(decodeURIComponent(a.getAttribute('href').slice(1)))).filter(Boolean);
    const onScroll = () => {
      const r = article.getBoundingClientRect();
      const total = r.height - innerHeight * 0.6;
      if (bar) bar.style.width = Math.min(100, Math.max(0, (-r.top / total) * 100)) + '%';
      let idx = 0;
      heads.forEach((h, i) => { if (h.getBoundingClientRect().top < Math.max(260, innerHeight * 0.32)) idx = i; });
      links.forEach((a, i) => a.classList.toggle('is-active', i === idx));
    };
    addEventListener('scroll', onScroll, { passive: true }); onScroll();
    $('#ar-copy')?.addEventListener('click', async e => {
      try { await navigator.clipboard.writeText(location.href); e.target.textContent = 'Đã sao chép ✓'; }
      catch { e.target.textContent = 'Không sao chép được'; }
    });
    $('#ar-print')?.addEventListener('click', () => print());
  }
})();
