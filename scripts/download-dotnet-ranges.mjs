import { mkdir, readFile, writeFile, stat } from 'node:fs/promises';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import path from 'node:path';

const [url, output, totalText, workDirectory] = process.argv.slice(2);
const total = Number(totalText);
const chunk = 4 * 1024 * 1024;
await mkdir(workDirectory, { recursive: true });
const parts = Array.from({ length: Math.ceil(total / chunk) }, (_, index) => ({
  index,
  start: index * chunk,
  end: Math.min(total - 1, (index + 1) * chunk - 1),
  file: path.join(workDirectory, `part-${String(index).padStart(4, '0')}`)
}));
async function size(file) { try { return (await stat(file)).size; } catch { return -1; } }
async function download(part) {
  const expected = part.end - part.start + 1;
  if (await size(part.file) === expected) return;
  for (let attempt = 1; attempt <= 5; attempt++) {
    await new Promise((resolve, reject) => {
      const child = spawn('curl.exe', ['--http1.1', '-fsSL', '--range', `${part.start}-${part.end}`, '--connect-timeout', '20', '--max-time', '90', url, '-o', part.file], { stdio: 'ignore' });
      child.on('error', reject);
      child.on('exit', code => code === 0 ? resolve() : reject(new Error(`curl exit ${code}`)));
    }).catch(() => {});
    if (await size(part.file) === expected) {
      console.log(`Part ${part.index + 1}/${parts.length}`);
      return;
    }
  }
  throw new Error(`Failed part ${part.index}`);
}
let cursor = 0;
async function worker() { while (cursor < parts.length) await download(parts[cursor++]); }
await Promise.all(Array.from({ length: 6 }, worker));
const buffers = [];
for (const part of parts) buffers.push(await readFile(part.file));
const archive = Buffer.concat(buffers);
if (archive.length !== total) throw new Error(`Wrong archive length ${archive.length}`);
const md5 = createHash('md5').update(archive).digest('base64');
if (md5 !== '1KjGUwHgpu5UxiQ9yDjNGQ==') throw new Error(`Wrong SDK MD5 ${md5}`);
await writeFile(output, archive);
console.log(`Verified SDK: ${archive.length} bytes, MD5 ${md5}`);
