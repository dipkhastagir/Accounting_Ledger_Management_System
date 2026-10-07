// TroyeeLedger client helpers: voucher line editor, confirmations, chart defaults.
(function () {
  "use strict";
  const fmt = (n) => Number(n).toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const num = (el) => { const v = parseFloat(el && el.value); return isNaN(v) ? 0 : v; };
  const round2 = (n) => Math.round(n * 100) / 100;

  function initLineEditor(root) {
    const form = root.closest("form");
    const tbody = root.querySelector("tbody.lines");
    const tpl = document.getElementById(root.dataset.template || "line-template");
    let lastRow = null;

    const rows = () => Array.from(tbody.querySelectorAll("tr.line"));
    const q = (sel) => form.querySelector(sel);

    function reindex() {
      rows().forEach((tr, i) => {
        tr.querySelectorAll("[name]").forEach((el) => { el.name = el.name.replace(/Lines\[[^\]]+\]/, "Lines[" + i + "]"); });
        const n = tr.querySelector(".line-no");
        if (n) n.textContent = i + 1;
      });
    }

    function recalc() {
      let d = 0, c = 0;
      rows().forEach((tr) => {
        const dr = tr.querySelector(".f-debit"), cr = tr.querySelector(".f-credit");
        d += num(dr); c += num(cr);
        if (dr) dr.classList.toggle("dr-in", num(dr) > 0);
        if (cr) cr.classList.toggle("cr-in", num(cr) > 0);
      });
      d = round2(d); c = round2(c);
      const diff = round2(d - c);
      const set = (sel, text) => { const el = q(sel); if (el) el.textContent = text; };
      set("#total-dr", fmt(d));
      set("#total-cr", fmt(c));
      set("#diff", fmt(Math.abs(diff)));
      const st = q("#balance-state");
      if (st) {
        const ok = diff === 0 && d > 0;
        st.textContent = ok ? "Balanced — debits equal credits"
          : (d === 0 && c === 0) ? "Enter amounts"
          : diff > 0 ? "Credits are short by " + fmt(diff) : "Debits are short by " + fmt(-diff);
        st.className = "balance-state " + (ok ? "ok" : "off");
      }
    }

    function wire(tr) {
      tr.addEventListener("focusin", () => { lastRow = tr; });
      const dr = tr.querySelector(".f-debit"), cr = tr.querySelector(".f-credit");
      if (dr && cr) {
        dr.addEventListener("input", () => { if (num(dr) > 0) cr.value = ""; recalc(); });
        cr.addEventListener("input", () => { if (num(cr) > 0) dr.value = ""; recalc(); });
      }
      const rm = tr.querySelector(".remove-line");
      if (rm) rm.addEventListener("click", () => {
        if (rows().length <= 2) {
          tr.querySelectorAll("input").forEach((i) => { i.value = ""; });
          tr.querySelectorAll("select").forEach((s) => { s.selectedIndex = 0; });
        } else {
          tr.remove();
        }
        reindex(); recalc();
      });
    }

    function addRow() {
      const i = rows().length;
      tbody.insertAdjacentHTML("beforeend", tpl.innerHTML.split("__i__").join(String(i)));
      const tr = tbody.lastElementChild;
      wire(tr); reindex(); recalc();
      return tr;
    }

    function emptyRow() {
      const r = rows().reverse().find((tr) => num(tr.querySelector(".f-debit")) === 0 && num(tr.querySelector(".f-credit")) === 0);
      return r || addRow();
    }

    rows().forEach(wire);
    reindex();
    const add = q("#add-line");
    if (add) add.addEventListener("click", () => { const tr = addRow(); const s = tr.querySelector("select"); if (s) s.focus(); });

    const bal = q("#balance-line");
    if (bal) bal.addEventListener("click", () => {
      let d = 0, c = 0;
      rows().forEach((tr) => { d += num(tr.querySelector(".f-debit")); c += num(tr.querySelector(".f-credit")); });
      const diff = round2(d - c);
      if (diff === 0) return;
      const tr = emptyRow();
      if (diff > 0) tr.querySelector(".f-credit").value = diff.toFixed(2);
      else tr.querySelector(".f-debit").value = (-diff).toFixed(2);
      recalc();
      tr.querySelector("select").focus();
    });

    const taxBtn = q("#add-tax");
    if (taxBtn) taxBtn.addEventListener("click", () => {
      const sel = q("#tax-rate");
      const opt = sel && sel.selectedOptions[0];
      if (!opt || !opt.value) { sel && sel.focus(); return; }
      const source = (lastRow && (num(lastRow.querySelector(".f-debit")) || num(lastRow.querySelector(".f-credit")))) ? lastRow
        : rows().find((tr) => num(tr.querySelector(".f-debit")) || num(tr.querySelector(".f-credit")));
      if (!source) { alert("Enter the taxable amount on a line first, then add VAT."); return; }
      const base = num(source.querySelector(".f-debit")) || num(source.querySelector(".f-credit"));
      const tax = round2(base * parseFloat(opt.dataset.rate) / 100);
      const tr = emptyRow();
      tr.querySelector(".f-account").value = opt.dataset.account;
      if (opt.dataset.kind === "Input") tr.querySelector(".f-debit").value = tax.toFixed(2);
      else tr.querySelector(".f-credit").value = tax.toFixed(2);
      const note = tr.querySelector(".f-note");
      if (note && !note.value) note.value = opt.textContent.trim() + " on " + fmt(base);
      recalc();
    });

    recalc();
  }

  document.querySelectorAll("[data-line-editor]").forEach(initLineEditor);

  // Voucher-type guidance
  const hints = {
    Payment: "Payment — money leaves a cash or bank account. Credit the cash/bank account and debit what was paid for.",
    Receipt: "Receipt — money arrives in a cash or bank account. Debit the cash/bank account and credit the source.",
    Journal: "Journal — non-cash adjustments such as accruals, credit sales, depreciation. Cash and bank accounts are not allowed.",
    Contra: "Contra — transfers between cash and bank accounts only, e.g. depositing cash or withdrawing from the bank."
  };
  const typeSel = document.querySelector("[data-voucher-type]");
  const hintEl = document.getElementById("type-hint");
  if (typeSel && hintEl) {
    const show = () => { hintEl.textContent = hints[typeSel.selectedOptions[0].text] || ""; };
    typeSel.addEventListener("change", show); show();
  }

  // Confirmation prompts on forms with data-confirm
  document.addEventListener("submit", (e) => {
    const msg = e.target.getAttribute("data-confirm");
    if (msg && !window.confirm(msg)) e.preventDefault();
  });

  // Rejection reason prompt
  document.querySelectorAll("form[data-reject]").forEach((f) => {
    f.addEventListener("submit", (e) => {
      const reason = window.prompt("Tell the author what to fix:");
      if (reason === null) { e.preventDefault(); return; }
      f.querySelector("input[name=reason]").value = reason;
    });
  });

  if (window.Chart) {
    Chart.defaults.font.family = '"Public Sans", "Segoe UI", sans-serif';
    Chart.defaults.color = "#4C5A68";
    Chart.defaults.borderColor = "#DCE7E1";
    Chart.defaults.plugins.legend.labels.boxWidth = 12;
    Chart.defaults.maintainAspectRatio = false;
  }
  window.TL = { fmt: fmt, palette: ["#1E4636", "#A9792B", "#23497F", "#A3302A", "#5E8C77", "#7A6A9B", "#C9A86A", "#8AA3B5"] };
})();
