import { writeFile, stat } from 'node:fs/promises';

const [url, output, expectedText] = process.argv.slice(2);
if (!url || !output) throw new Error('Usage: bun download.mjs <url> <output> [expected bytes]');
const response = await fetch(url, { redirect: 'follow' });
if (!response.ok) throw new Error(`HTTP ${response.status} ${response.statusText}`);
const expected = expectedText ? Number(expectedText) : Number(response.headers.get('content-length'));
const buffer = Buffer.from(await response.arrayBuffer());
if (expected && buffer.length !== expected) throw new Error(`Incomplete download: ${buffer.length}/${expected}`);
await writeFile(output, buffer);
const info = await stat(output);
console.log(`Downloaded ${info.size} bytes to ${output}`);
