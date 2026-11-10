/**
 * @file notification-service.ts
 * 站內通知 API。目前只有「跟進到期」：專案的下次聯絡日到了或逾期，由後端每天產生，通知負責人與排定下次聯絡的人。
 *
 * 通知只記專案與到期日；顯示的專案名稱與客戶是每次查詢時依目前的資料範圍組出來的，看不到的專案不會出現在通知裡。
 * 列表只含「仍待處理」的通知：跟進紀錄被刪除、改期或按了「不用跟進」後，舊通知就消失，也不算在未讀數裡。
 */
import { apiRequest } from '@/shared/api/http-client';

/** 一則通知 */
export interface NotificationItem {
  /** 通知識別碼 */
  id: string;
  /** 通知類型；目前只有 followup_due */
  type: string;
  /** 相關專案識別碼 */
  projectId: string;
  /** 觸發通知的下次聯絡日（YYYY-MM-DD） */
  dueOn: string;
  /** 通知產生時間（ISO 8601） */
  createdAt: string;
  /** 已讀時間；未讀為 null */
  readAt: string | null;
  /** 專案摘要 */
  project: { projectNo: string; name: string; status: string; clientName: string } | null;
}

/** 通知列表結果 */
export interface NotificationList {
  /** 符合條件的總筆數 */
  total: number;
  /** 未讀則數（只算仍待處理的，不受 unread 篩選影響） */
  unreadCount: number;
  /** 頁碼 */
  page: number;
  /** 每頁筆數 */
  pageSize: number;
  /** 本頁通知（只含仍待處理的；未讀在前，其次新到舊） */
  items: NotificationItem[];
}

/**
 * 查詢自己的通知
 *
 * @param query 是否只看未讀與分頁
 * @returns 通知與未讀則數
 */
export function listNotificationsApi(query: { unread?: boolean; page?: number; pageSize?: number } = {}): Promise<NotificationList> {
  return apiRequest('/api/notifications', { query: { ...query } });
}

/**
 * 把一則通知標為已讀
 *
 * @param id 通知識別碼
 */
export function markNotificationReadApi(id: string): Promise<void> {
  return apiRequest(`/api/notifications/${id}/read`, { method: 'POST' });
}

/** 全部標為已讀 */
export function markAllNotificationsReadApi(): Promise<void> {
  return apiRequest('/api/notifications/read-all', { method: 'POST' });
}
