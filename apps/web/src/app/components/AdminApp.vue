<script setup lang="ts">
/**
 * @component AdminApp
 * 每個後台頁面唯一的 Vue island：外框與授權由 AdminShell 處理，
 * 頁面本體以 async component 載入，確保只在登入者與權限確認後才掛載、才開始打 API。
 */
import { defineAsyncComponent, type Component } from 'vue';
import AdminShell from '@/app/components/AdminShell.vue';
import type { RouteKey } from '@/app/route-manifest';

const props = defineProps<{ routeKey: RouteKey }>();

const views: Record<RouteKey, () => Promise<{ default: Component }>> = {
  dashboard: () => import('@/pages/_components/DashboardView.vue'),
  office: () => import('@/pages/office/_components/OfficeView.vue'),
  companies: () => import('@/pages/platform/companies/_components/CompaniesView.vue'),
  'system-users': () => import('@/pages/platform/system-users/_components/SystemUsersView.vue'),
  'platform-roles': () => import('@/pages/admin/roles/_components/RolesView.vue'),
  departments: () => import('@/pages/admin/departments/_components/DepartmentsView.vue'),
  users: () => import('@/pages/admin/users/_components/UsersView.vue'),
  roles: () => import('@/pages/admin/roles/_components/RolesView.vue'),
  clients: () => import('@/pages/clients/_components/ClientsView.vue'),
  projects: () => import('@/pages/projects/_components/ProjectsView.vue'),
  'project-detail': () => import('@/pages/projects/detail/_components/ProjectDetailView.vue'),
  quotes: () => import('@/pages/quotes/_components/QuotesView.vue'),
  'quote-edit': () => import('@/pages/quotes/edit/_components/QuoteEditorView.vue'),
  products: () => import('@/pages/products/_components/ProductsView.vue'),
  settings: () => import('@/pages/admin/settings/_components/CompanySettingsView.vue'),
};

const View = defineAsyncComponent(views[props.routeKey]);
</script>

<template>
  <AdminShell :route-key="props.routeKey">
    <View :mode="props.routeKey === 'platform-roles' ? 'platform' : 'company'" />
  </AdminShell>
</template>
