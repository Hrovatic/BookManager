<script setup>
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl } from '@/utils/api'

const loading = ref(false)
const saving = ref(false)
const rows = ref([])
const error = ref('')
const success = ref('')

const showResetModal = ref(false)
const selectedUser = ref(null)
const newPassword = ref('')
const confirmPassword = ref('')
const { t } = useI18n()

const authHeaders = computed(() => {
  const token = localStorage.getItem('token')
  return token ? { headers: { Authorization: `Bearer ${token}` } } : { headers: {} }
})

const currentUserId = computed(() => Number(localStorage.getItem('userId') || 0))

async function loadUsers() {
  loading.value = true
  error.value = ''
  try {
    const res = await axios.get(apiUrl('/Users/all'), authHeaders.value)
    rows.value = Array.isArray(res.data) ? res.data : []
  } catch (e) {
    error.value = e?.response?.data || t('adminUsers.errors.loadFailed')
  } finally {
    loading.value = false
  }
}

function openResetModal(user) {
  selectedUser.value = user
  newPassword.value = ''
  confirmPassword.value = ''
  error.value = ''
  success.value = ''
  showResetModal.value = true
}

function closeResetModal() {
  showResetModal.value = false
  selectedUser.value = null
  newPassword.value = ''
  confirmPassword.value = ''
}

async function resetPassword() {
  if (!selectedUser.value) return
  error.value = ''
  success.value = ''

  if (!newPassword.value || !confirmPassword.value) {
    error.value = t('adminUsers.errors.bothPasswords')
    return
  }

  if (newPassword.value !== confirmPassword.value) {
    error.value = t('adminUsers.errors.passwordMismatch')
    return
  }

  if (newPassword.value.length < 8) {
    error.value = t('adminUsers.errors.passwordMin')
    return
  }

  saving.value = true
  try {
    await axios.post(
      apiUrl(`/Users/admin/${selectedUser.value.id}/password`),
      { newPassword: newPassword.value },
      authHeaders.value
    )

    success.value = t('adminUsers.success.passwordUpdated', { username: selectedUser.value.username })
    closeResetModal()
  } catch (e) {
    error.value = e?.response?.data || t('adminUsers.errors.updateFailed')
  } finally {
    saving.value = false
  }
}

async function deleteUser(user) {
  error.value = ''
  success.value = ''

  if (!confirm(t('adminUsers.confirm.delete', { username: user.username }))) return

  try {
    await axios.delete(apiUrl(`/Users/admin/${user.id}`), authHeaders.value)
    success.value = t('adminUsers.success.deleted', { username: user.username })
    await loadUsers()
  } catch (e) {
    error.value = e?.response?.data || t('adminUsers.errors.deleteFailed')
  }
}

onMounted(loadUsers)
</script>

<template>
  <div class="users-page">
    <div class="users-card surface">
      <div class="users-head">
        <div>
          <h1 class="title section-title">{{ $t('adminUsers.title') }}</h1>
          <p class="subtitle">{{ $t('adminUsers.subtitle') }}</p>
        </div>
        <button class="btn-secondary" type="button" @click="loadUsers" :disabled="loading">
          {{ $t('adminUsers.actions.refresh') }}
        </button>
      </div>

      <p v-if="error" class="msg error">{{ error }}</p>
      <p v-if="success" class="msg success">{{ success }}</p>

      <div class="table-wrap">
        <table class="users-table">
          <thead>
            <tr>
              <th>{{ $t('adminUsers.columns.id') }}</th>
              <th>{{ $t('adminUsers.columns.username') }}</th>
              <th>{{ $t('adminUsers.columns.email') }}</th>
              <th>{{ $t('adminUsers.columns.role') }}</th>
              <th>{{ $t('adminUsers.columns.center') }}</th>
              <th class="actions-col">{{ $t('adminUsers.columns.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading">
              <td colspan="6">{{ $t('adminUsers.loading') }}</td>
            </tr>
            <tr v-else-if="rows.length === 0">
              <td colspan="6">{{ $t('adminUsers.empty') }}</td>
            </tr>
            <tr v-for="u in rows" :key="u.id">
              <td>{{ u.id }}</td>
              <td>{{ u.username }}</td>
              <td>{{ u.email || '—' }}</td>
              <td>{{ u.role || $t('adminUsers.defaultUserRole') }}</td>
              <td>{{ u.centerName || '—' }}</td>
              <td class="actions">
                <button class="btn-secondary" type="button" @click="openResetModal(u)">
                  {{ $t('adminUsers.actions.changePassword') }}
                </button>
                <button
                  class="btn-danger"
                  type="button"
                  @click="deleteUser(u)"
                  :disabled="u.id === currentUserId"
                  :title="u.id === currentUserId ? $t('adminUsers.cannotDeleteSelf') : ''"
                >
                  {{ $t('adminUsers.actions.delete') }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="showResetModal" class="modal-backdrop" @click.self="closeResetModal">
      <div class="modal surface">
        <h2 class="modal-title">{{ $t('adminUsers.modal.title') }}</h2>
        <p class="modal-subtitle">
          {{ $t('adminUsers.modal.user') }}: <strong>{{ selectedUser?.username }}</strong>
        </p>

        <div class="field">
          <label for="new-password">{{ $t('adminUsers.modal.newPassword') }}</label>
          <input id="new-password" v-model="newPassword" class="input" type="password" autocomplete="new-password" />
        </div>

        <div class="field">
          <label for="confirm-password">{{ $t('adminUsers.modal.confirmPassword') }}</label>
          <input id="confirm-password" v-model="confirmPassword" class="input" type="password" autocomplete="new-password" />
        </div>

        <div class="modal-actions">
          <button class="btn-secondary" type="button" @click="closeResetModal">{{ $t('adminUsers.actions.cancel') }}</button>
          <button class="btn-primary" type="button" @click="resetPassword" :disabled="saving">
            {{ saving ? $t('adminUsers.actions.saving') : $t('adminUsers.actions.savePassword') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.users-page {
  padding: 16px 0 12px;
}

.users-card {
  max-width: 1280px;
  margin: 0 auto;
  border: 1px solid var(--card-border);
  border-radius: 16px;
  background: var(--card-bg);
  padding: 18px;
  box-shadow: var(--shadow-elev-1);
}

.users-head {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  margin-bottom: 14px;
}

.title {
  margin: 0;
}

.subtitle {
  margin: 6px 0 0;
  color: var(--page-subtitle);
}

.msg {
  margin: 8px 0;
  font-size: 14px;
}

.msg.error {
  color: var(--status-red-text);
}

.msg.success {
  color: var(--status-green-text);
}

.table-wrap {
  overflow-x: auto;
  border-radius: 14px;
  border: 1px solid var(--card-border);
}

.users-table {
  width: 100%;
  border-collapse: collapse;
  min-width: 760px;
  background: var(--card-bg);
}

.users-table th,
.users-table td {
  border-bottom: 1px solid var(--card-border);
  text-align: left;
  padding: 10px 8px;
  vertical-align: middle;
}

.users-table thead th {
  color: var(--muted);
  font-size: 12px;
  letter-spacing: 0.2px;
  background: var(--table-head-bg);
}

.users-table tbody tr:hover {
  background: var(--nav-item-hover);
}

.actions-col {
  width: 280px;
}

.actions {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.btn-primary,
.btn-secondary,
.btn-danger {
  border: 1px solid var(--card-border);
  border-radius: 10px;
  padding: 8px 12px;
  cursor: pointer;
  font-weight: 700;
  transition: background 0.18s ease, border-color 0.18s ease, opacity 0.18s ease;
}

.btn-primary {
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  color: var(--on-accent);
  border-color: rgba(124, 92, 255, 0.7);
}

.btn-secondary {
  background: var(--select-bg);
  color: var(--text);
}

.btn-danger {
  background: var(--status-red-bg);
  color: var(--status-red-text);
  border-color: var(--status-red-border);
}

.btn-primary:hover,
.btn-secondary:hover,
.btn-danger:hover {
  background: var(--nav-item-hover);
}

.btn-primary:hover {
  background: linear-gradient(180deg, var(--btn-gradient2), var(--btn-gradient1));
}

.btn-danger:hover {
  background: rgba(239, 68, 68, 0.2);
}

.btn-danger:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.modal-backdrop {
  position: fixed;
  inset: 0;
  background: var(--modal-overlay);
  display: grid;
  place-items: center;
  z-index: 200;
  padding: 16px;
}

.modal {
  width: min(480px, 100%);
  border-radius: 16px;
  border: 1px solid var(--card-border);
  background: var(--modal-bg);
  padding: 16px;
  box-shadow: var(--shadow-elev-2);
}

.modal-title {
  margin: 0;
}

.modal-subtitle {
  margin: 8px 0 14px;
  color: var(--page-subtitle);
}

.field {
  display: grid;
  gap: 6px;
  margin-bottom: 12px;
}

.input {
  width: 100%;
  border: 1px solid var(--card-border);
  border-radius: 10px;
  background: var(--select-bg);
  color: var(--text);
  padding: 10px;
}

.input:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px var(--focus-ring);
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

@media (max-width: 820px) {
  .users-head {
    flex-direction: column;
  }

  .actions {
    width: 100%;
  }

  .actions .btn-primary,
  .actions .btn-secondary,
  .actions .btn-danger {
    flex: 1 1 160px;
  }
}
</style>
