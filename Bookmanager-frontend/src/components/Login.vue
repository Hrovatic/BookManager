<script setup>
    import { useRouter } from 'vue-router';
    import { ref } from 'vue';
    import { useI18n } from 'vue-i18n'
    import { apiUrl } from '@/utils/api'

    const router = useRouter()
    const username = ref('')
    const password = ref('')
    const errorMessage = ref('')
    const { t } = useI18n()

    const handleLogin = async () => {
        errorMessage.value = ''

        try {
            const response = await fetch(apiUrl('/Auth/login'), {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                username: username.value,
                password: password.value
            })
        })

        if (!response.ok) {
            const msg = await response.text()
            errorMessage.value = msg || t('login.errors.invalid')
            return
        }

        // backend vrne čist string JWT
        const token = await response.text()
        localStorage.setItem('token', token)

        const payload = JSON.parse(atob(token.split('.')[1]))

        const userId = payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"]

        const usernameFromToken =
          payload["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"] ||
          payload["unique_name"] ||
          payload ["sub"] ||
          null
        
        let role =
          payload["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] ||
          payload["role"] ||
          'user'

        if (Array.isArray(role)) role = role[0]
        
        const user = {
          id: userId,
          username: usernameFromToken,
          role
        }

        localStorage.setItem("userId", userId)
        localStorage.setItem('user', JSON.stringify(user))
        localStorage.setItem('role', role)

        router.push('/')
        } catch (e) {
            console.error(e)
            errorMessage.value = t('login.errors.network')
        }
    }
</script>

<template>
  <div class="login-page">
    <div class="login-card surface">

      <h1 class="title section-title">{{ $t('login.title') }}</h1>
      <p class="subtitle">{{ $t('login.subtitle') }}</p>

      <form class="form" @submit.prevent="handleLogin">

        <div class="field">
          <label class="label" for="username" >{{ $t('login.username') }}</label>
          <input
            id="username"
            class="input"
            type="text"
            autocomplete="username"
            :placeholder="$t('login.usernamePlaceholder')"
            required
            autofocus
            v-model="username"
          />
        </div>

        <div class="field">
          <label class="label" for="password">{{ $t('login.password') }}</label>
          <input
            id="password"
            class="input"
            type="password"
            autocomplete="current-password"
            :placeholder="$t('login.passwordPlaceholder')"
            required
            v-model="password"
          />
        </div>

        <button class="btn" type="submit">{{ $t('login.submit') }}</button>

        
        <p v-if="errorMessage" class="msg error">{{ errorMessage }}</p>
      </form>
    </div>
  </div>
</template>


<style>
.login-page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background: var(--bg);
  color: var(--text);
}

.login-card {
  width: 100%;
  max-width: 420px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  backdrop-filter: blur(10px);
  border-radius: 16px;
  padding: 28px;
  box-shadow: 0 18px 50px rgba(0, 0, 0, 0.35);
}

.title {
  margin: 0;
  font-size: 28px;
  line-height: 1.2;
  color: var(--text);
  letter-spacing: 0.2px;
}

.subtitle {
  margin: 8px 0 22px;
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
  padding: 12px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  outline: none;
  transition: border-color 0.15s ease, box-shadow 0.15s ease;
}

.input::placeholder {
  color: var(--muted);
}

.input:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.btn {
  margin-top: 6px;
  width: 100%;
  border: 0;
  padding: 12px 14px;
  border-radius: 12px;
  font-weight: 700;
  color: var(--nav-btn-text);
  background: var(--nav-btn-bg);
  cursor: pointer;
  transition: transform 0.06s ease, opacity 0.15s ease;
}

.btn:active {
  transform: translateY(1px);
}

.btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

.msg {
  margin: 4px 0 0;
  font-size: 13px;
}

.msg.error {
  color: var(--status-red-text);
}

.meta {
  margin-top: 6px;
  display: flex;
  justify-content: center;
  gap: 6px;
  color: var(--muted);
  font-size: 13px;
}

.meta-text {
  color: var(--muted);
}

.link {
  color: var(--text);
  text-decoration: none;
}

.link:hover {
  text-decoration: underline;
}

@media (max-width: 640px) {
  .login-page {
    min-height: 100dvh;
    padding: 0 16px;
    align-content: center;
  }
}

</style>
