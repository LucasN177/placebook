// Erzeugt die App-Icons (wwwroot/icon-192.png, icon-512.png) ohne externe Abhängigkeiten.
//
//   node tools/generate-icons.mjs
//
// Motiv: weißer Kartenpin auf dunkelblauem Grund. Der Hintergrund ist randlos, damit das Icon auch als
// "maskable" funktioniert (das Betriebssystem schneidet Kreis/Squircle aus); der Pin liegt in der Safe Zone.

import { writeFileSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const BACKGROUND = [0x1e, 0x3a, 0x5f];
const PIN = [0xff, 0xff, 0xff];
const SAMPLES = 4; // 4x4 Supersampling für weiche Kanten

// Geometrie in Einheitskoordinaten (0..1, y nach unten)
const head = { x: 0.5, y: 0.42, r: 0.17 };
const tip = { x: 0.5, y: 0.76 };
const hole = { x: 0.5, y: 0.42, r: 0.07 };

// Tangentenpunkte von der Spitze an den Kreis: Pin = Kreis ∪ Dreieck(Tangente1, Tangente2, Spitze)
const d = tip.y - head.y;
const a = Math.acos(head.r / d);
const tangent1 = { x: head.x + head.r * Math.sin(a), y: head.y + head.r * Math.cos(a) };
const tangent2 = { x: head.x - head.r * Math.sin(a), y: head.y + head.r * Math.cos(a) };

function insideCircle(x, y, c) {
    return (x - c.x) ** 2 + (y - c.y) ** 2 <= c.r ** 2;
}

function insideTriangle(x, y, p1, p2, p3) {
    const s = (a, b) => (x - b.x) * (a.y - b.y) - (a.x - b.x) * (y - b.y);
    const d1 = s(p1, p2), d2 = s(p2, p3), d3 = s(p3, p1);
    return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
}

function isPin(x, y) {
    const body = insideCircle(x, y, head) || insideTriangle(x, y, tangent1, tangent2, tip);
    return body && !insideCircle(x, y, hole);
}

function render(size) {
    const rows = [];
    for (let py = 0; py < size; py++) {
        const row = Buffer.alloc(1 + size * 4); // Filterbyte 0 + RGBA
        for (let px = 0; px < size; px++) {
            let hits = 0;
            for (let sy = 0; sy < SAMPLES; sy++)
                for (let sx = 0; sx < SAMPLES; sx++)
                    if (isPin((px + (sx + 0.5) / SAMPLES) / size, (py + (sy + 0.5) / SAMPLES) / size)) hits++;
            const t = hits / (SAMPLES * SAMPLES);
            for (let c = 0; c < 3; c++)
                row[1 + px * 4 + c] = Math.round(BACKGROUND[c] * (1 - t) + PIN[c] * t);
            row[1 + px * 4 + 3] = 255;
        }
        rows.push(row);
    }
    return png(size, size, Buffer.concat(rows));
}

function png(width, height, raw) {
    const header = Buffer.alloc(13);
    header.writeUInt32BE(width, 0);
    header.writeUInt32BE(height, 4);
    header.set([8, 6, 0, 0, 0], 8); // 8 Bit, RGBA, Standard-Kompression/-Filter, kein Interlacing
    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', header),
        chunk('IDAT', deflateSync(raw, { level: 9 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

function chunk(type, data) {
    const length = Buffer.alloc(4);
    length.writeUInt32BE(data.length);
    const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(body));
    return Buffer.concat([length, body, crc]);
}

function crc32(buffer) {
    let crc = 0xffffffff;
    for (const byte of buffer) {
        crc ^= byte;
        for (let k = 0; k < 8; k++) crc = (crc >>> 1) ^ (0xedb88320 & -(crc & 1));
    }
    return (crc ^ 0xffffffff) >>> 0;
}

const wwwroot = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'src', 'Placebook', 'wwwroot');
for (const size of [192, 512]) {
    const file = path.join(wwwroot, `icon-${size}.png`);
    writeFileSync(file, render(size));
    console.log(`geschrieben: ${file}`);
}
