<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiUrl } from '@/utils/api'
import { fetchCenters } from '@/utils/centers'

const router = useRouter()
const { t, locale } = useI18n()

const username = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const center = ref('')
const submitting = ref(false)
const error = ref('')
const success = ref('')
const centers = ref([])

async function loadCenters() {
  try {
    centers.value = await fetchCenters()
  } catch {
    centers.value = []
  }
}

const toggleLanguage = () => {
  const next = locale.value === 'sl' ? 'en' : 'sl'
  locale.value = next
  localStorage.setItem('lang', next)
}

const savedLang = localStorage.getItem('lang')
if (savedLang === 'sl' || savedLang === 'en') {
  locale.value = savedLang
}

onMounted(() => {
  loadCenters()
})

const handleSubmit = async () => {
  error.value = ''
  success.value = ''

  if (!username.value || !email.value || !password.value || !confirmPassword.value || !center.value) {
    error.value = t('signup.errors.required')
    return
  }

  if (password.value !== confirmPassword.value) {
    error.value = t('signup.errors.passwordMismatch')
    return
  }

  submitting.value = true
  try {
    const response = await fetch(apiUrl('/Users/signup'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: username.value,
        email: email.value,
        password: password.value,
        confirmPassword: confirmPassword.value,
        center: Number(center.value)
      })
    })

    if (!response.ok) {
      const msg = (await response.text())?.trim()
      error.value = msg || t('signup.errors.createFailed')
      return
    }

    success.value = t('signup.success.created')
    username.value = ''
    email.value = ''
    password.value = ''
    confirmPassword.value = ''
    center.value = ''

    setTimeout(() => router.push('/login'), 1200)
  } catch {
    error.value = t('signup.errors.createFailed')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="signup-page">
    <div class="signup-card surface">
      <div class="header-row">
        <h1 class="title section-title">{{ $t('signup.title') }}</h1>
        <button type="button" class="lang-toggle" @click="toggleLanguage">
          {{ locale === 'sl' ? 'EN' : 'SL' }}
        </button>
      </div>
      <p class="subtitle">{{ $t('signup.subtitle') }}</p>

      <form class="form" @submit.prevent="handleSubmit">
        <div class="field">
          <label class="label" for="username">{{ $t('signup.username') }}</label>
          <input
            id="username"
            class="input"
            type="text"
            :placeholder="$t('signup.usernamePlaceholder')"
            required
            autocomplete="username"
            v-model="username"
          />
        </div>

        <div class="field">
          <label class="label" for="email">{{ $t('signup.email') }}</label>
          <input
            id="email"
            class="input"
            type="email"
            :placeholder="$t('signup.emailPlaceholder')"
            required
            autocomplete="email"
            v-model="email"
          />
        </div>

        <div class="field">
          <label class="label" for="center">{{ $t('signup.center') }}</label>
          <select id="center" class="input" v-model="center" required>
            <option value="">{{ $t('signup.centerPlaceholder') }}</option>
            <option v-for="c in centers" :key="c.id" :value="c.id">
              {{ c.id }} - {{ c.name }}
            </option>
          </select>
        </div>

        <div class="field">
          <label class="label" for="password">{{ $t('signup.password') }}</label>
          <input
            id="password"
            class="input"
            type="password"
            :placeholder="$t('signup.passwordPlaceholder')"
            required
            autocomplete="new-password"
            v-model="password"
          />
        </div>

        <div class="field">
          <label class="label" for="confirmPassword">{{ $t('signup.confirmPassword') }}</label>
          <input
            id="confirmPassword"
            class="input"
            type="password"
            :placeholder="$t('signup.confirmPasswordPlaceholder')"
            required
            autocomplete="new-password"
            v-model="confirmPassword"
          />
        </div>

        <button class="btn" type="submit" :disabled="submitting">
          {{ submitting ? $t('signup.submitting') : $t('signup.submit') }}
        </button>

        <p v-if="error" class="msg error">{{ error }}</p>
        <p v-if="success" class="msg success">{{ success }}</p>
      </form>
    </div>
  </div>
</template>

<style scoped>
.signup-page {
  min-height: 100vh;
  min-height: 100svh;
  min-height: 100dvh;
  display: grid;
  place-items: center;
  padding: 24px;
  background: var(--bg);
  color: var(--text);
}

.signup-card {
  width: 100%;
  max-width: 460px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 16px;
  padding: 28px;
  box-shadow: 0 18px 50px rgba(0, 0, 0, 0.35);
}

.title {
  margin: 0;
  font-size: 28px;
}

.header-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.lang-toggle {
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  border-radius: 10px;
  padding: 7px 12px;
  font-size: 12px;
  font-weight: 700;
  cursor: pointer;
}

.lang-toggle:hover {
  background: var(--nav-item-hover);
}

.subtitle {
  margin: 8px 0 20px;
  color: var(--page-subtitle);
  font-size: 14px;
}

.form {
  display: grid;
  gap: 14px;
}

.field {
  display: grid;
  gap: 8px;
}

.label {
  font-size: 13px;
  color: var(--muted);
}

.input {
  width: 100%;
  padding: 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  outline: none;
}

.input:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

select.input {
  background: var(--select-bg, var(--card-bg));
  color: var(--text);
}

select.input option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.btn {
  width: 100%;
  border: 0;
  padding: 12px 14px;
  border-radius: 12px;
  font-weight: 700;
  color: var(--nav-btn-text);
  background: var(--nav-btn-bg);
  cursor: pointer;
}

.btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.msg {
  margin: 4px 0 0;
  font-size: 13px;
}

.error {
  color: var(--status-red-text);
}

.success {
  color: var(--status-green-text);
}

@media (max-width: 430px) {
  .signup-page {
    padding:
      max(12px, env(safe-area-inset-top))
      16px
      max(12px, env(safe-area-inset-bottom));
    align-content: center;
  }

  .section-title{
    font-size: 25px;
  }
}
</style>

