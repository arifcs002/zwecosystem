import { toMonochrome, rasterCommands, buildReceiptJob, chunk, GS } from '../src/app/utils/escpos.util.ts';
import assert from 'node:assert/strict';

// 10x2 image: left 3 pixels black, rest white; second row all white; plus one transparent black pixel (=white)
const w = 10, h = 2, px = new Uint8ClampedArray(w * h * 4);
for (let i = 0; i < w * h; i++) { px[i*4]=255; px[i*4+1]=255; px[i*4+2]=255; px[i*4+3]=255; }
for (const x of [0,1,2]) { const i = x*4; px[i]=0; px[i+1]=0; px[i+2]=0; }
{ const i = 5*4; px[i]=0; px[i+1]=0; px[i+2]=0; px[i+3]=0; }            // fully transparent black => paper
const m = toMonochrome(px, w, h);
assert.equal(m.bytesPerRow, 2);                                          // 10 px -> 2 bytes
assert.equal(m.data[0], 0b11100000);                                     // first three dots, MSB first
assert.equal(m.data[1], 0);                                              // nothing in the remaining 2 px
assert.equal(m.data[2], 0); assert.equal(m.data[3], 0);                  // white row

// a one-dot image at the last column of a 9px row: second byte, top bit
const px2 = new Uint8ClampedArray(9*4); px2.fill(255); { const i=8*4; px2[i]=0; px2[i+1]=0; px2[i+2]=0; }
assert.equal(toMonochrome(px2, 9, 1).data[1], 0b10000000);

// banding: 300 rows with 96-row bands => 96, 96, 96, 12
const tall = { width: 16, height: 300, bytesPerRow: 2, data: new Uint8Array(2 * 300) };
const bands = rasterCommands(tall, 96);
assert.deepEqual(bands.map(b => b[6] | (b[7] << 8)), [96, 96, 96, 12]);
assert.deepEqual(bands[0].slice(0, 6), [GS, 0x76, 0x30, 0x00, 2, 0]);    // GS v 0, mode 0, xL=2 xH=0
assert.equal(bands[0].length, 8 + 2 * 96);

// full job starts with reset and ends with a cut; no cut when disabled
const job = buildReceiptJob(m);
assert.deepEqual([...job.slice(0, 2)], [0x1b, 0x40]);
assert.deepEqual([...job.slice(-4)], [GS, 0x56, 0x42, 0]);
assert.notDeepEqual([...buildReceiptJob(m, { cut: false }).slice(-4)], [GS, 0x56, 0x42, 0]);

// chunking keeps every byte, in order, in pieces of at most 100
const big = Uint8Array.from({ length: 1234 }, (_, i) => i % 251);
const parts = chunk(big);
assert.ok(parts.every(p => p.length <= 100));
assert.deepEqual(Uint8Array.from(parts.flatMap(p => [...p])), big);

// bad input is rejected, not silently printed as garbage
assert.throws(() => toMonochrome(new Uint8ClampedArray(3), 4, 4));
console.log('escpos tests ok');
