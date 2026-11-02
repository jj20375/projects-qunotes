import assert from 'node:assert/strict';
import { createHmac } from 'node:crypto';
import test from 'node:test';
import { extensionOf, isInlineType, matchesSignature } from './file-type.mjs';
import { verifyToken } from './token.mjs';

const pad = (bytes) => Buffer.concat([Buffer.from(bytes), Buffer.alloc(16)]);

test('檔頭與副檔名相符才通過', () => {
  assert.equal(matchesSignature('報價.pdf', pad([...Buffer.from('%PDF-1.7')])), true);
  assert.equal(matchesSignature('scan.JPG', pad([0xff, 0xd8, 0xff, 0xe0])), true);
  assert.equal(matchesSignature('a.xlsx', pad([0x50, 0x4b, 0x03, 0x04])), true);
  assert.equal(matchesSignature('fake.pdf', pad([0x4d, 0x5a])), false, 'exe 改名成 pdf 要被擋下');
  assert.equal(matchesSignature('script.html', pad([...Buffer.from('<html>')])), false, '未列入清單的副檔名一律拒絕');
  assert.equal(matchesSignature('noext', pad([...Buffer.from('%PDF')])), false);
});

test('副檔名與 inline 判斷', () => {
  assert.equal(extensionOf('A.B.PdF'), 'pdf');
  assert.equal(isInlineType('application/pdf'), true);
  assert.equal(isInlineType('text/html'), false);
});

test('token 簽章與 API 相同格式：base64(JSON).hex(HMAC-SHA256)', () => {
  const key = 'k'.repeat(32);
  const body = Buffer.from(JSON.stringify({ kind: 'download', fileId: 'x', expiresAtUnix: Math.floor(Date.now() / 1000) + 60 })).toString('base64');
  const token = `${body}.${createHmac('sha256', key).update(body).digest('hex')}`;
  assert.equal(verifyToken(token, key)?.fileId, 'x');
  assert.equal(verifyToken(token, 'w'.repeat(32)), null);
  assert.equal(verifyToken(`${body}.00`, key), null);
});
