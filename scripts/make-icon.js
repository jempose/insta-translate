'use strict';

// 生成应用图标（PNG / ICO），纯代码绘制，不依赖任何外部素材
// 图形：圆角渐变底 + 白色对话气泡 + 气泡内挖出的一对反向箭头（"互译"）
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const OUT = path.join(__dirname, '..', 'assets');

// ---------- PNG 编码 ----------
function crc32(buf) {
  const table = [];
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    table[n] = c >>> 0;
  }
  let crc = 0xffffffff;
  for (let i = 0; i < buf.length; i++) crc = table[(crc ^ buf[i]) & 0xff] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const t = Buffer.from(type, 'ascii');
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([t, data])));
  return Buffer.concat([len, t, data, crc]);
}

function encodePng(w, h, px) {
  const stride = w * 4 + 1;
  const raw = Buffer.alloc(stride * h);
  for (let y = 0; y < h; y++) {
    raw[y * stride] = 0;
    px.copy(raw, y * stride + 1, y * w * 4, (y + 1) * w * 4);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  const idat = zlib.deflateSync(raw, { level: 9 });
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', idat),
    chunk('IEND', Buffer.alloc(0))
  ]);
}

// ---------- 几何 ----------
const inRoundRect = (x, y, x0, y0, x1, y1, r) => {
  if (x < x0 || x > x1 || y < y0 || y > y1) return false;
  const cx = Math.min(Math.max(x, x0 + r), x1 - r);
  const cy = Math.min(Math.max(y, y0 + r), y1 - r);
  return (x - cx) ** 2 + (y - cy) ** 2 <= r * r;
};

const inTriangle = (px, py, ax, ay, bx, by, cx, cy) => {
  const s = (ax - cx) * (py - cy) - (ay - cy) * (px - cx);
  const t = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
  if (s < 0 !== t < 0 && s !== 0 && t !== 0) return false;
  const d = (cx - bx) * (py - by) - (cy - by) * (px - bx);
  return d === 0 || d < 0 === s + t <= 0;
};

/** 点到线段的距离（用于画圆头笔画） */
const segDist = (x0, y0, x1, y1) => (x, y) => {
  const dx = x1 - x0;
  const dy = y1 - y0;
  const len2 = dx * dx + dy * dy || 1;
  let t = ((x - x0) * dx + (y - y0) * dy) / len2;
  t = Math.max(0, Math.min(1, t));
  return Math.hypot(x - (x0 + t * dx), y - (y0 + t * dy));
};

// ---------- 绘制 ----------
function drawIcon(size) {
  const S = size;
  const px = Buffer.alloc(S * S * 4);

  // 背景渐变（左上偏蓝，右下偏紫）
  const c1 = [0x4f, 0x8d, 0xff];
  const c2 = [0x8b, 0x6c, 0xff];
  const bgAt = (x, y) => {
    const t = Math.min(1, Math.max(0, (x / S) * 0.35 + (y / S) * 0.65));
    return [c1[0] + (c2[0] - c1[0]) * t, c1[1] + (c2[1] - c1[1]) * t, c1[2] + (c2[2] - c1[2]) * t];
  };

  // 气泡主体
  const bx0 = S * 0.15;
  const by0 = S * 0.17;
  const bx1 = S * 0.85;
  const by1 = S * 0.745;
  const radius = S * 0.15;
  const tail = [
    [S * 0.29, S * 0.72],
    [S * 0.255, S * 0.9],
    [S * 0.47, S * 0.72]
  ];

  // 气泡内的箭头（挖空）：上下两条反向箭头
  const strokeArrows = S * 0.062;
  const arrows = [
    // 上：向左的箭头
    segDist(S * 0.66, S * 0.375, S * 0.355, S * 0.375),
    segDist(S * 0.355, S * 0.375, S * 0.44, S * 0.31),
    segDist(S * 0.355, S * 0.375, S * 0.44, S * 0.44),
    // 下：向右的箭头
    segDist(S * 0.34, S * 0.565, S * 0.645, S * 0.565),
    segDist(S * 0.645, S * 0.565, S * 0.56, S * 0.5),
    segDist(S * 0.645, S * 0.565, S * 0.56, S * 0.63)
  ];

  for (let y = 0; y < S; y++) {
    for (let x = 0; x < S; x++) {
      const i = (y * S + x) * 4;
      const cx = x + 0.5;
      const cy = y + 0.5;
      let r = 0;
      let g = 0;
      let b = 0;
      let a = 0;

      if (inRoundRect(cx, cy, 0, 0, S, S, S * 0.235)) {
        const bg = bgAt(x, y);
        r = bg[0];
        g = bg[1];
        b = bg[2];
        a = 255;

        const insideBubble = inRoundRect(cx, cy, bx0, by0, bx1, by1, radius);
        const insideTail = inTriangle(cx, cy, tail[0][0], tail[0][1], tail[1][0], tail[1][1], tail[2][0], tail[2][1]);
        if (insideBubble || insideTail) {
          r = 255;
          g = 255;
          b = 255;
          // 箭头用底色"挖空"
          if (insideBubble) {
            for (const d of arrows) {
              if (d(cx, cy) <= strokeArrows) {
                r = bg[0];
                g = bg[1];
                b = bg[2];
                break;
              }
            }
          }
        }
      }

      px[i] = Math.round(r);
      px[i + 1] = Math.round(g);
      px[i + 2] = Math.round(b);
      px[i + 3] = a;
    }
  }
  return encodePng(S, S, px);
}

// ---------- ICO ----------
function buildIco(pngs) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(pngs.length, 4);
  let offset = 6 + 16 * pngs.length;
  const entries = [];
  for (const { size, data } of pngs) {
    const e = Buffer.alloc(16);
    e[0] = size >= 256 ? 0 : size;
    e[1] = size >= 256 ? 0 : size;
    e.writeUInt16LE(1, 4);
    e.writeUInt16LE(32, 6);
    e.writeUInt32LE(data.length, 8);
    e.writeUInt32LE(offset, 12);
    offset += data.length;
    entries.push(e);
  }
  return Buffer.concat([header, ...entries, ...pngs.map((p) => p.data)]);
}

function main() {
  fs.mkdirSync(OUT, { recursive: true });
  const sizes = [16, 24, 32, 48, 64, 128, 256];
  const pngs = sizes.map((size) => ({ size, data: drawIcon(size) }));
  for (const p of pngs) {
    if (p.size === 256) fs.writeFileSync(path.join(OUT, 'icon.png'), p.data);
    if (p.size === 128) fs.writeFileSync(path.join(OUT, 'icon-128.png'), p.data);
    if (p.size === 48) fs.writeFileSync(path.join(OUT, 'icon-48.png'), p.data);
    if (p.size === 32) fs.writeFileSync(path.join(OUT, 'tray.png'), p.data);
    if (p.size === 16) fs.writeFileSync(path.join(OUT, 'tray-16.png'), p.data);
  }
  fs.writeFileSync(path.join(OUT, 'icon.ico'), buildIco(pngs));
  console.log('图标已生成：', fs.readdirSync(OUT).join(', '));
}

main();
