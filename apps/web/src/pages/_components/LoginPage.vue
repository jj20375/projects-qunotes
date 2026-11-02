<script setup lang="ts">
/**
 * @component LoginPage
 * 帳號密碼＋圖形驗證碼登入。成功後 refresh token 由伺服器寫入 HttpOnly cookie，
 * 只把 access token 存在前端；redirect 參數只接受站內路徑，避免開放式轉址。
 */
import { onMounted, reactive, ref } from 'vue';
import zhTW from 'ant-design-vue/es/locale/zh_TW';
import { getCaptchaApi, loginApi } from '@/shared/services/auth-service';

const form = reactive({ account: '', password: '', captchaCode: '' });
const captcha = ref<{ enabled: boolean; captchaId?: string; image?: string }>({ enabled: false });
const submitting = ref(false);
const error = ref('');

async function loadCaptcha() {
  form.captchaCode = '';
  try {
    captcha.value = await getCaptchaApi();
  } catch {
    error.value = '無法連線到伺服器，請稍後再試。';
  }
}

function safeRedirect(): string {
  const target = new URLSearchParams(window.location.search).get('redirect') ?? '/';
  return target.startsWith('/') && !target.startsWith('//') ? target : '/';
}

async function submit() {
  error.value = '';
  submitting.value = true;
  try {
    await loginApi({ account: form.account, password: form.password, captchaId: captcha.value.captchaId, captchaCode: form.captchaCode });
    window.location.assign(safeRedirect());
  } catch (exception) {
    error.value = (exception as Error).message;
    await loadCaptcha();
  } finally {
    submitting.value = false;
  }
}

onMounted(loadCaptcha);
</script>

<template>
  <a-config-provider :locale="zhTW" :theme="{ token: { colorPrimary: '#1f4e79', borderRadius: 6 } }">
    <main class="login">
      <a-card class="login-card" :bordered="false">
        <h1>專案報價管理系統</h1>
        <p class="hint">請使用公司提供的帳號登入</p>
        <a-alert v-if="error" type="error" :message="error" show-icon class="login-error" />
        <a-form layout="vertical" :model="form" @finish="submit">
          <a-form-item label="帳號" name="account" :rules="[{ required: true, message: '請輸入帳號' }]">
            <a-input v-model:value="form.account" autocomplete="username" size="large" />
          </a-form-item>
          <a-form-item label="密碼" name="password" :rules="[{ required: true, message: '請輸入密碼' }]">
            <a-input-password v-model:value="form.password" autocomplete="current-password" size="large" />
          </a-form-item>
          <a-form-item v-if="captcha.enabled" label="驗證碼" name="captchaCode" :rules="[{ required: true, message: '請輸入驗證碼' }]">
            <div class="captcha">
              <a-input v-model:value="form.captchaCode" size="large" autocomplete="off" />
              <img :src="captcha.image" alt="驗證碼，點擊更換" title="點擊更換" @click="loadCaptcha" />
            </div>
          </a-form-item>
          <a-button type="primary" html-type="submit" size="large" block :loading="submitting">登入</a-button>
        </a-form>
      </a-card>
    </main>
  </a-config-provider>
</template>

<style scoped>
.login {
  display: grid;
  place-items: center;
  min-height: 100vh;
  padding: 16px;
  box-sizing: border-box;
  background: linear-gradient(160deg, #eaf0f7, #f5f6f8);
}
.login-card {
  width: 100%;
  max-width: 400px;
  box-shadow: 0 8px 32px rgba(31, 78, 121, 0.12);
}
h1 {
  margin: 0;
  font-size: 22px;
  color: #1f4e79;
}
.hint {
  margin: 4px 0 20px;
  color: #888;
}
.login-error {
  margin-bottom: 16px;
}
.captcha {
  display: flex;
  gap: 8px;
}
.captcha img {
  height: 40px;
  cursor: pointer;
  border-radius: 6px;
  border: 1px solid #e5e7eb;
}
</style>
