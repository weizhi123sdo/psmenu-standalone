// 机械拆分 csscript1.js → src/ 多个 partial class 文件（词法级深度扫描）
const fs = require('fs');
const SRC = 'E:/code/.quicker/actions/86f8bb44-e464-423e-a397-05332b7cc633/files/csscript1.js';
const OUT = 'E:/code/psmenu/src';
const text = fs.readFileSync(SRC, 'utf8');
const lines = text.split(/\r?\n/);
const NL = text.length - text.replace(/\r?\n/g, '').length; // rough

// 词法扫描：记录每行结束时的 brace 深度（正确处理字符串/字符/注释/verbatim串）
const depthAfterLine = new Array(lines.length).fill(0);
let d = 0, i = 0, line = 0;
const n = text.length;
let state = 'code'; // code | str | chr | verbatim | linecomment | blockcomment
while (i < n) {
  const c = text[i], c2 = text.substr(i, 2);
  if (c === '\n') {
    depthAfterLine[line] = d;
    line++; i++;
    if (state === 'linecomment') state = 'code';
    continue;
  }
  if (state === 'linecomment') {
    if (c === '\n') { depthAfterLine[line] = d; line++; state = 'code'; }
    i++; continue;
  }
  if (state === 'code') {
    if (c2 === '//') { state = 'linecomment'; i += 2; continue; }
    if (c2 === '/*') { state = 'blockcomment'; i += 2; continue; }
    if (c === '"') {
      // verbatim @"..." ?
      if (text[i - 1] === '@') { state = 'verbatim'; i++; continue; }
      state = 'str'; i++; continue;
    }
    if (c === "'") { state = 'chr'; i++; continue; }
    if (c === '{') { d++; i++; continue; }
    if (c === '}') { d--; i++; continue; }
    i++; continue;
  }
  if (state === 'str') {
    if (c === '\\') { i += 2; continue; }
    if (c === '"') { state = 'code'; }
    i++; continue;
  }
  if (state === 'chr') {
    if (c === '\\') { i += 2; continue; }
    if (c === "'") { state = 'code'; }
    i++; continue;
  }
  if (state === 'verbatim') {
    if (c === '"') {
      if (text[i + 1] === '"') { i += 2; continue; }
      state = 'code';
    }
    i++; continue;
  }
  if (state === 'blockcomment') {
    if (c2 === '*/') { state = 'code'; i += 2; continue; }
    if (c === '\n') { depthAfterLine[line] = d; line++; }
    i++; continue;
  }
}

const USING = [
'using System;','using System.Collections.Generic;','using System.Diagnostics;','using System.Globalization;',
'using System.IO;','using System.Linq;','using System.Net;','using System.Reflection;','using System.Runtime.InteropServices;',
'using System.Text;','using System.Text.RegularExpressions;','using System.Threading;','using System.Threading.Tasks;',
'using System.Windows;','using System.Windows.Controls;','using System.Windows.Controls.Primitives;','using System.Windows.Input;',
'using System.Windows.Interop;','using System.Windows.Media;','using System.Windows.Media.Animation;','using System.Windows.Media.Effects;',
'using System.Windows.Media.Imaging;','using System.Windows.Threading;','using Newtonsoft.Json;','using Newtonsoft.Json.Linq;',
'using FontAwesome5;','using FontAwesome5.WPF;','using SD = System.Drawing;','using SWF = System.Windows.Forms;'
].join('\r\n');

const chunks = [
  ['Models.cs',       [186, 407]],
  ['Serialization.cs',[408, 514]],
  ['Misc.cs',         [515, 683]],
  ['ConfigService.cs',[684, 946]],
  ['NetIcons.cs',     [947, 1177]],
  ['NetScripts.cs',   [1178, 1360]],
  ['LibPicker.cs',    [1361, 2077]],
  ['LayerDetect.cs',  [2078, 2184]],
  ['Executors.cs',    [2185, 2377]],
  ['Tokens.cs',       [2378, 2687]],
  ['MenuHead.cs',     [2688, 2762]],
  ['Search.cs',       [2763, 2930]],
  ['Menu.cs',         [2931, 3940]],
  ['ColorPick.cs',    [3941, 4002]],
  ['Editor.cs',       [4003, 5859]],
  ['EditItem.cs',     [5860, 6440]],
  ['Dialogs.cs',      [6441, 6561]],
];

let bad = [];
for (const [name, [a, b]] of chunks) {
  const dA = a - 2 >= 0 ? depthAfterLine[a - 2] : 0;
  const dB = depthAfterLine[b - 1];
  if (dA !== 0) bad.push(`${name} 起 ${a} 前=深度${dA}`);
  if (dB !== 0) bad.push(`${name} 止 ${b} 后=深度${dB}`);
}
if (bad.length) { console.log('BOUNDARY_FAIL\n' + bad.join('\n')); process.exit(1); }

fs.mkdirSync(OUT, { recursive: true });
for (const [name, [a, b]] of chunks) {
  const body = lines.slice(a - 1, b).join('\r\n');
  const file = USING + '\r\n\r\n#pragma warning disable 169, 649, 414, 219, 67\r\n' +
    'namespace PsMenuApp\r\n{\r\n' +
    'public static partial class PsMenu\r\n{\r\n' +
    body + '\r\n}\r\n}\r\n';
  fs.writeFileSync(OUT + '/' + name, file, 'utf8');
  console.log(`${name}: ${b - a + 1} 行 (源 ${a}-${b})`);
}
console.log('OK');
