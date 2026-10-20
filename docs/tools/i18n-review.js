// Writes the translation review sheet for a native speaker: one CSV row per key with the English text, the translation,
// whether it is still missing, and the files that use the key (so the reviewer can find it on screen; with the setting
// Localization:ShowTextKeys on, ?textKeys=1 shows every key on the page itself).
// Run from the repo root:  node docs/tools/i18n-review.js ar C:\path\outside\the\repo\review-ar.csv
// The output is for sending to a reviewer: write it outside the repo (it is never committed).
const fs = require('fs');
const path = require('path');

const [language, out] = process.argv.slice(2);
if (!language || !out) {
  console.error('Usage: node docs/tools/i18n-review.js <ar|fr> <output.csv>');
  process.exit(1);
}

const web = path.join(__dirname, '..', '..', 'Omni.App');
const read = (lang) => JSON.parse(fs.readFileSync(path.join(web, 'Resources', 'i18n', lang + '.json'), 'utf8'));
const english = read('en');
const translation = read(language);

function walk(dir, files) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (['bin', 'obj', 'node_modules', 'lib', 'Resources'].includes(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, files);
    else if (/\.(cshtml|cs|js)$/.test(entry.name)) files.push(full);
  }
  return files;
}

// Where each key appears: the quoted key itself, or its plural family ("contacts.count" for contacts.count.one).
const sources = walk(web, []).map((file) => ({ file: path.relative(web, file).replace(/\\/g, '/'), text: fs.readFileSync(file, 'utf8') }));
function usedIn(key) {
  const family = key.replace(/\.(zero|one|two|few|many|other)$/, '');
  return sources.filter((s) => s.text.includes('"' + key + '"') || s.text.includes("'" + key + "'")
    || (family !== key && (s.text.includes('"' + family + '"') || s.text.includes("'" + family + "'"))))
    .map((s) => s.file).slice(0, 5).join(' ');
}

const csv = (value) => '"' + String(value ?? '').replace(/"/g, '""') + '"';
const rows = [['key', 'english', language, 'status', 'used in', 'reviewer note'].map(csv).join(',')];
for (const key of Object.keys(english)) {
  const status = translation[key] === undefined ? 'missing' : translation[key] === english[key] ? 'same as English' : '';
  rows.push([key, english[key], translation[key] ?? '', status, usedIn(key), ''].map(csv).join(','));
}
// Extra plural forms a language adds (Arabic zero/two/few/many) follow their family.
for (const key of Object.keys(translation).filter((k) => english[k] === undefined)) {
  rows.push([key, '', translation[key], 'extra plural form', usedIn(key), ''].map(csv).join(','));
}

// A byte-order mark so spreadsheet apps read the Arabic and accented text as UTF-8.
fs.writeFileSync(out, '\uFEFF' + rows.join('\r\n') + '\r\n', 'utf8');
console.log(`wrote ${rows.length - 1} rows to ${out}`);
