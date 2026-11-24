import assert from 'node:assert/strict';
import { parseCsv, mapTable, toNumber, TEMPLATE_CSV } from '../src/app/utils/csv.util.ts';

// quotes, commas inside cells, doubled quotes, CRLF, blank lines, BOM
const t = parseCsv('﻿name,note\r\n"Boot, tall","He said ""hi"""\r\n\r\nSock,plain\r\n');
assert.deepEqual(t, [['name', 'note'], ['Boot, tall', 'He said "hi"'], ['Sock', 'plain']]);

// semicolon-delimited (Excel in many regions) and tab-delimited
assert.deepEqual(parseCsv('a;b;c\n1;2;3'), [['a', 'b', 'c'], ['1', '2', '3']]);
assert.deepEqual(parseCsv('a\tb\n1\t2'), [['a', 'b'], ['1', '2']]);

// a newline inside quotes stays inside the cell
assert.deepEqual(parseCsv('a,b\n"line1\nline2",x'), [['a', 'b'], ['line1\nline2', 'x']]);

// numbers: separators, currency sign, Bangla digits, junk
assert.equal(toNumber('1,250'), 1250);
assert.equal(toNumber('৳ 2,500.50'), 2500.5);
assert.equal(toNumber('২৫০০'), 2500);
assert.equal(toNumber(''), null);
assert.ok(Number.isNaN(toNumber('12abc')));

// header aliases, any order, extra columns reported
const m = mapTable(parseCsv('Category,Product Name,Selling Price,Buy Price,Qty,Colour\nShoes,Loafer,"2,500",1500,4,Black'));
assert.deepEqual(m.missingRequired, []);
assert.deepEqual(m.unknownHeaders, ['Colour']);
assert.deepEqual(m.rows[0], { category: 'Shoes', name: 'Loafer', price: 2500, wholesalePrice: 1500, quantity: 4 });

// missing required columns are named
assert.deepEqual(mapTable(parseCsv('name,price\nA,1')).missingRequired, ['wholesalePrice', 'category']);

// the downloadable template parses back into valid rows
const tpl = mapTable(parseCsv(TEMPLATE_CSV));
assert.deepEqual(tpl.missingRequired, []);
assert.equal(tpl.rows.length, 2);
assert.equal(tpl.rows[0].sizes, '40:5; 41:3; 42:2');
assert.equal(tpl.rows[1].quantity, 30);
assert.equal(tpl.rows[0].uom, 'pair');

console.log('csv tests ok');
