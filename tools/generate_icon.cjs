// Regenerate committed branding assets. Requires ui/node_modules and sharp
// (available through normal Node resolution or NODE_PATH). Not needed to build.
// Glyphs are unmodified Phosphor Icons bold paths, under the MIT license.
const fs = require('node:fs');
const path = require('node:path');
const {createRequire} = require('node:module');
const {pathToFileURL} = require('node:url');
const root = path.resolve(__dirname, '..');
const uiRequire = createRequire(path.join(root, 'ui/package.json'));
const React = uiRequire('react');
const {renderToStaticMarkup} = uiRequire('react-dom/server');
const sharp = require('sharp');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

function atomic(file, data) {
  fs.mkdirSync(path.dirname(file), {recursive: true});
  fs.writeFileSync(file + '.tmp', data);
  fs.renameSync(file + '.tmp', file);
}

function glyph(Icon, x, y, size, color) {
  return renderToStaticMarkup(React.createElement(Icon, {
    weight: 'bold', x, y, width: size, height: size, color,
  }));
}

(async () => {
  const {Snowflake, CaretDown} = await import(pathToFileURL(path.join(root, 'ui/node_modules/@phosphor-icons/react/dist/index.es.js')).href);
  // A dark well, a high-contrast ice core, then a single downward cue.
  // No text or game artwork: the silhouette stays readable in the tray.
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 256 256">
<title>IdleLegions自动下井 · 冰晶下潜</title>
<defs><linearGradient id="well" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#173c49"/><stop offset="1" stop-color="#091e2a"/></linearGradient></defs>
<rect x="8" y="8" width="240" height="240" rx="54" fill="url(#well)"/>
<rect x="9" y="9" width="238" height="238" rx="53" fill="none" stroke="#5ad7df" stroke-opacity=".28" stroke-width="2"/>
${glyph(Snowflake, 30, 8, 196, '#a5f3fc')}
${glyph(CaretDown, 76, 157, 104, '#2dd4bf')}
</svg>\n`;
  const assets = path.join(root, 'app/Assets');
  atomic(path.join(assets, 'app.svg'), svg);
  atomic(path.join(assets, 'app.png'), await sharp(Buffer.from(svg)).png().toBuffer());
  const frames = await Promise.all(sizes.map(size => sharp(Buffer.from(svg)).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + 16 * sizes.length);
  header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  sizes.forEach((size, i) => {
    const entry = 6 + 16 * i;
    header[entry] = header[entry + 1] = size === 256 ? 0 : size;
    header.writeUInt16LE(1, entry + 4); header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frames[i].length, entry + 8); header.writeUInt32LE(offset, entry + 12);
    offset += frames[i].length;
  });
  const ico = Buffer.concat([header, ...frames]);
  atomic(path.join(assets, 'app.ico'), ico);
  atomic(path.join(root, 'ui/public/favicon.ico'), ico);
  console.log(`Generated SVG, 512px PNG, and ICO (${sizes.join('/')}, ${ico.length} bytes).`);
})().catch(error => {console.error(error); process.exitCode = 1;});
