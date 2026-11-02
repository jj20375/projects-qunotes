import { inject, type InjectionKey, type Ref } from 'vue';
import type { Me } from '@/shared/types/auth';
import { hasOption, hasPermission, type PermissionAction, type PermissionCode } from '@/shared/types/permissions';

export const ME_KEY: InjectionKey<Ref<Me | null>> = Symbol('me');

/** 頁面內取得目前登入者；AdminShell 只會在 me 載入完成後才渲染頁面內容。 */
export function useMe() {
  const me = inject(ME_KEY)!;
  return {
    me,
    can: (code: PermissionCode, action: PermissionAction = 'read') => hasPermission(me.value?.grants, code, action),
    canOption: (code: PermissionCode, option: string) => hasOption(me.value?.grants, code, option),
  };
}
