// Extract simple upstream defaults for the NEXMC settings adapter.
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const source = await readFile(path.join(root, 'references/PCL/Plain Craft Launcher 2/Pages/PageSetup/Settings.vb'), 'utf8');
const defaults = {};
for (const match of source.matchAll(/New Setting\("([^"]+)",\s*("[^"]*"|True|False|-?\d+)/g)) {
  const [, key, raw] = match;
  defaults[key] = raw === 'True' ? true : raw === 'False' ? false : raw.startsWith('"') ? raw.slice(1, -1) : Number(raw);
}
Object.assign(defaults, {
  LoginType: 0,
  SystemSystemTelemetry: false,
  SystemSystemCache: '',
  LaunchFolderSelect: '',
  UiHiddenPageLink: true,
  UiHiddenSetupLink: true
});
await writeFile(path.join(root, 'src/Nexmc.Kernel/kernel-defaults.json'), JSON.stringify(defaults, null, 2) + '\n');
console.log(`Extracted ${Object.keys(defaults).length} upstream setting defaults.`);
