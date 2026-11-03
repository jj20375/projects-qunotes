/**
 * 第四階段端對端驗證：訂單／保固／發票（每專案多筆）、發票收款與作廢、專案金額摘要、
 * 附件上傳與下載（API 簽 token → file-server 存檔 → 回呼 API）、檔頭與大小檢查、權限與資料範圍。
 *
 * 前置：API 以 Quote__LoginCaptchaEnabled=false 啟動、file-server 已啟動，且已執行 Seeder（SeedDemoData=true）。
 * 用法：node tests/documents-smoke.mjs [apiUrl] [fileServerUrl]
 */
import { readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const fileUrl = process.argv[3] ?? 'http://127.0.0.1:5190';
const envText = await readFile(new URL('../.env.development-api.local', import.meta.url), 'utf8');
const seedPassword = envText.match(/^Quote__BootstrapAdminPassword=(.+)$/m)?.[1]?.trim();
assert.ok(seedPassword, '找不到 Quote__BootstrapAdminPassword');

const suffix = Date.now().toString(36);
let passed = 0;

async function call(method, path, { token, body } = {}) {
  const headers = {};
  if (token) headers.authorization = `Bearer ${token}`;
  if (body !== undefined) headers['content-type'] = 'application/json';
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const text = await response.text();
  return { status: response.status, data: text ? JSON.parse(text) : null };
}

async function login(account) {
  const result = await call('POST', '/api/auth/login', { body: { account, password: seedPassword } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return result.data.accessToken;
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

/** 申請上傳 → PUT 到 file-server；回傳 file-server 的回應狀態。 */
async function upload(token, ownerType, ownerId, fileName, bytes, declaredSize = bytes.length) {
  const session = await call('POST', '/api/files/upload-sessions', { token, body: { ownerType, ownerId, fileName, size: declaredSize, category: '掃描檔' } });
  if (session.status !== 201) return { sessionStatus: session.status, session };
  const response = await fetch(`${fileUrl}${session.data.uploadUrl}`, { method: 'PUT', headers: { authorization: `Bearer ${session.data.token}` }, body: bytes });
  return { sessionStatus: 201, session: session.data, status: response.status, data: await response.json() };
}

const pdf = Buffer.concat([Buffer.from('%PDF-1.7\n%測試訂單掃描檔\n'), Buffer.alloc(2048, 0x20), Buffer.from('\n%%EOF\n')]);

const admin = await login('demo-admin');
const otherSales = await login('demo-sales');
let sales;
let accountant;
let projectId;
let orderId;
let cancelledOrderId;
let invoiceId;
let acceptedQuoteId;
let draftQuoteId;

await step('準備：業務、會計帳號，客戶與業務負責的專案', async () => {
  const roles = (await call('GET', '/api/admin/roles', { token: admin })).data;
  const departments = (await call('GET', '/api/admin/departments', { token: admin })).data;
  const create = (account, role, dept) =>
    call('POST', '/api/admin/users', {
      token: admin,
      body: { account, displayName: account, password: seedPassword, departmentId: departments.find((d) => d.code === dept).id, roleId: roles.find((r) => r.code === role).id },
    });
  assert.equal((await create(`ds-${suffix}`, 'sales', 'SALES-1')).status, 201);
  assert.equal((await create(`da-${suffix}`, 'accountant', 'FIN')).status, 201);
  sales = await login(`ds-${suffix}`);
  accountant = await login(`da-${suffix}`);
  const client = await call('POST', '/api/clients', { token: sales, body: { name: `${suffix} 文件客戶`, invoiceTitle: `${suffix} 文件客戶股份有限公司`, taxId: '24536806' } });
  projectId = (await call('POST', '/api/projects', { token: sales, body: { name: `${suffix} 文件專案`, clientId: client.data.id } })).data.id;
  // 訂單必須掛在定案（客戶已接受）的報價下：準備一份已接受、一份草稿。
  const quoteBody = { title: '文件測試報價', items: [{ name: '伺服器', unit: '台', quantity: 1, unitPrice: 100000 }] };
  acceptedQuoteId = (await call('POST', `/api/projects/${projectId}/quotes`, { token: sales, body: quoteBody })).data.id;
  for (const [action, token] of [['submit', sales], ['approve', admin], ['send', sales], ['accept', sales]]) {
    const result = await call('POST', `/api/quotes/${acceptedQuoteId}/${action}`, { token });
    assert.equal(result.status, 200, `${action}：${JSON.stringify(result.data)}`);
  }
  draftQuoteId = (await call('POST', `/api/projects/${projectId}/quotes`, { token: sales, body: quoteBody })).data.id;
});

await step('層級規則：訂單必須對應已接受的報價', async () => {
  const body = { orderNo: `PX-${suffix}`, orderDate: '2026-10-29', amount: 1, taxAmount: 0 };
  assert.equal((await call('POST', `/api/projects/${projectId}/orders`, { token: sales, body })).status, 400, '沒有報價');
  assert.equal((await call('POST', `/api/projects/${projectId}/orders`, { token: sales, body: { ...body, quoteId: draftQuoteId } })).status, 400, '草稿報價');
  assert.equal((await call('POST', `/api/projects/${projectId}/orders`, { token: sales, body: { ...body, quoteId: '00000000-0000-0000-0000-000000000001' } })).status, 400, '不存在的報價');
});

await step('訂單：業務新增（合計自動計算）；業務沒有刪除權', async () => {
  const created = await call('POST', `/api/projects/${projectId}/orders`, { token: sales, body: { quoteId: acceptedQuoteId, orderNo: `PO-${suffix}`, orderDate: '2026-10-29', amount: 100000, taxAmount: 5000 } });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  orderId = created.data.id;
  assert.equal((await call('POST', `/api/projects/${projectId}/orders`, { token: sales, body: { quoteId: acceptedQuoteId, orderNo: 'x', orderDate: '2026-10-29', amount: -1 } })).status, 400);
  const list = (await call('GET', `/api/projects/${projectId}/orders`, { token: sales })).data;
  assert.equal(list.activeTotal, 105000);
  assert.equal(list.total, 1, 'total 是筆數（分頁用），合計金額在 activeTotal');
  assert.equal(list.canDelete, false);
  assert.equal((await call('DELETE', `/api/orders/${orderId}`, { token: sales })).status, 403);
});

await step('會計：專案唯讀，但可登錄訂單（子資料權限 + 全公司範圍）', async () => {
  const project = (await call('GET', `/api/projects/${projectId}`, { token: accountant })).data;
  assert.equal(project.canWrite, false);
  const created = await call('POST', `/api/projects/${projectId}/orders`, { token: accountant, body: { quoteId: acceptedQuoteId, orderNo: `PO2-${suffix}`, orderDate: '2026-10-30', amount: 20000, taxAmount: 1000, status: 'cancelled' } });
  assert.equal(created.status, 201);
  cancelledOrderId = created.data.id;
  const orders = (await call('GET', `/api/projects/${projectId}/orders`, { token: sales })).data;
  assert.equal(orders.activeTotal, 105000, '已取消的訂單不計入合計');
  assert.equal(orders.total, 2);
});

await step('保固：起訖日檢查與到期狀態', async () => {
  assert.equal((await call('POST', `/api/projects/${projectId}/warranties`, { token: sales, body: { orderId, warrantyNo: 'W1', subject: '主機', startDate: '2026-10-29', endDate: '2026-10-01' } })).status, 400);
  assert.equal((await call('POST', `/api/projects/${projectId}/warranties`, { token: sales, body: { warrantyNo: 'W2', subject: '主機', startDate: '2026-10-01', endDate: '2026-10-29' } })).status, 400, '保固單必須對應訂單');
  assert.equal((await call('POST', `/api/projects/${projectId}/warranties`, { token: sales, body: { orderId: cancelledOrderId, warrantyNo: 'W3', subject: '主機', startDate: '2026-10-01', endDate: '2026-10-29' } })).status, 400, '不能選已取消的訂單');
  const created = await call('POST', `/api/projects/${projectId}/warranties`, { token: sales, body: { orderId, warrantyNo: `W-${suffix}`, subject: '伺服器保固', startDate: '2025-11-01', endDate: '2026-12-31' } });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  const list = (await call('GET', `/api/projects/${projectId}/warranties`, { token: sales })).data;
  assert.equal(list.items[0].state, 'expiring');
  assert.equal(list.items[0].orderNo, `PO-${suffix}`);
});

await step('發票：業務只能看；會計開立（號碼正規化、買受人帶客戶抬頭）；號碼不可重複', async () => {
  const body = { orderId, invoiceNo: 'ab12345678', invoiceDate: '2026-10-29', salesAmount: 100000, taxAmount: 5000, dueDate: '2026-11-30' };
  assert.equal((await call('POST', `/api/projects/${projectId}/invoices`, { token: sales, body })).status, 403);
  const number = `${String.fromCharCode(65 + (Date.now() % 26))}${String.fromCharCode(65 + (Math.floor(Date.now() / 32) % 26))}${String(Date.now()).slice(-8)}`;
  const created = await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body: { ...body, invoiceNo: number.toLowerCase() } });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  assert.equal(created.data.invoiceNo, `${number.slice(0, 2)}-${number.slice(2)}`);
  invoiceId = created.data.id;
  assert.equal((await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body: { ...body, invoiceNo: number } })).status, 409);
  assert.equal((await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body: { ...body, invoiceNo: 'A1234' } })).status, 400);
  assert.equal((await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body: { ...body, orderId: undefined, invoiceNo: `QQ${String(Date.now()).slice(-8)}` } })).status, 400, '發票必須對應訂單');
  const invoice = (await call('GET', `/api/projects/${projectId}/invoices`, { token: sales })).data.items.find((i) => i.id === invoiceId);
  assert.equal(invoice.buyerName, `${suffix} 文件客戶股份有限公司`);
  assert.equal(invoice.buyerTaxId, '24536806');
  assert.equal(invoice.totalAmount, 105000);
});

await step('收款：部分 → 付清；已收款不可作廢；改回 0 後作廢需原因', async () => {
  const pay = (amount) => call('POST', `/api/invoices/${invoiceId}/payment`, { token: accountant, body: { paidAmount: amount, paidAt: '2026-11-15' } });
  assert.equal((await call('POST', `/api/invoices/${invoiceId}/payment`, { token: sales, body: { paidAmount: 1 } })).status, 403);
  assert.equal((await pay(200000)).status, 400);
  assert.equal((await pay(50000)).data.paymentStatus, 'partial');
  assert.equal((await pay(105000)).data.paymentStatus, 'paid');
  const summary = (await call('GET', `/api/projects/${projectId}`, { token: accountant })).data.summary;
  assert.deepEqual(summary, { orderTotal: 105000, invoicedTotal: 105000, paidTotal: 105000, activeWarranties: 1 });
  assert.equal((await call('POST', `/api/invoices/${invoiceId}/void`, { token: accountant, body: { reason: '開錯' } })).status, 400);
  await pay(0);
  assert.equal((await call('POST', `/api/invoices/${invoiceId}/void`, { token: accountant, body: {} })).status, 400);
  assert.equal((await call('POST', `/api/invoices/${invoiceId}/void`, { token: accountant, body: { reason: '金額開錯' } })).status, 200);
  assert.equal((await call('GET', `/api/projects/${projectId}`, { token: accountant })).data.summary.invoicedTotal, 0, '作廢的發票不計入');
});

await step('刪除未收款發票後，號碼可以重新登錄', async () => {
  const body = { orderId, invoiceNo: `ZZ-${String(Date.now()).slice(-8)}`, invoiceDate: '2026-10-29', salesAmount: 1000, taxAmount: 50 };
  const first = await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body });
  assert.equal((await call('DELETE', `/api/invoices/${first.data.id}`, { token: accountant })).status, 204);
  assert.equal((await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body })).status, 201);
});

await step('發票號碼即時防呆：重複時帶出那張發票、訂單與專案；看不到的專案不透露內容；編輯自己不算重複', async () => {
  const mine = (await call('GET', `/api/projects/${projectId}/invoices`, { token: sales })).data.items.find((item) => item.id === invoiceId);
  const lookup = (token, number, excludeId) => call('GET', `/api/invoices/lookup?invoiceNo=${encodeURIComponent(number)}${excludeId ? `&excludeId=${excludeId}` : ''}`, { token });

  // 重複（格式可以不含連字號、小寫）：帶出發票、對應訂單與所屬專案
  const found = await lookup(sales, mine.invoiceNo.replace('-', '').toLowerCase());
  assert.equal(found.status, 200, JSON.stringify(found.data));
  assert.equal(found.data.exists, true);
  assert.equal(found.data.visible, true);
  assert.equal(found.data.invoice.id, invoiceId);
  assert.equal(found.data.invoice.totalAmount, 105000);
  assert.equal(found.data.order.orderNo, `PO-${suffix}`);
  assert.equal(found.data.order.quoteNo.startsWith('Q'), true, '訂單的來源報價也帶出來');
  assert.equal(found.data.project.id, projectId);
  // 編輯這張發票自己時不算重複
  assert.equal((await lookup(sales, mine.invoiceNo, invoiceId)).data.exists, false);
  // 沒用過的號碼
  assert.equal((await lookup(sales, `ZY-${String(Date.now()).slice(-8)}`)).data.exists, false);
  // 格式不對
  assert.equal((await lookup(sales, 'A1234')).status, 400);
  // 看不到那個專案的人：只知道號碼已被使用，拿不到任何內容
  const hidden = await lookup(otherSales, mine.invoiceNo);
  assert.equal(hidden.status, 200);
  assert.equal(hidden.data.exists, true);
  assert.equal(hidden.data.visible, false);
  assert.equal(hidden.data.invoice, undefined);
  assert.equal(hidden.data.order, undefined);
  assert.equal(hidden.data.project, undefined);
  // 沒有發票讀取權的人不能查
  assert.equal((await call('GET', `/api/invoices/lookup?invoiceNo=${mine.invoiceNo}`, {})).status, 401);
  // 已作廢的號碼一樣算重複：存檔也被擋
  const voidedNo = `ZX-${String(Date.now()).slice(-8)}`;
  const voided = await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body: { orderId, invoiceNo: voidedNo, invoiceDate: '2026-10-29', salesAmount: 100, taxAmount: 5 } });
  assert.equal(voided.status, 201, JSON.stringify(voided.data));
  assert.equal((await call('POST', `/api/invoices/${voided.data.id}/void`, { token: accountant, body: { reason: '測試作廢' } })).status, 200);
  const voidedLookup = (await lookup(sales, voidedNo)).data;
  assert.equal(voidedLookup.exists, true);
  assert.equal(voidedLookup.invoice.status, 'void');
  assert.equal(voidedLookup.invoice.voidReason, '測試作廢');
  assert.equal((await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body: { orderId, invoiceNo: voidedNo, invoiceDate: '2026-10-29', salesAmount: 100, taxAmount: 5 } })).status, 409);
});

await step('訂單內容預覽：帶出來源報價、所屬專案與底下發票統計；看不到的專案回 404', async () => {
  const detail = await call('GET', `/api/orders/${orderId}`, { token: sales });
  assert.equal(detail.status, 200, JSON.stringify(detail.data));
  assert.equal(detail.data.orderNo, `PO-${suffix}`);
  assert.equal(detail.data.total, 105000);
  assert.equal(detail.data.quoteId, acceptedQuoteId);
  assert.equal(detail.data.quoteNo.startsWith('Q'), true);
  assert.equal(detail.data.project.id, projectId);
  // 發票筆數與金額只算開立中的（作廢的不算），與發票列表一致
  const issued = (await call('GET', `/api/projects/${projectId}/invoices?pageSize=100`, { token: sales })).data.items.filter((item) => item.orderId === orderId && item.status === 'issued');
  assert.equal(detail.data.invoiceCount, issued.length);
  assert.equal(detail.data.invoicedAmount, issued.reduce((sum, item) => sum + item.totalAmount, 0));
  assert.equal(detail.data.shipmentCount, 0);
  assert.equal(detail.data.warrantyCount, (await call('GET', `/api/projects/${projectId}/warranties?pageSize=100`, { token: sales })).data.items.filter((item) => item.orderId === orderId).length);
  assert.equal((await call('GET', `/api/orders/${orderId}`, { token: otherSales })).status, 404, '其他業務看不到這個專案的訂單');
  assert.equal((await call('GET', `/api/orders/00000000-0000-0000-0000-000000000001`, { token: sales })).status, 404);
  assert.equal((await call('GET', `/api/orders/${orderId}`, {})).status, 401);
});

let shipmentId;
await step('出貨單：掛在訂單下（必填、不能選已取消的訂單），附件與訂單權限一致；訂單底下有出貨單不能刪', async () => {
  const body = { orderId, shipmentNo: `SH-${suffix}-1`, shipDate: '2026-11-01', receiverName: '王小明', deliveryAddress: '台北市信義區', trackingNo: `TRK-${suffix}` };
  assert.equal((await call('POST', `/api/projects/${projectId}/shipments`, { token: sales, body: { ...body, orderId: undefined } })).status, 400, '出貨單必須對應訂單');
  assert.equal((await call('POST', `/api/projects/${projectId}/shipments`, { token: sales, body: { ...body, orderId: cancelledOrderId } })).status, 400, '不能選已取消的訂單');
  assert.equal((await call('POST', `/api/projects/${projectId}/shipments`, { token: sales, body: { ...body, status: 'lost' } })).status, 400, '狀態不正確');
  const created = await call('POST', `/api/projects/${projectId}/shipments`, { token: sales, body });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  shipmentId = created.data.id;
  assert.equal((await call('POST', `/api/projects/${projectId}/shipments`, { token: sales, body: { ...body, shipmentNo: `SH-${suffix}-2`, shipDate: '2026-11-05' } })).status, 201, '同一張訂單可分批出貨');
  assert.equal((await call('PUT', `/api/shipments/${shipmentId}`, { token: sales, body: { ...body, status: 'delivered' } })).status, 200);
  const list = (await call('GET', `/api/projects/${projectId}/shipments`, { token: sales })).data;
  assert.equal(list.total, 2);
  assert.equal(list.canDelete, false, '業務沒有 orders:delete');
  const row = list.items.find((item) => item.id === shipmentId);
  assert.equal(row.orderNo, `PO-${suffix}`);
  assert.equal(row.status, 'delivered');
  // 出貨單附件：讀寫跟著 orders 權限
  const uploaded = await upload(sales, 'shipment', shipmentId, '簽收單.pdf', pdf);
  assert.equal(uploaded.status, 200, JSON.stringify(uploaded.data ?? uploaded.session));
  assert.equal((await call('GET', `/api/projects/${projectId}/shipments`, { token: sales })).data.items.find((item) => item.id === shipmentId).attachmentCount, 1);
  const attachment = (await call('GET', `/api/attachments?ownerType=shipment&ownerId=${shipmentId}`, { token: sales })).data.items[0];
  const link = await call('GET', `/api/attachments/${attachment.id}/download`, { token: sales });
  assert.equal(link.status, 200);
  assert.equal(Buffer.compare(Buffer.from(await (await fetch(`${fileUrl}${link.data.url}`)).arrayBuffer()), pdf), 0);
  // 只有出貨單的新訂單：有出貨單時不能刪，出貨單刪掉後可以刪
  const order = (await call('POST', `/api/projects/${projectId}/orders`, { token: accountant, body: { quoteId: acceptedQuoteId, orderNo: `PO3-${suffix}`, orderDate: '2026-10-31', amount: 10, taxAmount: 0 } })).data.id;
  const onlyShipment = (await call('POST', `/api/projects/${projectId}/shipments`, { token: accountant, body: { ...body, orderId: order, shipmentNo: `SH-${suffix}-3` } })).data.id;
  const blocked = await call('DELETE', `/api/orders/${order}`, { token: accountant });
  assert.equal(blocked.status, 400);
  assert.match(blocked.data.message ?? JSON.stringify(blocked.data), /出貨單/);
  assert.equal((await call('DELETE', `/api/shipments/${onlyShipment}`, { token: sales })).status, 403, '業務不能刪出貨單');
  assert.equal((await call('DELETE', `/api/shipments/${onlyShipment}`, { token: accountant })).status, 204);
  assert.equal((await call('DELETE', `/api/orders/${order}`, { token: accountant })).status, 204);
  // 其他業務看不到這個專案的出貨單
  assert.equal((await call('GET', `/api/projects/${projectId}/shipments`, { token: otherSales })).status, 404);
});

let attachmentId;
await step('附件上傳：API 簽 token → file-server 存檔並回呼 → 列表出現', async () => {
  const result = await upload(sales, 'order', orderId, '客戶訂單.pdf', pdf);
  assert.equal(result.status, 200, JSON.stringify(result.data));
  assert.equal(result.data.size, pdf.length);
  const list = (await call('GET', `/api/attachments?ownerType=order&ownerId=${orderId}`, { token: sales })).data;
  assert.equal(list.items.length, 1);
  assert.equal(list.items[0].fileName, '客戶訂單.pdf');
  assert.equal(list.canDownload, true);
  attachmentId = list.items[0].id;
  assert.equal((await call('GET', `/api/projects/${projectId}/orders`, { token: sales })).data.items.find((o) => o.id === orderId).attachmentCount, 1);
});

await step('附件下載：短效連結取回相同內容與中文檔名', async () => {
  const link = await call('GET', `/api/attachments/${attachmentId}/download`, { token: sales });
  assert.equal(link.status, 200);
  const response = await fetch(`${fileUrl}${link.data.url}`);
  assert.equal(response.status, 200);
  assert.deepEqual(Buffer.from(await response.arrayBuffer()), pdf);
  assert.match(response.headers.get('content-disposition'), /filename\*=UTF-8''%E5%AE%A2%E6%88%B6/);
  const ranged = await fetch(`${fileUrl}${link.data.url}`, { headers: { range: 'bytes=0-3' } });
  assert.equal(ranged.status, 206);
  assert.equal(await ranged.text(), '%PDF');
  assert.equal((await fetch(`${fileUrl}${link.data.url.replace(/token=[^&]+/, 'token=forged.abc')}`)).status, 401);
});

await step('檔案檢查：副檔名不在清單、超過 20MB、檔頭不符、大小不符、token 對不上檔案', async () => {
  assert.equal((await upload(sales, 'order', orderId, 'virus.exe', pdf)).sessionStatus, 400);
  assert.equal((await upload(sales, 'order', orderId, 'big.pdf', pdf, 21 * 1024 * 1024)).sessionStatus, 400);
  const fake = await upload(sales, 'order', orderId, '假的.pdf', Buffer.concat([Buffer.from('MZ'), Buffer.alloc(100)]));
  assert.equal(fake.status, 415);
  const short = await upload(sales, 'order', orderId, '短少.pdf', pdf, pdf.length + 10);
  assert.equal(short.status, 400);
  const session = await call('POST', '/api/files/upload-sessions', { token: sales, body: { ownerType: 'order', ownerId: orderId, fileName: 'a.pdf', size: pdf.length } });
  const other = await call('POST', '/api/files/upload-sessions', { token: sales, body: { ownerType: 'order', ownerId: orderId, fileName: 'b.pdf', size: pdf.length } });
  const swapped = await fetch(`${fileUrl}${other.data.uploadUrl}`, { method: 'PUT', headers: { authorization: `Bearer ${session.data.token}` }, body: pdf });
  assert.equal(swapped.status, 401, '不能拿 A 檔的 token 上傳到 B 檔');
  const list = (await call('GET', `/api/attachments?ownerType=order&ownerId=${orderId}`, { token: sales })).data;
  assert.equal(list.items.length, 1, '失敗或未完成的上傳不會出現在列表');
});

await step('資料範圍：其他業務看不到也下載不到；回呼端點需要共享密鑰', async () => {
  assert.equal((await call('GET', `/api/attachments?ownerType=order&ownerId=${orderId}`, { token: otherSales })).status, 404);
  assert.equal((await call('GET', `/api/attachments/${attachmentId}/download`, { token: otherSales })).status, 404);
  assert.equal((await call('POST', '/api/files/upload-sessions', { token: otherSales, body: { ownerType: 'order', ownerId: orderId, fileName: 'x.pdf', size: 10 } })).status, 404);
  const callback = await fetch(`${baseUrl}/api/internal/files/${attachmentId}/completed`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ size: 1, sha256: 'a'.repeat(64) }) });
  assert.equal(callback.status, 401);
});

await step('發票附件：會計上傳、業務可下載（有 invoices:download）；業務不能刪', async () => {
  assert.equal((await upload(accountant, 'invoice', invoiceId, '發票.png', Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.alloc(64)]))).status, 200);
  const list = (await call('GET', `/api/attachments?ownerType=invoice&ownerId=${invoiceId}`, { token: sales })).data;
  assert.equal(list.items.length, 1);
  assert.equal(list.canDelete, false);
  assert.equal((await call('GET', `/api/attachments/${list.items[0].id}/download?inline=true`, { token: sales })).status, 200);
  assert.equal((await call('DELETE', `/api/attachments/${list.items[0].id}`, { token: sales })).status, 403);
});

await step('刪除附件後不再列出、連結失效', async () => {
  assert.equal((await call('DELETE', `/api/attachments/${attachmentId}`, { token: sales })).status, 204);
  assert.equal((await call('GET', `/api/attachments?ownerType=order&ownerId=${orderId}`, { token: sales })).data.items.length, 0);
  assert.equal((await call('GET', `/api/attachments/${attachmentId}/download`, { token: sales })).status, 404);
});

await step('專案子資料的搜尋與分頁由後端處理：訂單、出貨、保固、發票、報價，合計不受分頁影響', async () => {
  // 發票號碼在公司內不可重複，每次執行用不同的起始號碼。
  const invoiceBase = Number(String(Date.now()).slice(-7)) * 10;
  // 補到 23 筆訂單（日期比前面的早，不影響其他步驟）、22 筆保固、22 筆發票。
  for (let i = 0; i < 22; i += 1) {
    const no = String(i).padStart(2, '0');
    assert.equal((await call('POST', `/api/projects/${projectId}/orders`, { token: accountant, body: { quoteId: acceptedQuoteId, orderNo: `PG-${suffix}-${no}`, orderDate: '2026-01-15', amount: 1000, taxAmount: 50 } })).status, 201);
    assert.equal((await call('POST', `/api/projects/${projectId}/shipments`, { token: sales, body: { orderId, shipmentNo: `SG-${suffix}-${no}`, shipDate: '2026-01-20', receiverName: `收貨人 ${no}` } })).status, 201);
    assert.equal((await call('POST', `/api/projects/${projectId}/warranties`, { token: sales, body: { orderId, warrantyNo: `WG-${suffix}-${no}`, subject: `分頁保固 ${no}`, startDate: '2026-01-01', endDate: '2027-01-01' } })).status, 201);
    assert.equal((await call('POST', `/api/projects/${projectId}/invoices`, { token: accountant, body: { orderId, invoiceNo: `PG${String(invoiceBase + i).padStart(8, '0')}`, invoiceDate: '2026-02-01', salesAmount: 1000, taxAmount: 50, dueDate: '2026-12-31' } })).status, 201);
  }
  const get = async (resource, query) => (await call('GET', `/api/projects/${projectId}/${resource}?${query}`, { token: accountant })).data;
  for (const resource of ['orders', 'shipments', 'warranties', 'invoices']) {
    const first = await get(resource, 'page=1&pageSize=10');
    const second = await get(resource, 'page=2&pageSize=10');
    assert.equal(first.items.length, 10, `${resource} 第一頁 10 筆`);
    assert.equal(first.page, 1);
    assert.equal(second.page, 2);
    assert.ok(first.total >= 22, `${resource} total 是整個專案的筆數：${first.total}`);
    assert.equal(new Set([...first.items, ...second.items].map((row) => row.id)).size, 20, `${resource} 兩頁不重複`);
    const last = await get(resource, `page=${Math.ceil(first.total / 10)}&pageSize=10`);
    assert.ok(last.items.length >= 1 && last.items.length <= 10);
    assert.equal((await get(resource, 'page=1&pageSize=0')).items.length, 1, '每頁筆數下限 1');
  }
  // 預設一頁 20 筆
  assert.equal((await get('orders', '')).items.length, 20);
  // 關鍵字由後端過濾，total 是符合條件的筆數
  const found = await get('orders', `keyword=PG-${suffix}-03`);
  assert.equal(found.total, 1);
  assert.equal(found.items[0].orderNo, `PG-${suffix}-03`);
  assert.equal((await get('warranties', `keyword=${encodeURIComponent('分頁保固 07')}`)).total, 1);
  assert.equal((await get('shipments', `keyword=${encodeURIComponent('收貨人 09')}`)).total, 1);
  assert.equal((await get('shipments', `keyword=SG-${suffix}-1`)).total, 10, '出貨單號關鍵字（SG-…-10 ~ 19）');
  assert.equal((await get('invoices', `keyword=${String(invoiceBase + 5).padStart(8, '0')}`)).total, 1, '發票號碼存成 AB-12345678 格式，用數字部分搜尋');
  // 合計是整個專案的，不因分頁或關鍵字改變
  const full = await get('orders', 'page=1&pageSize=5');
  assert.equal(full.activeTotal, 105000 + 22 * 1050);
  assert.equal((await get('orders', `keyword=PG-${suffix}-03&pageSize=5`)).activeTotal, full.activeTotal);
  const invoices = await get('invoices', 'page=1&pageSize=5');
  const invoicesPage2 = await get('invoices', 'page=2&pageSize=5');
  assert.equal(invoices.totalAmount, invoicesPage2.totalAmount, '發票合計不受分頁影響');
  assert.ok(invoices.totalAmount >= 22 * 1050);
  // activeOnly 與分頁、關鍵字可以一起用（開發票選訂單用）
  const active = await get('orders', 'activeOnly=true&pageSize=100');
  assert.ok(active.items.every((row) => row.status !== 'cancelled'));
  assert.equal(active.total, 1 + 22);
  // 專案底下的報價也是後端分頁
  const quoted = (await call('GET', `/api/projects/${projectId}/quotes?page=1&pageSize=5`, { token: sales })).data;
  assert.equal(quoted.pageSize, 5);
  assert.equal(typeof quoted.total, 'number');
});

await step('清理：停用這次建立的測試帳號（不讓公司通訊錄與成員下拉堆滿測試帳號）', async () => {
  for (const account of [`ds-${suffix}`, `da-${suffix}`]) {
    const found = (await call('GET', `/api/admin/users?keyword=${account}`, { token: admin })).data.items.find((user) => user.account === account);
    if (found) assert.equal((await call('PATCH', `/api/admin/users/${found.membershipId}`, { token: admin, body: { isActive: false } })).status, 200, account);
  }
});

console.log(`\n全部 ${passed} 項通過`);
