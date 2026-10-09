// Rebuilds the self-hosted Material Symbols subset (docs/UI_GUIDE.md §0.4 "Fonts and icons").
// Scans the views and scripts for icon names, asks Google Fonts for a font holding only those glyphs, and saves it as
// PlasticSurgery/wwwroot/fonts/material-symbols-outlined.woff2 (+ icons.txt, the list it contains).
// Run from the repo root after adding an icon:  node docs/tools/icon-font.js
// Needs Node 18+ and internet access on the developer machine only; the app never calls Google.
const fs = require('fs');
const path = require('path');

const web = path.join(__dirname, '..', '..', 'PlasticSurgery');
const out = path.join(web, 'wwwroot', 'fonts');

// Icons the kit (aurora.js / aurora.css) draws itself.
const kit = ['check_circle', 'close', 'dark_mode', 'error', 'expand_more', 'info', 'light_mode', 'menu', 'warning'];

// Where icon names appear:
//   <span class="au-icon …">name</span>        (views, partials, JS strings)
//   icon: 'name' / Icon = "name" / "name" passed to an Icon parameter (C# models, JS maps) — written as  icon:name  below
const patterns = [
  /class="(?:[^"]*\s)?au-icon(?:\s[^"]*)?"[^>]*>\s*([a-z0-9_]+)\s*</g,
  /class=\\?'(?:[^']*\s)?au-icon(?:\s[^']*)?\\?'[^>]*>\s*([a-z0-9_]+)\s*</g,
  /[Ii]con\s*[:=]\s*"([a-z0-9_]+)"/g, // C#: Icon: "event", ActionIcon = "add"
  /[Ii]con\s*:\s*'([a-z0-9_]+)'/g, // JS maps: icon: 'event'
  /\/\*\s*icon:([a-z0-9_]+)\s*\*\//g,
  /@\*\s*icon:([a-z0-9_]+)\s*\*@/g,
];

function walk(dir, files) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === 'bin' || entry.name === 'obj' || entry.name === 'node_modules') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, files);
    else if (/\.(cshtml|cs|js)$/.test(entry.name)) files.push(full);
  }
  return files;
}

const names = new Set(kit);
for (const file of walk(web, [])) {
  if (file.endsWith(path.join('wwwroot', 'js', 'aurora.js'))) continue; // covered by the kit list
  const text = fs.readFileSync(file, 'utf8');
  for (const rx of patterns) for (const m of text.matchAll(rx)) names.add(m[1]);
}
const list = [...names].sort();

(async () => {
  const ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36';
  const cssUrl = 'https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@20..24,400,0..1,0'
    + '&icon_names=' + list.join(',') + '&display=block';
  const css = await (await fetch(cssUrl, { headers: { 'User-Agent': ua } })).text();
  const fontUrl = (css.match(/url\((https:[^)]+)\)/) || [])[1];
  if (!fontUrl) throw new Error('No font URL in the Google Fonts answer (is every icon name valid?):\n' + css.slice(0, 400));
  const font = Buffer.from(await (await fetch(fontUrl)).arrayBuffer());
  fs.mkdirSync(out, { recursive: true });
  fs.writeFileSync(path.join(out, 'material-symbols-outlined.woff2'), font);
  fs.writeFileSync(path.join(out, 'icons.txt'), list.join('\n') + '\n');
  console.log(`material-symbols-outlined.woff2: ${list.length} icons, ${(font.length / 1024).toFixed(1)} KB`);
})().catch((e) => { console.error(e.message); process.exit(1); });
