import sharp from 'sharp';
import pngToIco from 'png-to-ico';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { writeFile } from 'node:fs/promises';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
await sharp(path.join(root, 'assets/logo.svg')).resize({ width: 1200 }).png().toFile(path.join(root, 'assets/logo.png'));
await sharp(path.join(root, 'assets/logo-white.svg')).resize({ width: 1200 }).png().toFile(path.join(root, 'assets/logo-white.png'));
await sharp(path.join(root, 'assets/logo-black.svg')).resize({ width: 1200 }).flatten({ background: '#FFFFFF' }).png().toFile(path.join(root, 'assets/logo-desktop.png'));
await sharp(path.join(root, 'assets/badge.svg')).resize(512, 512).png().toFile(path.join(root, 'assets/badge.png'));
await sharp(path.join(root, 'assets/badge-light.svg')).resize(512, 512).png().toFile(path.join(root, 'assets/badge-light.png'));
await sharp(path.join(root, 'assets/badge-white.svg')).resize(512, 512).png().toFile(path.join(root, 'assets/badge-white.png'));
const iconPng = await sharp(path.join(root, 'assets/badge-light.svg')).resize(256, 256).flatten({ background: '#FFFFFF' }).png().toBuffer();
await writeFile(path.join(root, 'assets/nexmc.ico'), await pngToIco(iconPng));
console.log('Rendered white in-app logo, black-on-white desktop logo and monochrome icon.');
