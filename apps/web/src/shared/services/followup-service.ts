/**
 * @file followup-service.ts
 * 專案跟進紀錄 API 與顯示用工具。
 *
 * 跟進紀錄一律掛在專案下，可見範圍與專案相同。專案的「下次聯絡」只看最新一筆，
 * 每次寫入後由後端重算並回傳 `project`，畫面用它更新，不自行推算。
 */
import { apiRequest } from '@/shared/api/http-client';

/** 跟進類型代碼 */
export type FollowUpType = 'call' | 'visit' | 'meeting' | 'email' | 'message' | 'other';

/** 跟進類型選項；順序即畫面上的按鈕順序 */
export const FOLLOW_UP_TYPES: { value: FollowUpType; label: string }[] = [
  { value: 'call', label: '電話' },
  { value: 'visit', label: '拜訪' },
  { value: 'meeting', label: '會議' },
  { value: 'email', label: 'Email' },
  { value: 'message', label: '訊息' },
  { value: 'other', label: '其他' },
];

/** 專案列表的跟進狀態篩選值 */
export type FollowUpFilter = 'pending' | 'overdue' | 'none';

/** 專案列表的跟進狀態篩選選項 */
export const FOLLOW_UP_FILTERS: { value: FollowUpFilter; label: string }[] = [
  { value: 'pending', label: '待跟進' },
  { value: 'overdue', label: '已逾期' },
  { value: 'none', label: '沒有下次聯絡' },
];

/** 不算「待跟進」的專案狀態，與後端 FollowUpRules.InactiveProjectStatuses 相同 */
const INACTIVE_PROJECT_STATUSES = ['lost', 'closed', 'cancelled'];

/** 人員摘要 */
export interface FollowUpUser {
  /** 使用者識別碼 */
  id: string;
  /** 顯示名稱 */
  displayName: string | null;
}

/** 一筆跟進紀錄 */
export interface FollowUp {
  /** 跟進紀錄識別碼 */
  id: string;
  /** 所屬專案識別碼 */
  projectId: string;
  /** 類型 */
  type: FollowUpType;
  /** 發生時間（ISO 8601） */
  occurredAt: string;
  /** 對象聯絡人識別碼 */
  contactId: string | null;
  /** 對象姓名 */
  contactName: string | null;
  /** 主旨 */
  summary: string;
  /** 內容 */
  content: string | null;
  /** 下次聯絡日（YYYY-MM-DD） */
  nextFollowUpOn: string | null;
  /** 下次要做的事 */
  nextAction: string | null;
  /** 按「不用跟進」的時間 */
  nextDismissedAt: string | null;
  /** 撰寫人 */
  createdBy: FollowUpUser;
  /** 建立時間 */
  createdAt: string;
  /** 最後修改人；沒有修改過為 null */
  updatedBy: FollowUpUser | null;
  /** 最後更新時間 */
  updatedAt: string;
}

/** 專案目前的跟進狀態（後端依最新一筆重算） */
export interface FollowUpProjectState {
  /** 最新一筆的發生時間 */
  lastFollowUpAt: string | null;
  /** 下次聯絡日（YYYY-MM-DD）；沒有排定為 null */
  nextFollowUpOn: string | null;
  /** 下次要做的事 */
  nextAction: string | null;
}

/** 新增或修改跟進紀錄的輸入 */
export type FollowUpInput = {
  type: FollowUpType;
  occurredAt: string;
  contactId: string | null;
  contactName: string | null;
  summary: string;
  content: string | null;
  nextFollowUpOn: string | null;
  nextAction: string | null;
};

/** 專案內列表結果 */
export interface ProjectFollowUpList {
  /** 是否可新增 */
  canCreate: boolean;
  /** 是否可修改（含「不用跟進」） */
  canUpdate: boolean;
  /** 是否可刪除 */
  canDelete: boolean;
  /** 總筆數 */
  total: number;
  /** 目前頁碼 */
  page: number;
  /** 每頁筆數 */
  pageSize: number;
  /** 本頁紀錄（最新一筆在前） */
  items: FollowUp[];
  /** 專案目前的跟進狀態 */
  project: FollowUpProjectState;
}

/** 跨專案總表的一列 */
export interface FollowUpRow extends FollowUp {
  /** 所屬專案摘要 */
  project: { id: string; projectNo: string; name: string; status: string; nextFollowUpOn: string | null } | null;
  /** 客戶名稱 */
  clientName: string | null;
}

/** 跨專案總表的查詢條件；全部由後端處理 */
export type FollowUpSearchQuery = {
  keyword?: string;
  type?: string;
  createdBy?: string;
  departmentId?: string;
  projectId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
  sort?: 'occurredAt' | 'createdAt';
  desc?: boolean;
};

/** 表單下拉選單用的聯絡人 */
export interface FollowUpContact {
  /** 聯絡人識別碼 */
  id: string;
  /** 姓名 */
  name: string;
  /** 職稱 */
  title: string | null;
}

/** 下次聯絡的提醒狀態 */
export interface FollowUpDue {
  /** none 沒有排定、inactive 專案已停止、overdue 逾期、today 今天、upcoming 之後 */
  state: 'none' | 'inactive' | 'overdue' | 'today' | 'upcoming';
  /** 與今天相差的天數（逾期為負數） */
  days: number;
}

/**
 * 取得跟進類型的中文名稱
 *
 * @param type 類型代碼
 * @returns 中文名稱；未知代碼原樣回傳
 */
export function followUpTypeLabel(type: string): string {
  return FOLLOW_UP_TYPES.find((option) => option.value === type)?.label ?? type;
}

/**
 * 台灣時間的今天（與後端 BusinessTime.Today 相同的判斷基準）
 *
 * @returns YYYY-MM-DD
 */
export function taipeiToday(): string {
  return new Date(Date.now() + 8 * 3600_000).toISOString().slice(0, 10);
}

/**
 * 計算下次聯絡的提醒狀態（與後端專案列表的待跟進、逾期規則一致）
 *
 * @param nextFollowUpOn 下次聯絡日（YYYY-MM-DD）
 * @param projectStatus 專案狀態
 * @returns 提醒狀態與相差天數
 */
export function followUpDue(nextFollowUpOn: string | null | undefined, projectStatus: string | null | undefined): FollowUpDue {
  if (!nextFollowUpOn) return { state: 'none', days: 0 };
  const days = Math.round((Date.parse(nextFollowUpOn) - Date.parse(taipeiToday())) / 86400_000);
  if (INACTIVE_PROJECT_STATUSES.includes(projectStatus ?? '')) return { state: 'inactive', days };
  return { state: days < 0 ? 'overdue' : days === 0 ? 'today' : 'upcoming', days };
}

/**
 * 查詢跨專案跟進紀錄總表
 *
 * @param query 篩選、排序與分頁條件
 * @returns 分頁結果
 */
export function listFollowUpsApi(query: FollowUpSearchQuery): Promise<{ total: number; page: number; pageSize: number; items: FollowUpRow[] }> {
  return apiRequest('/api/followups', { query: { ...query } });
}

/**
 * 查詢專案內的跟進紀錄
 *
 * @param projectId 專案識別碼
 * @param query 關鍵字、類型與分頁
 * @returns 分頁結果、寫入旗標與專案目前的跟進狀態
 */
export function listProjectFollowUpsApi(
  projectId: string,
  query: { keyword?: string; type?: string; page?: number; pageSize?: number } = {}
): Promise<ProjectFollowUpList> {
  return apiRequest(`/api/projects/${projectId}/followups`, { query: { ...query } });
}

/**
 * 查詢專案客戶的聯絡人（由後端依關鍵字搜尋，最多 20 筆）
 *
 * @param projectId 專案識別碼
 * @param keyword 姓名或職稱關鍵字
 * @returns 聯絡人清單
 */
export function listFollowUpContactsApi(projectId: string, keyword = ''): Promise<FollowUpContact[]> {
  return apiRequest(`/api/projects/${projectId}/followups/contacts`, { query: { keyword: keyword || undefined } });
}

/**
 * 新增跟進紀錄
 *
 * @param projectId 專案識別碼
 * @param input 紀錄內容
 * @returns 新紀錄與專案重算後的跟進狀態
 */
export function createFollowUpApi(projectId: string, input: FollowUpInput): Promise<{ item: FollowUp; project: FollowUpProjectState }> {
  return apiRequest(`/api/projects/${projectId}/followups`, { method: 'POST', body: input });
}

/**
 * 修改跟進紀錄
 *
 * @param id 跟進紀錄識別碼
 * @param input 紀錄內容
 * @returns 修改後的紀錄與專案重算後的跟進狀態
 */
export function updateFollowUpApi(id: string, input: FollowUpInput): Promise<{ item: FollowUp; project: FollowUpProjectState }> {
  return apiRequest(`/api/followups/${id}`, { method: 'PUT', body: input });
}

/**
 * 刪除跟進紀錄
 *
 * @param id 跟進紀錄識別碼
 * @returns 專案重算後的跟進狀態
 */
export function deleteFollowUpApi(id: string): Promise<{ project: FollowUpProjectState }> {
  return apiRequest(`/api/followups/${id}`, { method: 'DELETE' });
}

/**
 * 不用跟進：清掉專案目前的下次聯絡日
 *
 * @param projectId 專案識別碼
 * @returns 專案重算後的跟進狀態
 */
export function dismissNextFollowUpApi(projectId: string): Promise<{ project: FollowUpProjectState }> {
  return apiRequest(`/api/projects/${projectId}/followups/dismiss-next`, { method: 'POST' });
}
