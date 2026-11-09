<script setup lang="ts">
/**
 * @component AdminShell
 * 後台共用外框兼授權閘門：
 * - 沒有 access token 時先嘗試用 refresh cookie 換新，失敗才導向登入頁。
 * - 依 route-manifest 檢查目前工作範圍與權限；不足時顯示 403，不渲染頁面內容。
 * - 左側選單來自 /api/auth/menu，並排除尚未實作的路由。
 * 頁面內容以 slot 取得 me（使用者、工作範圍、有效權限）。
 */
import { computed, onMounted, provide, ref } from 'vue';
import zhTW from 'ant-design-vue/es/locale/zh_TW';
import dayjs from 'dayjs';
import 'dayjs/locale/zh-tw';
import { message } from 'ant-design-vue';
import type { ItemType } from 'ant-design-vue';
import { findRoute, implementedPaths, type RouteKey } from '@/app/route-manifest';
import { readAccessToken, redirectToLogin, refreshAccessToken } from '@/shared/api/http-client';
import { getMeApi, getMenuApi, listScopesApi, logoutApi, SCOPE_PAGE_SIZE, selectScopeApi } from '@/shared/services/auth-service';
import type { Me, MenuNode, ScopeOption } from '@/shared/types/auth';
import { DATA_SCOPE_LABELS, meetsRequirement } from '@/shared/types/permissions';
import NotificationBell from '@/app/components/NotificationBell.vue';
import { ME_KEY } from '@/app/composables/use-me';
import { startPresence } from '@/app/composables/use-presence';

dayjs.locale('zh-tw');

const props = defineProps<{ routeKey: RouteKey }>();
const route = findRoute(props.routeKey);

const me = ref<Me | null>(null);
const menu = ref<MenuNode[]>([]);
const scopes = ref<ScopeOption[]>([]);
/** 第一次載入時有兩個以上的範圍才顯示切換選單；之後的搜尋結果可能只剩一筆，不能拿來判斷。 */
const canSwitchScope = ref(false);
let scopeSearchTimer: ReturnType<typeof setTimeout> | undefined;
const forbidden = ref(false);
const switching = ref(false);
provide(ME_KEY, me);

const menuItems = computed<ItemType[]>(() => {
  const toItem = (node: MenuNode): ItemType | null => {
    if (node.routePath) return implementedPaths.has(node.routePath) ? { key: node.routePath, label: node.name } : null;
    const children = node.children.map(toItem).filter((item): item is ItemType => item !== null);
    return children.length ? { key: node.code, label: node.name, children } : null;
  };
  return [{ key: '/', label: '首頁' }, ...menu.value.filter((node) => node.routePath !== '/').map(toItem).filter((item): item is ItemType => item !== null)];
});

/** 群組預設全部展開；選單項目少，收合反而讓人找不到功能。 */
const openKeys = ref<string[]>([]);
/** 窄螢幕預設收合側欄，改由 header 左側按鈕開啟（覆蓋在內容上方）。 */
const collapsed = ref(typeof window !== 'undefined' && window.innerWidth < 768);
const currentScopeValue = computed(() => (me.value?.scope.kind === 'platform' ? 'platform' : me.value?.scope.companyId ?? undefined));
const scopeOptions = computed(() =>
  scopes.value.map((scope) => ({
    value: scope.scopeKind === 'platform' ? 'platform' : scope.companyId!,
    label: scope.isImpersonation ? `${scope.name}（以平台身分進入）` : scope.name,
  })),
);

onMounted(async () => {
  if (!readAccessToken() && !(await refreshAccessToken())) {
    redirectToLogin();
    return;
  }
  const [meResult, menuResult, scopeResult] = await Promise.all([getMeApi(), getMenuApi(), listScopesApi()]);
  me.value = meResult;
  menu.value = menuResult;
  openKeys.value = menuResult.filter((node) => !node.routePath).map((node) => node.code);
  scopes.value = scopeResult;
  scopeOffset = Math.min(scopeResult.filter((scope) => scope.scopeKind === 'company').length, SCOPE_PAGE_SIZE);
  scopeHasMore = scopeOffset >= SCOPE_PAGE_SIZE;
  canSwitchScope.value = scopeResult.length > 1;
  forbidden.value = (route.scope !== undefined && route.scope !== meResult.scope.kind) || (route.requirement !== undefined && !meetsRequirement(meResult.grants, route.requirement));
  // 上線狀態與敲門通知在後台任何頁面都要有；權限不足時 startPresence 自己會略過。
  void startPresence(meResult);
});

/** 公司很多時輸入名稱搜尋，由後端過濾（GET /api/auth/scopes?keyword=）；往下捲到底再向後端要下一頁（offset）。 */
let scopeKeyword = '';
let scopeOffset = 0;
let scopeHasMore = false;
let scopeLoading = false;

async function loadScopes(keyword: string, append: boolean) {
  if (scopeLoading) return;
  scopeLoading = true;
  try {
    const page = await listScopesApi(keyword, append ? scopeOffset : 0);
    // 第一頁可能多附上目前所在的公司（不屬於這一頁的筆數），位移最多算一頁。
    const companies = Math.min(page.filter((scope) => scope.scopeKind === 'company').length, SCOPE_PAGE_SIZE);
    scopeKeyword = keyword;
    scopeOffset = (append ? scopeOffset : 0) + companies;
    scopeHasMore = companies >= SCOPE_PAGE_SIZE;
    const known = new Set(scopes.value.map((scope) => scope.companyId ?? 'platform'));
    scopes.value = append ? [...scopes.value, ...page.filter((scope) => !known.has(scope.companyId ?? 'platform'))] : page;
  } catch {
    // 查詢失敗時保留目前清單。
  } finally {
    scopeLoading = false;
  }
}

function searchScopes(keyword: string) {
  clearTimeout(scopeSearchTimer);
  scopeSearchTimer = setTimeout(() => loadScopes(keyword, false), 250);
}

function onScopePopupScroll(event: Event) {
  const list = event.target as HTMLElement;
  if (scopeHasMore && list.scrollTop + list.clientHeight >= list.scrollHeight - 24) loadScopes(scopeKeyword, true);
}

async function switchScope(value: unknown) {
  const target = scopes.value.find((scope) => (scope.scopeKind === 'platform' ? 'platform' : scope.companyId) === value);
  if (!target) return;
  switching.value = true;
  try {
    await selectScopeApi(target.scopeKind, target.companyId);
    window.location.assign('/');
  } catch (error) {
    message.error((error as Error).message);
    switching.value = false;
  }
}

/** 後台是多頁靜態站，選單直接換頁；群組節點的 key 是權限代碼，不會觸發 click。 */
function navigate({ key }: { key: string | number }) {
  window.location.assign(String(key));
}

async function logout() {
  await logoutApi();
  window.location.assign('/login');
}
</script>

<template>
  <a-config-provider :locale="zhTW" :theme="{ token: { colorPrimary: '#1f4e79', borderRadius: 6 } }">
    <div v-if="!me" class="shell-loading"><a-spin size="large" /></div>
    <a-layout v-else class="shell">
      <a-layout-sider
        v-model:collapsed="collapsed"
        width="220"
        theme="light"
        class="shell-sider"
        breakpoint="md"
        :collapsed-width="0"
        :trigger="null"
      >
        <div class="brand">專案報價管理</div>
        <!-- 收合（寬度 0）時 AntD 會把 inline 選單改成浮出子選單，展開中的群組會一直浮在畫面左上角，所以收合時不渲染。 -->
        <a-menu v-if="!collapsed" v-model:open-keys="openKeys" mode="inline" :selected-keys="[route.menuPath ?? route.path]" :items="menuItems" @click="navigate" />
      </a-layout-sider>
      <div v-if="!collapsed" class="shell-mask" @click="collapsed = true" />
      <a-layout>
        <a-layout-header class="shell-header">
          <div class="shell-title">
            <a-button type="text" class="shell-toggle" aria-label="切換選單" @click="collapsed = !collapsed">☰</a-button>
            <span>{{ route.title }}</span>
          </div>
          <a-space :size="16">
            <a-select
              v-if="canSwitchScope"
              :value="currentScopeValue"
              :options="scopeOptions"
              :loading="switching"
              show-search
              :filter-option="false"
              placeholder="輸入公司名稱搜尋"
              class="shell-scope"
              @search="searchScopes"
              @popup-scroll="onScopePopupScroll"
              @change="switchScope"
            />
            <span v-else class="shell-company">{{ me.scope.companyName ?? '平台管理' }}</span>
            <NotificationBell v-if="me.scope.kind === 'company' && meetsRequirement(me.grants, { code: 'followups' })" />
            <a-dropdown>
              <a-button type="text">{{ me.user.displayName }}</a-button>
              <template #overlay>
                <a-menu>
                  <a-menu-item disabled>
                    {{ me.user.account }}
                    <template v-if="me.scope.departmentName"> · {{ me.scope.departmentName }}</template>
                  </a-menu-item>
                  <a-menu-item v-if="me.scope.kind === 'company'" disabled>資料範圍：{{ DATA_SCOPE_LABELS[me.scope.dataScope] }}</a-menu-item>
                  <a-menu-divider />
                  <a-menu-item @click="logout">登出</a-menu-item>
                </a-menu>
              </template>
            </a-dropdown>
          </a-space>
        </a-layout-header>
        <a-alert
          v-if="me.scope.isImpersonating"
          type="warning"
          banner
          :message="`目前以平台管理員身分進入「${me.scope.companyName}」，擁有公司管理員權限，所有操作都會記錄。`"
        />
        <a-layout-content class="shell-content">
          <a-result v-if="forbidden" status="403" title="沒有權限" sub-title="目前的工作範圍或權限無法使用這個功能。">
            <template #extra><a-button type="primary" href="/">回首頁</a-button></template>
          </a-result>
          <slot v-else :me="me" />
        </a-layout-content>
      </a-layout>
    </a-layout>
  </a-config-provider>
</template>

<style scoped>
.shell {
  min-height: 100vh;
}
.shell-loading {
  display: grid;
  place-items: center;
  min-height: 100vh;
}
.shell-sider {
  border-right: 1px solid #eef0f3;
}
.brand {
  padding: 18px 24px;
  font-weight: 600;
  font-size: 16px;
  color: #1f4e79;
}
.shell-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 0 24px;
  background: #fff;
  border-bottom: 1px solid #eef0f3;
}
.shell-title {
  display: flex;
  align-items: center;
  gap: 4px;
  min-width: 0;
  font-size: 18px;
  font-weight: 600;
  white-space: nowrap;
}
.shell-toggle {
  display: none;
}
.shell-scope {
  width: 240px;
}
.shell-company {
  display: inline-block;
  max-width: 240px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  vertical-align: middle;
  color: #555;
}
.shell-mask {
  display: none;
}
.shell-content {
  padding: 24px;
  min-width: 0;
}
@media (max-width: 767px) {
  .shell-sider {
    position: fixed !important;
    inset: 0 auto 0 0;
    z-index: 30;
    height: 100vh;
  }
  .shell-mask {
    display: block;
    position: fixed;
    inset: 0;
    z-index: 20;
    background: rgba(0, 0, 0, 0.35);
  }
  .shell-toggle {
    display: inline-flex;
  }
  .shell-header {
    padding: 0 12px;
  }
  .shell-title {
    font-size: 16px;
  }
  .shell-scope {
    width: 150px;
  }
  .shell-company {
    max-width: 30vw;
  }
  .shell-content {
    padding: 16px;
  }
}
</style>
