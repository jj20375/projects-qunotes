import type { PermissionGrant } from '@/shared/types/permissions';

export interface AuthUser {
  id: string;
  account: string;
  displayName: string;
  email: string | null;
  isSystemUser: boolean;
}

export interface AuthScope {
  kind: 'platform' | 'company';
  companyId: string | null;
  companyName: string | null;
  membershipId: string | null;
  departmentId: string | null;
  departmentName: string | null;
  dataScope: string;
  isImpersonating: boolean;
  isRootAdmin: boolean;
}

/** GET /api/auth/me */
export interface Me {
  user: AuthUser;
  scope: AuthScope;
  grants: PermissionGrant[];
}

export interface MenuNode {
  code: string;
  name: string;
  routePath: string | null;
  children: MenuNode[];
}

export interface ScopeOption {
  scopeKind: 'platform' | 'company';
  companyId: string | null;
  name: string;
  isImpersonation: boolean;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
}
