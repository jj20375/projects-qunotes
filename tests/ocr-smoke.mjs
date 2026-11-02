/**
 * 第六階段圖像識別匯入驗證：用本機模擬端點取代真正的 Claude（Anthropic）或 Gemini（Google），
 * 檢查 API 送出的請求（模型、圖片／PDF、structured output schema）、辨識結果轉成預覽（日期、稅別、金額比對提醒）、
 * 確認後建立報價、各種錯誤情境與權限。供應商依 API 的設定決定（GET /api/quote-imports/options），兩家各跑一次。
 *
 * 前置：API 以下列環境變數啟動（模擬端點由本測試在 5299 埠啟動，不需要真的金鑰、不產生費用）：
 *   Claude：Quote__LoginCaptchaEnabled=false Ai__Ocr__Provider=claude Quote__AnthropicApiKey=test-key Quote__AnthropicBaseUrl=http://127.0.0.1:5299 Quote__GeminiApiKey=
 *   （務必明確指定供應商並清空另一家的金鑰，否則本機 .env 的真金鑰會讓測試真的呼叫外部服務）
 *   Gemini：Quote__LoginCaptchaEnabled=false Ai__Ocr__Provider=gemini Quote__GeminiApiKey=test-key Quote__GeminiBaseUrl=http://127.0.0.1:5299
 * 用法：node tests/ocr-smoke.mjs [baseUrl]
 *
 * 模型與強度的預期值讀自 AI 設定檔（預設 apps/api/QuotePlatform.Api/ai-settings.json；API 用 Quote__AiSettingsPath 指向別的檔案時，
 * 請以 AI_SETTINGS_PATH 指向同一個檔案）。另設 AI_SETTINGS_RELOAD_TEST=1 時會修改該檔案測試熱更新（結束後還原），
 * 只應指向測試用的副本，不要指向版控中的設定檔。
 */
import { createServer } from 'node:http';
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';

const baseUrl = process.argv[2] ?? 'http://127.0.0.1:5280';
const mockPort = 5299;
const envText = await readFile(new URL('../.env.development-api.local', import.meta.url), 'utf8');
const seedPassword = envText.match(/^Quote__BootstrapAdminPassword=(.+)$/m)?.[1]?.trim();
assert.ok(seedPassword, '找不到 Quote__BootstrapAdminPassword');

const suffix = Date.now().toString(36);
let passed = 0;

// AI 設定檔允許 // 註解；測試只需要把整行註解去掉再解析。
// 用 fileURLToPath，不要用 URL.pathname：專案路徑有中文，pathname 會是百分比編碼。
const settingsPath = process.env.AI_SETTINGS_PATH ?? fileURLToPath(new URL('../apps/api/QuotePlatform.Api/ai-settings.json', import.meta.url));
const readSettings = async () => JSON.parse((await readFile(settingsPath, 'utf8')).replace(/^\s*\/\/.*$/gm, '')).Ai.Ocr;
const settings = await readSettings();
const reloadTest = process.env.AI_SETTINGS_RELOAD_TEST === '1';
assert.ok(!reloadTest || process.env.AI_SETTINGS_PATH, '熱更新測試必須以 AI_SETTINGS_PATH 指向測試用的副本');

// ---------- 模擬 Claude／Gemini ----------

const requests = [];
let reply = () => ({ status: 500, body: { error: { message: '尚未設定回應' } } });
/** 由 options 決定：'claude' 或 'gemini'。 */
let provider;

const claudeMessage = (text, stopReason = 'end_turn') => ({
  status: 200,
  body: {
    id: `msg_${Date.now()}`,
    type: 'message',
    role: 'assistant',
    model: 'claude-opus-5-5',
    content: text === null ? [] : [{ type: 'text', text }],
    stop_reason: stopReason,
    stop_sequence: null,
    stop_details: stopReason === 'refusal' ? { type: 'refusal', category: null, explanation: null } : null,
    usage: { input_tokens: 1200, output_tokens: 300 },
  },
});
const geminiResponse = (text, finishReason = 'STOP') => ({
  status: 200,
  body: {
    candidates: [{ content: { role: 'model', parts: text === null ? [] : [{ text: '先看表頭再看明細', thought: true }, { text }] }, finishReason, index: 0 }],
    usageMetadata: { promptTokenCount: 1200, candidatesTokenCount: 300, totalTokenCount: 1500 },
  },
});

/** 各情境的模擬回應，依供應商產生對應的格式。 */
const replies = {
  ok: (payload) => (provider === 'gemini' ? geminiResponse(JSON.stringify(payload)) : claudeMessage(JSON.stringify(payload))),
  refused: () => (provider === 'gemini' ? geminiResponse(null, 'SAFETY') : claudeMessage(null, 'refusal')),
  blockedPrompt: () => ({ status: 200, body: { promptFeedback: { blockReason: 'PROHIBITED_CONTENT' } } }),
  badKey: () =>
    provider === 'gemini'
      ? { status: 400, body: { error: { code: 400, message: 'API key not valid. Please pass a valid API key.', status: 'INVALID_ARGUMENT', details: [{ reason: 'API_KEY_INVALID' }] } } }
      : { status: 401, body: { type: 'error', error: { type: 'authentication_error', message: 'invalid x-api-key' } } },
  rateLimited: () => ({ status: 429, body: { error: { code: 429, message: 'Resource exhausted', status: 'RESOURCE_EXHAUSTED' } } }),
  broken: () => (provider === 'gemini' ? geminiResponse('{"isQuote": tru') : claudeMessage('{"isQuote": tru')),
};

const recognized = {
  isQuote: true,
  header: {
    clientName: '模擬客戶股份有限公司', clientTaxId: '24536806', clientAddress: null, contactName: '王小明', contactPhone: null, contactEmail: null,
    quoteDate: '114/10/29', validUntil: '2025-11-28', title: '監控系統更新', paymentTerms: '月結 30 天', deliveryTerms: null, terms: null,
    taxType: '營業稅 5% 外加', discount: null, sourceQuoteNo: 'OLD-0099',
  },
  items: [
    { lineType: 'section', itemCode: null, name: '設備', spec: null, unit: null, quantity: null, unitPrice: null, discountRate: null, amount: null, remark: null, uncertain: false },
    { lineType: 'item', itemCode: 'CAM-4K', name: '網路攝影機', spec: '4K 戶外型', unit: '台', quantity: 8, unitPrice: 6500, discountRate: null, amount: 52000, remark: null, uncertain: false },
    { lineType: 'item', itemCode: null, name: 'NVR 錄影主機', spec: '16 路', unit: '台', quantity: 1, unitPrice: 28000, discountRate: null, amount: 25000, remark: null, uncertain: true },
    { lineType: 'item', itemCode: null, name: '安裝調校', spec: null, unit: '式', quantity: null, unitPrice: null, discountRate: null, amount: 15000, remark: '含佈線', uncertain: false },
  ],
  totals: { subtotal: 92000, tax: 4600, total: 96600 },
  notes: ['NVR 金額欄有塗改'],
};

const mock = createServer((request, response) => {
  let raw = '';
  request.on('data', (chunk) => (raw += chunk));
  request.on('end', () => {
    requests.push({ path: request.url, headers: request.headers, body: raw ? JSON.parse(raw) : null });
    const { status, body } = reply();
    response.writeHead(status, { 'content-type': 'application/json', 'request-id': `req_${requests.length}` });
    response.end(JSON.stringify(body));
  });
});
await new Promise((resolve) => mock.listen(mockPort, '127.0.0.1', resolve));

// ---------- 共用 ----------

async function call(method, path, { token, body, form } = {}) {
  const headers = {};
  if (token) headers.authorization = `Bearer ${token}`;
  if (body !== undefined) headers['content-type'] = 'application/json';
  const response = await fetch(`${baseUrl}${path}`, { method, headers, body: form ?? (body === undefined ? undefined : JSON.stringify(body)) });
  const text = await response.text();
  return { status: response.status, data: text ? JSON.parse(text) : null };
}

async function login(account) {
  const result = await call('POST', '/api/auth/login', { body: { account, password: seedPassword } });
  assert.equal(result.status, 200, `${account} 登入失敗：${JSON.stringify(result.data)}`);
  return result.data.accessToken;
}

async function ok(promise, expected = [200, 201]) {
  const result = await promise;
  assert.ok(expected.includes(result.status), JSON.stringify(result.data));
  return result.data;
}

async function step(name, fn) {
  await fn();
  passed += 1;
  console.log(`✓ ${name}`);
}

const png = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.alloc(2048, 7)]);
const pdf = Buffer.concat([Buffer.from('%PDF-1.7\n'), Buffer.alloc(2048, 0x20), Buffer.from('\n%%EOF\n')]);
const upload = (token, bytes, fileName, type, kind = 'ocr') => {
  const form = new FormData();
  form.append('file', new Blob([bytes], { type }), fileName);
  form.append('kind', kind);
  return call('POST', '/api/quote-imports', { token, form });
};

let admin;
let sales;
let accountant;
let projectId;

try {
  await step('準備：新公司、業務與會計帳號、客戶與專案', async () => {
    const platform = await login('admin');
    await ok(call('POST', '/api/platform/companies', { token: platform, body: { name: `OCR ${suffix}`, slug: `ocr-${suffix}`, adminAccount: `ocr-${suffix}`, adminDisplayName: 'OCR 管理員', adminPassword: seedPassword } }));
    admin = await login(`ocr-${suffix}`);
    const root = (await ok(call('GET', '/api/admin/departments', { token: admin })))[0];
    const roles = await ok(call('GET', '/api/admin/roles', { token: admin }));
    for (const [account, role] of [['s', 'sales'], ['a', 'accountant']]) {
      await ok(call('POST', '/api/admin/users', { token: admin, body: { account: `ocr-${account}-${suffix}`, displayName: `OCR ${account}`, password: seedPassword, departmentId: root.id, roleId: roles.find((value) => value.code === role).id } }));
    }
    sales = await login(`ocr-s-${suffix}`);
    accountant = await login(`ocr-a-${suffix}`);
    const client = (await ok(call('POST', '/api/clients', { token: sales, body: { name: 'OCR 客戶' } }))).id;
    projectId = (await ok(call('POST', '/api/projects', { token: sales, body: { name: 'OCR 專案', clientId: client } }))).id;
  });

  await step('伺服器已設定金鑰：options 回報 ocrEnabled', async () => {
    const options = await ok(call('GET', '/api/quote-imports/options', { token: sales }));
    assert.equal(options.ocrEnabled, true);
    assert.ok(['claude', 'gemini'].includes(options.ocrProvider));
    provider = options.ocrProvider;
    console.log(`  （供應商：${provider}）`);
  });

  let job;
  await step('圖片辨識：送給供應商的請求格式正確', async () => {
    reply = () => replies.ok(recognized);
    requests.length = 0;
    const result = await upload(sales, png, '舊報價照片.png', 'image/png');
    assert.equal(result.status, 201, JSON.stringify(result.data));
    job = result.data;
    assert.equal(requests.length, 1);
    const [sent] = requests;
    if (provider === 'gemini') {
      assert.equal(sent.path, `/v1beta/models/${settings.Gemini.Model}:generateContent`);
      const level = settings.Gemini.ThinkingLevel.trim();
      assert.deepEqual(sent.body.generationConfig.thinkingConfig, level ? { thinkingLevel: level.toUpperCase() } : undefined, '強度（thinkingLevel）依設定檔');
      assert.equal(sent.headers['x-goog-api-key'], 'test-key');
      assert.ok(!sent.path.includes('key='), '金鑰不可放在網址');
      assert.match(sent.body.systemInstruction.parts[0].text, /報價單抄錄助手/);
      const [first] = sent.body.contents;
      assert.equal(first.role, 'user');
      assert.equal(first.parts[0].inlineData.mimeType, 'image/png');
      assert.equal(Buffer.from(first.parts[0].inlineData.data, 'base64').length, png.length);
      assert.ok(first.parts[1].text);
      const config = sent.body.generationConfig;
      assert.equal(config.responseMimeType, 'application/json');
      assert.equal(config.responseSchema.type, 'OBJECT');
      assert.deepEqual(config.responseSchema.required, ['isQuote', 'header', 'items', 'totals', 'notes']);
      assert.equal(config.responseSchema.additionalProperties, undefined, 'Gemini responseSchema 不支援 additionalProperties');
      const quantity = config.responseSchema.properties.items.items.properties.quantity;
      assert.deepEqual([quantity.type, quantity.nullable], ['NUMBER', true]);
    } else {
      assert.equal(sent.path, '/v1/messages');
      // SDK 13（API 版本 2026-10-21）把 API key 放在 Authorization: Bearer。
      assert.equal(sent.headers.authorization, 'Bearer test-key');
      assert.match(sent.headers['anthropic-beta'] ?? '', /server-side-fallback-2026-07-01/);
      assert.equal(sent.body.model, settings.Claude.Model);
      assert.equal(sent.body.fallbacks, 'default');
      assert.equal(sent.body.output_config.effort, settings.Claude.Effort.toLowerCase(), '強度（effort）依設定檔');
      assert.equal(sent.body.output_config.format.type, 'json_schema');
      assert.deepEqual(sent.body.output_config.format.schema.required, ['isQuote', 'header', 'items', 'totals', 'notes']);
      assert.equal(sent.body.output_config.format.schema.additionalProperties, false);
      assert.equal(sent.body.thinking, undefined, 'Opus 5.5 不可送 thinking 設定');
      assert.match(String(sent.body.system), /報價單抄錄助手/);
      const [first] = sent.body.messages;
      assert.equal(first.role, 'user');
      assert.equal(first.content[0].type, 'image');
      assert.deepEqual({ type: first.content[0].source.type, media: first.content[0].source.media_type }, { type: 'base64', media: 'image/png' });
      assert.equal(Buffer.from(first.content[0].source.data, 'base64').length, png.length);
      assert.equal(first.content[1].type, 'text');
    }
  });

  await step('預覽：民國日期、稅別轉換，金額不一致與看不清楚的列產生提醒', async () => {
    const { header, items, warnings } = job.preview;
    assert.equal(job.kind, 'ocr');
    assert.equal(header.QuoteDate, '2025-10-29');
    assert.equal(header.ValidUntil, '2025-11-28');
    assert.equal(header.TaxType, 'taxable');
    assert.equal(header.TaxRate, '5');
    assert.equal(header.ClientName, '模擬客戶股份有限公司');
    assert.equal(items.length, 4);
    assert.equal(items[0].lineType, 'section');
    assert.deepEqual([items[3].quantity, items[3].unitPrice], [1, 15000], '「一式」以金額當單價');
    assert.ok(warnings.some((value) => value.includes('NVR') && value.includes('看不清楚')));
    assert.ok(warnings.some((value) => value.includes('與文件金額 25,000')));
    assert.ok(warnings.some((value) => value.includes('明細加總 95,000')));
    assert.ok(warnings.some((value) => value.includes('塗改')));
  });

  await step('確認匯入：建立報價草稿（來源 ocr，金額由後端重算）', async () => {
    const created = await ok(call('POST', `/api/quote-imports/${job.id}/confirm`, {
      token: sales,
      body: {
        projectId,
        quote: {
          title: job.preview.header.Title,
          quoteDate: job.preview.header.QuoteDate,
          taxType: job.preview.header.TaxType,
          taxRate: Number(job.preview.header.TaxRate),
          items: job.preview.items.map(({ lineType, itemCode, name, spec, unit, quantity, unitPrice, discountRate, remark }) => ({ lineType, itemCode, name, spec, unit, quantity, unitPrice, discountRate, remark })),
        },
      },
    }));
    const quote = await ok(call('GET', `/api/quotes/${created.id}`, { token: sales }));
    assert.equal(quote.source, 'ocr');
    assert.equal(quote.status, 'draft');
    assert.equal(quote.subtotal, 95000);
    assert.equal(quote.taxAmount, 4750);
    assert.equal(quote.total, 99750);
    assert.equal((await call('POST', `/api/quote-imports/${job.id}/confirm`, { token: sales, body: { projectId, quote: { items: [{ name: 'x', quantity: 1, unitPrice: 1 }] } } })).status, 400, '同一份匯入只能確認一次');
  });

  await step('PDF 以供應商的文件格式送出', async () => {
    requests.length = 0;
    await ok(upload(sales, pdf, '掃描檔.pdf', 'application/pdf'));
    if (provider === 'gemini') {
      assert.equal(requests[0].body.contents[0].parts[0].inlineData.mimeType, 'application/pdf');
    } else {
      const block = requests[0].body.messages[0].content[0];
      assert.equal(block.type, 'document');
      assert.deepEqual([block.source.type, block.source.media_type], ['base64', 'application/pdf']);
    }
  });

  await step('不支援的格式與過大的圖片在呼叫供應商前就擋下', async () => {
    requests.length = 0;
    const heic = await upload(sales, Buffer.concat([Buffer.from([0, 0, 0, 0x18]), Buffer.from('ftypheic'), Buffer.alloc(64)]), 'IMG_0001.HEIC', 'image/heic');
    assert.equal(heic.status, 400);
    assert.match(heic.data.message, /HEIC/);
    const disguised = await upload(sales, Buffer.from('PK\u0003\u0004 not an image'), 'fake.png', 'image/png');
    assert.equal(disguised.status, 400);
    const large = await upload(sales, Buffer.concat([png, Buffer.alloc(5 * 1024 * 1024)]), 'huge.png', 'image/png');
    assert.equal(large.status, 400);
    assert.match(large.data.message, /5 MB/);
    assert.equal(requests.length, 0);
  });

  await step('拒絕、金鑰無效、忙碌、沒有辨識到品項、結果壞掉：回傳中文訊息', async () => {
    reply = replies.refused;
    const refused = await upload(sales, png, 'a.png', 'image/png');
    assert.equal(refused.status, 400);
    assert.match(refused.data.message, /拒絕/);
    if (provider === 'gemini') {
      reply = replies.blockedPrompt;
      assert.match((await upload(sales, png, 'a2.png', 'image/png')).data.message, /拒絕/);
    }
    reply = replies.badKey;
    const unauthorized = await upload(sales, png, 'b.png', 'image/png');
    assert.equal(unauthorized.status, 400);
    assert.match(unauthorized.data.message, /金鑰無效/);
    reply = replies.rateLimited;
    const limited = await upload(sales, png, 'b2.png', 'image/png');
    assert.equal(limited.status, 400);
    assert.match(limited.data.message, /忙碌/);
    reply = () => replies.ok({ ...recognized, isQuote: false, items: [] });
    const empty = await upload(sales, png, 'c.png', 'image/png');
    assert.equal(empty.status, 400);
    assert.match(empty.data.message, /沒有辨識到任何品項/);
    reply = replies.broken;
    const broken = await upload(sales, png, 'd.png', 'image/png');
    assert.equal(broken.status, 400);
    assert.match(broken.data.message, /無法解析/);
  });

  await step('權限：沒有 quotes:import_ocr 的會計不能使用', async () => {
    requests.length = 0;
    assert.equal((await upload(accountant, png, 'e.png', 'image/png')).status, 403);
    assert.equal((await call('GET', '/api/quote-imports/options', { token: accountant })).status, 403);
    assert.equal(requests.length, 0);
  });

  if (reloadTest) {
    await step('設定檔熱更新：改模型與強度後不重啟就生效；低於 medium 或寫錯時沿用上一份設定', async () => {
      const original = await readFile(settingsPath, 'utf8');
      const next = structuredClone(settings);
      next.Claude = { Model: 'claude-sonnet-5-5', Effort: 'high' };
      next.Gemini = { Model: 'gemini-3.5-flash-lite', ThinkingLevel: 'high' };
      const sentWith = async () => {
        reply = () => replies.ok(recognized);
        requests.length = 0;
        await ok(upload(sales, png, 'reload.png', 'image/png'));
        const [sent] = requests;
        return provider === 'gemini'
          ? { model: sent.path.split('/').pop().replace(':generateContent', ''), strength: sent.body.generationConfig.thinkingConfig?.thinkingLevel }
          : { model: sent.body.model, strength: sent.body.output_config.effort };
      };
      const expected = provider === 'gemini' ? { model: 'gemini-3.5-flash-lite', strength: 'HIGH' } : { model: 'claude-sonnet-5-5', strength: 'high' };
      try {
        await writeFile(settingsPath, `// 熱更新測試\n${JSON.stringify({ Ai: { Ocr: next } }, null, 2)}\n`);
        let actual;
        for (let i = 0; i < 20; i += 1) {
          await new Promise((resolve) => setTimeout(resolve, 500));
          actual = await sentWith();
          if (actual.model === expected.model) break;
        }
        assert.deepEqual(actual, expected);
        // 強度低於下限 medium、或寫錯 → API 記錄錯誤、沿用上一份正確設定。
        for (const [effort, level] of [['low', 'minimal'], ['ultra', 'max']]) {
          await writeFile(settingsPath, JSON.stringify({ Ai: { Ocr: { ...next, Claude: { ...next.Claude, Effort: effort }, Gemini: { ...next.Gemini, ThinkingLevel: level } } } }));
          await new Promise((resolve) => setTimeout(resolve, 3000));
          assert.deepEqual(await sentWith(), expected, `強度 ${effort}／${level} 應被拒絕`);
        }
      } finally {
        await writeFile(settingsPath, original);
      }
    });
  }

  await step('Excel 匯入不受影響：kind=ocr 以外仍只收 .xlsx', async () => {
    const result = await upload(sales, png, 'photo.png', 'image/png', 'excel_template');
    assert.equal(result.status, 400);
    assert.match(result.data.message, /xlsx/);
  });
} finally {
  mock.close();
}

console.log(`\n全部 ${passed} 項通過`);
