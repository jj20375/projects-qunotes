/**
 * @file route-manifest.ts
 * 後台已實作的路由與所需權限。
 *
 * 左側選單由 GET /api/auth/menu 依權限產生，再以這份清單排除尚未實作的功能；
 * AdminShell 也用它在直接輸入網址時再檢查一次權限（沒權限顯示 403）。
 */
import type { PermissionRequirement } from '@/shared/types/permissions';

export type RouteKey =
  | 'dashboard'
  | 'companies'
  | 'system-users'
  | 'platform-roles'
  | 'departments'
  | 'users'
  | 'roles'
  | 'clients'
  | 'projects'
  | 'project-detail'
  | 'quotes'
  | 'quote-edit'
  | 'products'
  | 'settings';

export interface RouteManifestItem {
  key: RouteKey;
  path: string;
  title: string;
  /** 不在選單上的子頁面，指定要反白的選單項目路徑。 */
  menuPath?: string;
  /** 只在此工作範圍顯示；未指定代表兩種範圍都可進入。 */
  scope?: 'platform' | 'company';
  requirement?: PermissionRequirement;
}

export const routeManifest: RouteManifestItem[] = [
  { key: 'dashboard', path: '/', title: '首頁' },
  { key: 'companies', path: '/platform/companies', title: '公司管理', scope: 'platform', requirement: { code: 'platform.companies' } },
  { key: 'system-users', path: '/platform/system-users', title: '系統帳號', scope: 'platform', requirement: { code: 'platform.system_users' } },
  { key: 'platform-roles', path: '/platform/roles', title: '系統角色', scope: 'platform', requirement: { code: 'platform.roles' } },
  { key: 'clients', path: '/clients', title: '客戶', scope: 'company', requirement: { code: 'clients' } },
  { key: 'projects', path: '/projects', title: '專案', scope: 'company', requirement: { code: 'projects' } },
  { key: 'project-detail', path: '/projects/detail', title: '專案明細', menuPath: '/projects', scope: 'company', requirement: { code: 'projects' } },
  { key: 'quotes', path: '/quotes', title: '報價單', scope: 'company', requirement: { code: 'quotes' } },
  { key: 'quote-edit', path: '/quotes/edit', title: '報價單', menuPath: '/quotes', scope: 'company', requirement: { code: 'quotes' } },
  { key: 'products', path: '/products', title: '品項價目', scope: 'company', requirement: { code: 'products' } },
  { key: 'departments', path: '/admin/departments', title: '部門', scope: 'company', requirement: { code: 'company.departments' } },
  { key: 'users', path: '/admin/users', title: '帳號與成員', scope: 'company', requirement: { code: 'company.users' } },
  { key: 'roles', path: '/admin/roles', title: '角色權限', scope: 'company', requirement: { code: 'company.roles' } },
  { key: 'settings', path: '/admin/settings', title: '公司參數', scope: 'company', requirement: { code: 'company.settings' } },
];

export const implementedPaths = new Set(routeManifest.map((route) => route.path));

export function findRoute(key: RouteKey): RouteManifestItem {
  return routeManifest.find((route) => route.key === key)!;
}
