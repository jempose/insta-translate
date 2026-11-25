'use strict';

// 打包"可分发安装包"：把编译好的程序作为资源嵌进 setup.exe，双击即可安装/卸载
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const ROOT = path.join(__dirname, '..');
const pkg = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
const VERSION = pkg.version;
const CSC = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';
const APP_DIR = path.join(ROOT, 'dist', 'InstaTranslate');
const BUILD = path.join(ROOT, 'build', 'installer');
const OUT = path.join(ROOT, 'dist', `即时译-${VERSION}-安装版.exe`);

function rm(p) {
  try {
    fs.rmSync(p, { recursive: true, force: true });
  } catch {
    /* 被占用时忽略 */
  }
}

function rmf(p) {
  try {
    fs.rmSync(p, { force: true });
  } catch {
    /* noop */
  }
}

function main() {
  if (!fs.existsSync(path.join(APP_DIR, 'InstaTranslate.exe'))) {
    console.error('先运行 npm run build:native 生成 dist\\InstaTranslate');
    process.exit(1);
  }
  rm(BUILD);
  fs.mkdirSync(BUILD, { recursive: true });

  // 1) 收集要打包的文件
  const files = [];
  const walk = (dir, base) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, e.name);
      const rel = base ? base + '/' + e.name : e.name;
      if (e.isDirectory()) walk(full, rel);
      else if (e.isFile()) {
        // 运行时会产生的东西不打包
        if (/^(livetest-|boot\.log|app\.log)/i.test(e.name)) continue;
        if (/_test|\.log$/i.test(e.name)) continue;
        files.push({ rel, full });
      }
    }
  };
  walk(APP_DIR, '');

  // 2) 生成资源清单
  const resArgs = [];
  for (const f of files) {
    const resName = 'payload.' + f.rel.replace(/[\\/]/g, '__');
    resArgs.push('/resource:' + f.full + ',' + resName);
  }

  // 3) 编译 setup.exe
  const exe = path.join(BUILD, 'setup.exe');
  const icon = path.join(ROOT, 'assets', 'icon.ico');
  const args = [
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/codepage:65001',
    '/out:' + exe,
    '/win32icon:' + icon,
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Xml.dll',
    path.join(ROOT, 'src', 'installer', 'Setup.cs'),
    path.join(ROOT, 'src', 'installer', 'SetupInfo.cs')
  ].concat(resArgs);

  try {
    const out = execFileSync(CSC, args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
    if (out.trim()) console.log(out.trim());
  } catch (err) {
    console.error(((err.stdout || '') + (err.stderr || '')).trim() || err.message);
    process.exit(1);
  }

  // 4) 输出：dist 里一份 + 干净目录一份（避免带低完整性标记的目录把程序降权）
  const ascii = path.join(ROOT, 'dist', `InstaTranslate-${VERSION}-setup.exe`);
  rmf(OUT);
  rmf(ascii);
  fs.copyFileSync(exe, OUT);
  fs.copyFileSync(exe, ascii);

  const cleanDir = process.env.INSTA_CLEAN_DIR || path.join(require('os').homedir(), 'InstaTranslate');
  const cleanOut = path.join(cleanDir, `InstaTranslate-${VERSION}-setup.exe`);
  try {
    fs.mkdirSync(cleanDir, { recursive: true });
    rmf(cleanOut);
    fs.copyFileSync(exe, cleanOut);
  } catch (e) {
    console.log('  ! 复制到干净目录失败：' + e.message);
  }

  const size = fs.statSync(OUT).size;
  console.log('安装包已生成：');
  console.log('  文件 ', OUT);
  console.log('  别名 ', ascii);
  console.log('  干净副本 ', cleanOut);
  console.log('  体积 ', (size / 1024).toFixed(0) + ' KB');
  console.log('  内含 ', files.length, '个文件');
}

main();
