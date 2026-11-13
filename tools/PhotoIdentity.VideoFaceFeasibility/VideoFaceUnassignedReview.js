/* Private WI-0155 unassigned-candidate human review.
 * No network requests, no identities, no storage, no application integration.
 * Only explicitly requested CSV save/import to the local browser's file system.
 */
(() => {
  'use strict';

  const columns = ['Clip', 'Sample', 'Frame', 'Face', 'Label', 'PersonAlias'];
  const cards = Array.from(document.querySelectorAll('article[data-sample]'));
  const status = document.getElementById('audit-status');
  const importFile = document.getElementById('audit-file');
  const hide = document.getElementById('hide-nonface');
  const labels = new Set(['', 'Real face', 'Not a face', 'Cannot tell']);
  const aliasPattern = /^(?:P(?:\d{2}|\d{4})|UNKNOWN)$/;

  function validRow(row) {
    const label = row.Label;
    const alias = row.PersonAlias;
    return labels.has(label) &&
      (alias === '' || (label === 'Real face' && aliasPattern.test(alias)));
  }

  function sampleRows() {
    return cards.map(card => {
      const label = card.querySelector('.audit-label').value;
      const person = card.querySelector('.audit-person').value.trim().toUpperCase();
      return {
        Clip: 'V03', // overridden by the gallery's own clip below
        Sample: card.dataset.sample,
        Frame: card.dataset.frame,
        Face: card.dataset.face,
        Label: label,
        PersonAlias: person
      };
    });
  }

  // The HTML element's own clip source is inserted as an immutable data field.
  const clip = document.querySelector('main').dataset.clip;
  if (!/^V(?!00)\d{2}$/.test(clip)) return;
  function gather() {
    const rows = sampleRows();
    rows.forEach(row => { row.Clip = clip; });
    return rows;
  }

  function refresh() {
    let reviewed = 0;
    for (const card of cards) {
      const label = card.querySelector('.audit-label').value;
      const person = card.querySelector('.audit-person');
      person.disabled = label !== 'Real face';
      if (label !== 'Real face') person.value = '';
      if (label) reviewed++;
      card.hidden = hide.checked && label === 'Not a face';
    }
    status.textContent = reviewed + ' / ' + cards.length + ' reviewed';
  }

  // Parse conventional quoted CSV (including Excel's doubled quotes);
  // imported files never execute as HTML, formulae or JavaScript.
  function parseCsv(raw) {
    if (raw.length > 64 * 1024) throw new Error('Too many CSV bytes');
    const rows = [];
    let row = [];
    let cell = '';
    let quoted = false;
    let closing = false;
    for (let i = 0; i < raw.length; i++) {
      const c = raw[i];
      if (quoted) {
        if (c === '"') {
          if (raw[i + 1] === '"') { cell += '"'; i++; }
          else { quoted = false; closing = true; }
        } else { cell += c; }
      } else if (c === '"') {
        if (cell !== '' || closing) throw new Error('Invalid CSV quoting');
        quoted = true;
      } else if (c === ',' || c === '\n') {
        row.push(cell);
        cell = '';
        closing = false;
        if (c === '\n') {
          if (row.some(x => x !== '')) rows.push(row);
          row = [];
        }
      } else if (c === '\r') {
        if (raw[i + 1] !== '\n') throw new Error('Unexpected newline');
      } else {
        if (closing) throw new Error('Trailing text after quote');
        cell += c;
      }
    }
    if (quoted) throw new Error('Unclosed CSV field');
    if (cell !== '' || closing || row.length) {
      row.push(cell);
      if (row.some(x => x !== '')) rows.push(row);
    }
    return rows;
  }

  function quote(value) {
    return '"' + String(value).replaceAll('"', '""') + '"';
  }

  for (const card of cards) {
    card.querySelector('.audit-label').addEventListener('change', refresh);
    card.querySelector('.audit-person').addEventListener('change', () => {
      const element = card.querySelector('.audit-person');
      element.value = element.value.trim().toUpperCase();
    });
  }
  hide.addEventListener('change', refresh);

  document.getElementById('save-audit').addEventListener('click', () => {
    refresh();
    const rows = gather();
    if (!rows.every(validRow)) {
      status.textContent = 'Invalid person alias: use P0001 or UNKNOWN only for Real face';
      return;
    }
    const contents = [columns.join(','),
      ...rows.map(row => columns.map(c => quote(row[c])).join(','))]
      .join('\r\n') + '\r\n';
    const blob = new Blob([contents], { type: 'text/csv;charset=utf-8' });
    const link = document.createElement('a');
    const url = URL.createObjectURL(blob);
    link.href = url;
    link.download = 'wi0155-' + clip + '-unassigned-review.csv';
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1500);
    status.textContent = 'CSV save requested; no source or database changes';
  });

  document.getElementById('import-audit').addEventListener('click',
    () => { importFile.click(); });
  importFile.addEventListener('change', async () => {
    const file = importFile.files?.[0];
    if (!file) return;
    try {
      if (file.size > 64 * 1024) throw new Error('CSV exceeds size limit');
      const data = parseCsv(await file.text());
      if (data.length !== cards.length + 1 ||
        data[0].join('|') !== columns.join('|')) {
        throw new Error('CSV does not match this private audit');
      }

      const expected = gather();
      const selections = [];
      for (let i = 0; i < expected.length; i++) {
        const source = data[i + 1];
        if (source.length !== columns.length) throw new Error('Wrong CSV columns');
        const item = Object.fromEntries(columns.map((col, j) => [col, source[j]]));
        if (item.Clip !== expected[i].Clip ||
          item.Sample !== expected[i].Sample ||
          item.Frame !== expected[i].Frame ||
          item.Face !== expected[i].Face ||
          !validRow(item)) {
          throw new Error('Audit identity, ordinal or classification mismatch');
        }
        selections.push(item);
      }
      // All-or-nothing import: only mutate controls after full validation.
      for (let i = 0; i < cards.length; i++) {
        cards[i].querySelector('.audit-label').value = selections[i].Label;
        cards[i].querySelector('.audit-person').value = selections[i].PersonAlias;
      }
      refresh();
      status.textContent = 'Validated private CSV imported';
    } catch {
      status.textContent = 'Invalid or mismatched private review CSV: no changes imported';
    } finally { importFile.value = ''; }
  });
  refresh();
})();
