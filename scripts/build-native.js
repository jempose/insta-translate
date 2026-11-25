'use strict';

// 编译原生版（C# / WinForms）：不依赖 Chromium，体积小、启动快
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const ROOT = path.join(__dirname, '..');
const pkg = JSON.parse(fs.readFileSync(path.join(ROOT, 'package.json'), 'utf8'));
const SRC = path.join(ROOT, 'src', 'native');
const OUTDIR = path.join(ROOT, 'dist', 'InstaTranslate');
const CSC = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe';

function rm(p) {
  fs.rmSync(p, { recursive: true, force: true });
}

function main() {
  if (!fs.existsSync(CSC)) {
    console.error('找不到 .NET Framework 编译器：' + CSC);
    process.exit(1);
  }
  rm(OUTDIR);
  fs.mkdirSync(OUTDIR, { recursive: true });

  const sources = fs
    .readdirSync(SRC)
    .filter((f) => f.endsWith('.cs'))
    .map((f) => path.join(SRC, f));

  const exe = path.join(OUTDIR, 'InstaTranslate.exe');
  const icon = path.join(ROOT, 'assets', 'icon.ico');

  const gac = 'C:\\Windows\\Microsoft.NET\\assembly\\GAC_MSIL\\System.Speech\\v4.0_4.0.0.0__31bf3856ad364e35\\System.Speech.dll';
  const speech = fs.existsSync(gac) ? gac : 'System.Speech.dll';

  const args = [
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    '/codepage:65001',
    '/out:' + exe,
    '/win32icon:' + icon,
    '/win32manifest:' + path.join(SRC, 'app.manifest'),
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Web.Extensions.dll',
    '/reference:' + speech,
    '/reference:System.Xml.dll',
    '/reference:' + path.join(process.env.WINDIR || 'C:\\\\Windows', 'Microsoft.NET', 'assembly', 'GAC_MSIL', 'UIAutomationClient', 'v4.0_4.0.0.0__31bf3856ad364e35', 'UIAutomationClient.dll'),
    '/reference:' + path.join(process.env.WINDIR || 'C:\\\\Windows', 'Microsoft.NET', 'assembly', 'GAC_MSIL', 'UIAutomationTypes', 'v4.0_4.0.0.0__31bf3856ad364e35', 'UIAutomationTypes.dll'),
    '/reference:' + path.join(process.env.WINDIR || 'C:\\\\Windows', 'Microsoft.NET', 'assembly', 'GAC_MSIL', 'WindowsBase', 'v4.0_4.0.0.0__31bf3856ad364e35', 'WindowsBase.dll')
  ].concat(sources);

  try {
    const out = execFileSync(CSC, args, { encoding: 'utf8' });
    if (out.trim()) console.log(out);
  } catch (err) {
    const msg = (err.stdout || '') + (err.stderr || '');
    console.error(msg.trim() || err.message);
    process.exit(1);
  }

  // 附带图标目录（运行时读取托盘图标用）
  fs.mkdirSync(path.join(OUTDIR, 'assets'), { recursive: true });
  for (const f of ['icon.ico', 'icon.png']) {
    const src = path.join(ROOT, 'assets', f);
    if (fs.existsSync(src)) fs.copyFileSync(src, path.join(OUTDIR, 'assets', f));
  }

  const readme = path.join(ROOT, 'README-native.md');
  if (fs.existsSync(readme)) fs.copyFileSync(readme, path.join(OUTDIR, '使用说明.md'));

  // 启动脚本：万一 exe 被安全软件拦截
  fs.writeFileSync(
    path.join(OUTDIR, '启动.cmd'),
    '@echo off\r\ncd /d "%~dp0"\r\nstart "" "%~dp0InstaTranslate.exe"\r\n',
    'ascii'
  );
  // 自检
  fs.writeFileSync(
    path.join(OUTDIR, '自检.cmd'),
    [
      '@echo off',
      'chcp 65001 >nul',
      'cd /d "%~dp0"',
      'echo Running self-test ...',
      'start /wait "" "%~dp0InstaTranslate.exe" --selftest',
      'echo.',
      'type "%TEMP%\\InstaTranslate\\selftest.txt" 2>nul',
      'pause'
    ].join('\r\n') + '\r\n',
    'ascii'
  );

  // 真机验证：自动开记事本 → 选中 → 走真实取词链路 → 报结论
  fs.writeFileSync(
    path.join(OUTDIR, '验证.cmd'),
    [
      '@echo off',
      'chcp 65001 >nul',
      'cd /d "%~dp0"',
      'set R=%~dp0livetest-result.txt',
      'del /q "%R%" 2>nul',
      'echo 正在验证取词链路（会自动打开记事本，约 5 秒）...',
      'start /wait "" "%~dp0InstaTranslate.exe" --live-test',
      'echo.',
      'type "%R%" 2>nul',
      'pause'
    ].join('\r\n') + '\r\n',
    'ascii'
  );

  // 另存一份到"干净目录"：工作区 D:\dsh 带低完整性标记，从那里启动的程序会一直是低权限，
  // 低权限读不到别的程序的选区（这是之前所有失败的根因）。放到 D:\InstaTranslate 就不受影响。
  // 输出到用户主目录：不要放在带低完整性标记的目录里（从那种目录启动的程序会被降权）
  const CLEAN = process.env.INSTA_CLEAN_DIR || path.join(require('os').homedir(), 'InstaTranslate');

  function copyTree(from, to) {
    fs.mkdirSync(to, { recursive: true });
    for (const entry of fs.readdirSync(from, { withFileTypes: true })) {
      const s = path.join(from, entry.name);
      const d = path.join(to, entry.name);
      if (entry.isDirectory()) copyTree(s, d);
      else if (entry.isFile()) fs.copyFileSync(s, d);
    }
  }

  try {
    fs.rmSync(CLEAN, { recursive: true, force: true });
  } catch {
    /* 目录里有正在运行的程序时会失败，忽略 */
  }
  try {
    copyTree(OUTDIR, CLEAN);
    console.log('  干净副本 ', CLEAN);
  } catch (e) {
    console.log('  ! 复制到 ' + CLEAN + ' 失败：' + e.message);
  }

  const size = fs.statSync(exe).size;
  console.log('原生版编译完成：');
  console.log('  目录 ', OUTDIR);
  console.log('  程序 ', exe);
  console.log('  体积 ', (size / 1024).toFixed(0) + ' KB');
}

main();
