#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';

const indexFile = process.argv[2];
if (!indexFile) {
  console.error('usage: node validate-registry.mjs <index.json> [--repo-root <dir>]');
  process.exit(2);
}

const rootArg = process.argv.indexOf('--repo-root');
const repoRoot = rootArg >= 0 ? process.argv[rootArg + 1] : null;
const errors = [];
const warnings = [];
const idPattern = /^[a-z0-9]+(?:\.[a-z0-9-]+)+$/;
const semver = /^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$/;
const hostVersion = /^[0-9]+\.[0-9]+\.[0-9]+$/;
const sha256 = /^[a-f0-9]{64}$/;
const iconPattern = /^icons\/[A-Za-z0-9._-]+\.(png|jpg|jpeg|webp)$/;
const downloadPattern = /^https:\/\/github\.com\/[^/]+\/[^/]+\/releases\/download\/.+/;
const permissions = new Set([
  'instance.read', 'instance.write', 'network',
  'filesystem.read', 'filesystem.write', 'process.start', 'ui.command'
]);

function fail(message) { errors.push(message); }
function warn(message) { warnings.push(message); }

let data;
try {
  data = JSON.parse(fs.readFileSync(indexFile, 'utf8'));
} catch (error) {
  console.error(`FAIL parse: ${error.message}`);
  process.exit(1);
}

if (data.schemaVersion !== 1) fail('schemaVersion must be 1');
if (typeof data.updatedAt !== 'string' || Number.isNaN(Date.parse(data.updatedAt))) fail('updatedAt must be ISO date-time');
if (!Array.isArray(data.plugins)) fail('plugins must be an array');
else if (data.plugins.length > 2000) fail('plugins exceeds 2000 entries');

const ids = new Set();
for (const [pluginIndex, plugin] of (data.plugins ?? []).entries()) {
  const where = `plugins[${pluginIndex}]`;
  if (!plugin || typeof plugin !== 'object' || Array.isArray(plugin)) { fail(`${where} must be an object`); continue; }
  if (typeof plugin.id !== 'string' || !idPattern.test(plugin.id)) fail(`${where}.id invalid`);
  else if (ids.has(plugin.id)) fail(`${where}.id duplicate: ${plugin.id}`);
  else ids.add(plugin.id);
  if (typeof plugin.name !== 'string' || !plugin.name.trim() || plugin.name.length > 60) fail(`${where}.name invalid`);
  if (typeof plugin.summary !== 'string' || !plugin.summary.trim() || plugin.summary.length > 240) fail(`${where}.summary invalid`);
  if (typeof plugin.icon !== 'string' || !iconPattern.test(plugin.icon)) fail(`${where}.icon invalid`);
  else if (repoRoot && !fs.existsSync(path.join(repoRoot, plugin.icon))) fail(`${where}.icon missing: ${plugin.icon}`);
  if (!plugin.author || typeof plugin.author.name !== 'string' || typeof plugin.author.github !== 'string') fail(`${where}.author invalid`);
  if (typeof plugin.license !== 'string' || !plugin.license.trim()) fail(`${where}.license missing`);
  if (!Number.isInteger(plugin.apiVersion) || plugin.apiVersion < 1) fail(`${where}.apiVersion invalid`);
  if (typeof plugin.latest !== 'string' || !semver.test(plugin.latest)) fail(`${where}.latest invalid`);
  if (!Array.isArray(plugin.versions) || plugin.versions.length < 1 || plugin.versions.length > 50) {
    fail(`${where}.versions invalid`);
    continue;
  }

  const versions = new Set();
  for (const [versionIndex, version] of plugin.versions.entries()) {
    const vwhere = `${where}.versions[${versionIndex}]`;
    if (!version || typeof version !== 'object' || Array.isArray(version)) { fail(`${vwhere} must be an object`); continue; }
    if (typeof version.version !== 'string' || !semver.test(version.version)) fail(`${vwhere}.version invalid`);
    else if (versions.has(version.version)) fail(`${vwhere}.version duplicate`);
    else versions.add(version.version);
    if (typeof version.publishedAt !== 'string' || Number.isNaN(Date.parse(version.publishedAt))) fail(`${vwhere}.publishedAt invalid`);
    if (typeof version.download !== 'string' || !downloadPattern.test(version.download)) fail(`${vwhere}.download must be a GitHub Release asset URL`);
    if (typeof version.sha256 !== 'string' || !sha256.test(version.sha256)) fail(`${vwhere}.sha256 invalid`);
    if (!Number.isInteger(version.size) || version.size < 1 || version.size > 2147483648) fail(`${vwhere}.size invalid`);
    if (typeof version.minHost !== 'string' || !hostVersion.test(version.minHost)) fail(`${vwhere}.minHost invalid`);
    if (version.maxHost !== null && (typeof version.maxHost !== 'string' || !hostVersion.test(version.maxHost))) fail(`${vwhere}.maxHost invalid`);
    if (!Array.isArray(version.permissions) || version.permissions.some(item => !permissions.has(item))) fail(`${vwhere}.permissions invalid`);
    if (!Array.isArray(version.dependencies)) fail(`${vwhere}.dependencies invalid`);
    else for (const dep of version.dependencies) {
      if (!dep || typeof dep.id !== 'string' || typeof dep.version !== 'string' || !semver.test(dep.version)) {
        fail(`${vwhere}.dependencies entry invalid`);
      }
    }
  }
  if (!versions.has(plugin.latest)) fail(`${where}.latest not present in versions`);
  if (plugin.latest && versions.size && [...versions].some(item => item === plugin.latest && !semver.test(item))) fail(`${where}.latest invalid`);
}

const revokedArg = process.argv.indexOf('--revoked');
if (revokedArg >= 0) {
  const revokedFile = process.argv[revokedArg + 1];
  try {
    const revoked = JSON.parse(fs.readFileSync(revokedFile, 'utf8'));
    if (revoked.schemaVersion !== 1 || !Array.isArray(revoked.entries)) fail('revoked file invalid');
    else for (const [i, entry] of revoked.entries.entries()) {
      if (!entry || typeof entry.id !== 'string' || !sha256.test(entry.sha256 || '')) fail(`revoked.entries[${i}] invalid`);
      if (!entry.version || !semver.test(entry.version)) fail(`revoked.entries[${i}].version invalid`);
      if (!['warn', 'block'].includes(entry.severity)) fail(`revoked.entries[${i}].severity invalid`);
    }
  } catch (error) {
    fail(`revoked parse failed: ${error.message}`);
  }
}

for (const warning of warnings) console.warn(`WARN ${warning}`);
if (errors.length) {
  for (const error of errors) console.error(`FAIL ${error}`);
  console.error(`Registry invalid: ${errors.length} error(s)`);
  process.exit(1);
}
console.log(`OK ${data.plugins.length} plugin(s), ${data.plugins.reduce((n, p) => n + p.versions.length, 0)} version(s)`);
