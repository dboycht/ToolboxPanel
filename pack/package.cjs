// package.cjs —— 打发布包（Node，避免 PowerShell 的编码坑）
//
// 用法：node pack/package.cjs
//
// 做什么：
//   1. 读 winui/ToolboxPanel.WinUI.csproj 里的 <Version>（**版本号单一来源**，与
//      src/toolbox/__init__.py 的 __version__ 保持一致）；
//   2. 确认发布产物已编译（winui/bin/x64/Debug/<tfm>/win-x64/ToolboxPanel.WinUI.exe）；
//   3. 把**整个输出目录**打进 dist/ToolboxPanel-<版本>-win-x64.zip
//      （unpackaged + WindowsAppSDK self-contained ⇒ 运行时在目录里，双击 EXE 即可用）；
//   4. 打印 zip 的大小与 SHA256（写 Release 说明时要贴）。
//
// ⚠️ 只打包 EXE 所在目录，**绝不**把 data/（用户数据）或源码包进去。

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { execFileSync } = require('child_process');

const root = path.resolve(__dirname, '..');
const csproj = path.join(root, 'winui', 'ToolboxPanel.WinUI.csproj');

function readVersion() {
  const text = fs.readFileSync(csproj, 'utf8');
  const m = text.match(/<Version>([^<]+)<\/Version>/);
  if (!m) throw new Error('csproj 里没有 <Version>');
  return m[1].trim();
}

function findOutputDir() {
  const base = path.join(root, 'winui', 'bin', 'x64', 'Debug');
  if (!fs.existsSync(base)) throw new Error('还没有编译产物：' + base);
  for (const tfm of fs.readdirSync(base)) {
    const dir = path.join(base, tfm, 'win-x64');
    if (fs.existsSync(path.join(dir, 'ToolboxPanel.WinUI.exe'))) return dir;
  }
  throw new Error('找不到 ToolboxPanel.WinUI.exe（先 dotnet build -p:Platform=x64）');
}

const version = readVersion();
const outDir = findOutputDir();
const distDir = path.join(root, 'dist');
fs.mkdirSync(distDir, { recursive: true });

const zipName = `ToolboxPanel-${version}-win-x64.zip`;
const zipPath = path.join(distDir, zipName);
if (fs.existsSync(zipPath)) fs.unlinkSync(zipPath);

// 用 PowerShell 的 Compress-Archive（本机自带；路径都带引号，避免空格问题）
const ps = `Compress-Archive -Path "${outDir}\\*" -DestinationPath "${zipPath}" -Force`;
execFileSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', ps], { stdio: 'inherit' });

const bytes = fs.readFileSync(zipPath);
const sha256 = crypto.createHash('sha256').update(bytes).digest('hex');

const exeInfo = path.join(outDir, 'ToolboxPanel.WinUI.exe');
console.log('版本号      :', version);
console.log('输出目录    :', outDir);
console.log('EXE 是否存在:', fs.existsSync(exeInfo));
console.log('发布包      :', zipPath);
console.log('大小        :', (bytes.length / 1024 / 1024).toFixed(1), 'MB');
console.log('SHA256      :', sha256);
