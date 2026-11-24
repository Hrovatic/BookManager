<script setup>
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { apiUrl } from '@/utils/api'
import { fetchCenters } from '@/utils/centers'

const username = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const center = ref('') // <-- NOVO
const error = ref('')
const success = ref('')
const centers = ref([])
const { t } = useI18n()

async function loadCenters() {
  try {
    centers.value = await fetchCenters()
  } catch {
    centers.value = []
  }
}

onMounted(() => {
  loadCenters()
})

const handleSubmit = async (e) => {
  e.preventDefault()

  error.value = ''
  success.value = ''

  if (!username.value || !email.value || !password.value || !confirmPassword.value || !center.value) {
    error.value = t('register.errors.required')
    return
  }

  if (password.value !== confirmPassword.value) {
    error.value = t('register.errors.passwordMismatch')
    return
  }

  await createUser()
}

const createUser = async () => {
  try {
    const token = localStorage.getItem('token')

    const response = await fetch(apiUrl('/Users/register'), {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({
        username: username.value,
        email: email.value,
        password: password.value,
        center: Number(center.value), // <-- NOVO (pošlje "1", "2", ...)
      }),
    })

    if (response.status == 409) {
      error.value = t('register.errors.exists')
      return
    }

    if (!response.ok) {
      error.value = t('register.errors.createFailed')
      return
    }

    success.value = t('register.success.created')

    username.value = ''
    email.value = ''
    password.value = ''
    confirmPassword.value = ''
    center.value = ''
  } catch (err) {
    error.value = t('register.errors.createFailed')
  }
}
</script>

<template>
  <div class="register-page">
    <div class="register-card surface">
      <h1 class="title section-title">{{ $t('register.title') }}</h1>
      <p class="subtitle">{{ $t('register.subtitle') }}</p>

      <form class="form" @submit="handleSubmit">
        <div class="field">
          <label class="label" for="username">{{ $t('register.username') }}</label>
          <input
            id="username"
            class="input"
            type="text"
            :placeholder="$t('register.usernamePlaceholder')"
            required
            v-model="username"
            autocomplete="username"
          />
        </div>

        <div class="field">
          <label class="label" for="email">{{ $t('register.email') }}</label>
          <input
            id="email"
            class="input"
            type="email"
            :placeholder="$t('register.emailPlaceholder')"
            required
            v-model="email"
            autocomplete="email"
          />
        </div>

        <!-- NOVO: Center -->
        <div class="field">
          <label class="label" for="center">{{ $t('register.center') }}</label>
          <select id="center" class="input" v-model="center" required>
            <option value="">{{ $t('register.centerPlaceholder') }}</option>
            <option v-for="c in centers" :key="c.id" :value="c.id">
              {{ c.id }} — {{ c.name }}
            </option>
          </select>
        </div>

        <div class="field">
          <label class="label" for="password">{{ $t('register.password') }}</label>
          <input
            id="password"
            class="input"
            type="password"
            :placeholder="$t('register.passwordPlaceholder')"
            required
            v-model="password"
            autocomplete="new-password"
          />
        </div>

        <div class="field">
          <label class="label" for="confirmPassword">{{ $t('register.confirmPassword') }}</label>
          <input
            id="confirmPassword"
            class="input"
            type="password"
            :placeholder="$t('register.confirmPasswordPlaceholder')"
            required
            v-model="confirmPassword"
            autocomplete="new-password"
          />
        </div>

        <button class="btn btn--primary" type="submit">{{ $t('register.submit') }}</button>

        <p v-if="error" class="msg error">{{ error }}</p>
        <p v-if="success" class="msg success">{{ success }}</p>
      </form>
    </div>
  </div>
</template>

<style scoped>
.register-page {
  display: grid;
  place-items: center;
  background: var(--bg);
  color: var(--text);
  padding: 16px 12px 12px;
}

.register-card {
  width: 100%;
  max-width: 520px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 16px;
  padding: 24px;
  box-shadow: var(--shadow-elev-1);
}

.title {
  margin: 0;
  font-size: 24px;
  line-height: 1.2;
}

.subtitle {
  margin: 8px 0 22px;
  color: var(--page-subtitle);
  font-size: 14px;
}

.form {
  display: grid;
  gap: 12px;
}

.field {
  display: grid;
  gap: 6px;
}

.label {
  font-size: 12px;
  font-weight: 800;
  color: var(--muted);
}

.input {
  width: 100%;
  padding: 11px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  outline: none;
}

select option{
  background-color: var(--option-bg);
}

.input::placeholder {
  color: var(--muted);
}

.input:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px var(--focus-ring);
}

.legend {
  margin-top: 2px;
  font-size: 12px;
  color: var(--muted);
}

.legend-list {
  margin-top: 6px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.legend-item {
  padding: 6px 10px;
  border-radius: 999px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
}

.btn {
  margin-top: 6px;
  width: 100%;
  border: 1px solid var(--card-border);
  padding: 11px 14px;
  border-radius: 12px;
  font-weight: 800;
  color: var(--text);
  background: var(--select-bg);
  cursor: pointer;
  transition: transform 0.06s ease, opacity 0.15s ease, background 0.18s ease, border-color 0.18s ease;
}

.btn:hover{
  background-color: var(--nav-item-hover);
}

.btn:active {
  transform: translateY(1px);
}

.btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.btn--primary {
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  border-color: rgba(124, 92, 255, 0.72);
  color: var(--on-accent);
}

.btn--primary:hover {
  background: linear-gradient(180deg, var(--btn-gradient2), var(--btn-gradient1));
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
</style>

