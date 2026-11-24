<script setup>
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl } from '@/utils/api'

const CENTER_API = apiUrl('/api/center')
const INVENTORY_API = apiUrl('/api/inventory')

const loading = ref(false)
const saving = ref(false)
const rows = ref([])
const error = ref('')
const success = ref('')

const newCenterName = ref('')
const editCenterId = ref(null)
const editCenterName = ref('')
const { t } = useI18n()

const authHeaders = computed(() => {
  const token = localStorage.getItem('token')
  return token ? { headers: { Authorization: `Bearer ${token}` } } : { headers: {} }
})

function clearMessages() {
  error.value = ''
  success.value = ''
}

function normalizeName(value) {
  return String(value ?? '').trim().replace(/\s+/g, ' ')
}

async function loadCenters() {
  loading.value = true
  clearMessages()

  try {
    const [resCenters, resPrices, resInventory] = await Promise.allSettled([
      axios.get(CENTER_API, authHeaders.value),
      axios.get(`${CENTER_API}/prices`, authHeaders.value),
      axios.get(`${INVENTORY_API}/counts-by-center`, authHeaders.value)
    ])

    const centersData =
      resCenters.status === 'fulfilled' && Array.isArray(resCenters.value?.data)
        ? resCenters.value.data
        : null

    if (!centersData) {
      throw resCenters.status === 'rejected' ? resCenters.reason : new Error('Failed to load centers')
    }

    const pricesData =
      resPrices.status === 'fulfilled' && Array.isArray(resPrices.value?.data)
        ? resPrices.value.data
        : []

    const inventoryRows =
      resInventory.status === 'fulfilled' && Array.isArray(resInventory.value?.data)
        ? resInventory.value.data
        : []

    const pricesByCenterId = Object.fromEntries(
      pricesData
        .map((x) => [
          Number(x?.id ?? x?.Id),
          {
            name: String(x?.name ?? x?.Name ?? '').trim(),
            usersCount: Number(x?.usersCount ?? x?.UsersCount ?? 0),
            price: Number(x?.price ?? x?.Price ?? 0)
          }
        ])
        .filter(([id]) => Number.isInteger(id) && id > 0)
    )

    const copiesByCenterId = Object.fromEntries(
      inventoryRows
        .map((x) => [Number(x?.centerId ?? x?.CenterId), Number(x?.copyCount ?? x?.CopyCount ?? 0)])
        .filter(([id]) => Number.isInteger(id) && id > 0)
    )
    const titlesByCenterId = Object.fromEntries(
      inventoryRows
        .map((x) => [Number(x?.centerId ?? x?.CenterId), Number(x?.titleCount ?? x?.TitleCount ?? 0)])
        .filter(([id]) => Number.isInteger(id) && id > 0)
    )

    rows.value = centersData
      .map((x) => ({
        id: Number(x?.id ?? x?.Id),
        name: String(x?.name ?? x?.Name ?? '').trim(),
        usersCount: 0,
        price: 0,
        copiesCount: 0,
        titlesCount: 0
      }))
      .filter((x) => Number.isInteger(x.id) && x.id > 0 && x.name)
      .map((x) => ({
        ...x,
        name: pricesByCenterId[x.id]?.name || x.name,
        usersCount: Number(pricesByCenterId[x.id]?.usersCount ?? 0),
        price: Number(pricesByCenterId[x.id]?.price ?? 0),
        copiesCount: Number(copiesByCenterId[x.id] ?? 0),
        titlesCount: Number(titlesByCenterId[x.id] ?? 0)
      }))
      .sort((a, b) => a.id - b.id)
  } catch (e) {
    error.value = e?.response?.data || t('adminCenters.errors.loadFailed')
    rows.value = []
  } finally {
    loading.value = false
  }
}

function startEdit(row) {
  clearMessages()
  editCenterId.value = row.id
  editCenterName.value = row.name
}

function cancelEdit() {
  editCenterId.value = null
  editCenterName.value = ''
}

async function createCenter() {
  clearMessages()
  const name = normalizeName(newCenterName.value)
  if (!name) {
    error.value = t('adminCenters.errors.enterName')
    return
  }

  saving.value = true
  try {
    await axios.post(CENTER_API, { name }, authHeaders.value)
    success.value = t('adminCenters.success.created', { name })
    newCenterName.value = ''
    await loadCenters()
  } catch (e) {
    error.value = e?.response?.data || t('adminCenters.errors.createFailed')
  } finally {
    saving.value = false
  }
}

async function saveEdit() {
  if (!Number.isInteger(editCenterId.value) || editCenterId.value <= 0) return

  clearMessages()
  const name = normalizeName(editCenterName.value)
  if (!name) {
    error.value = t('adminCenters.errors.nameEmpty')
    return
  }
  const target = rows.value.find((x) => x.id === editCenterId.value)
  if (!target) {
    error.value = t('adminCenters.errors.notFound')
    return
  }

  if (name === target.name) {
    error.value = t('adminCenters.errors.nameSame')
    return
  }

  const confirmed = confirm(
    t('adminCenters.confirm.updatePrompt', {
      oldName: target.name,
      newName: name,
      users: target.usersCount,
      copies: target.copiesCount
    })
  )
  if (!confirmed) return

  saving.value = true
  try {
    await axios.put(`${CENTER_API}/${editCenterId.value}`, { name }, authHeaders.value)
    success.value = t('adminCenters.success.updated')
    cancelEdit()
    await loadCenters()
  } catch (e) {
    error.value = e?.response?.data || t('adminCenters.errors.updateFailed')
  } finally {
    saving.value = false
  }
}

async function deleteCenter(row) {
  clearMessages()
  if (!confirm(t('adminCenters.confirm.deletePrompt', { name: row.name }))) return

  saving.value = true
  try {
    await axios.delete(`${CENTER_API}/${row.id}`, authHeaders.value)
    success.value = t('adminCenters.success.deleted', { name: row.name })
    if (editCenterId.value === row.id) cancelEdit()
    await loadCenters()
  } catch (e) {
    error.value = e?.response?.data || t('adminCenters.errors.deleteFailed')
  } finally {
    saving.value = false
  }
}

onMounted(loadCenters)
</script>

<template>
  <div class="centers-page">
    <div class="centers-card surface">
      <div class="centers-head">
        <div>
          <h1 class="title section-title">{{ $t('adminCenters.title') }}</h1>
          <p class="subtitle">{{ $t('adminCenters.subtitle') }}</p>
        </div>
        <button class="btn-secondary" type="button" @click="loadCenters" :disabled="loading || saving">
          {{ $t('adminCenters.actions.refresh') }}
        </button>
      </div>

      <div class="create-row">
        <input v-model="newCenterName" class="input" type="text" :placeholder="$t('adminCenters.newCenterPlaceholder')"
          @keyup.enter="createCenter" />
        <button class="btn-primary" type="button" @click="createCenter" :disabled="saving">
          {{ $t('adminCenters.actions.addCenter') }}
        </button>
      </div>

      <p v-if="error" class="msg error">{{ error }}</p>
      <p v-if="success" class="msg success">{{ success }}</p>

      <div class="table-wrap">
        <table class="centers-table">
          <thead>
            <tr>
              <th>{{ $t('adminCenters.columns.id') }}</th>
              <th>{{ $t('adminCenters.columns.center') }}</th>
              <th>{{ $t('adminCenters.columns.users') }}</th>
              <th>{{ $t('adminCenters.columns.booksCopies') }}</th>
              <th>{{ $t('adminCenters.columns.price') }}</th>
              <th class="actions-col">{{ $t('adminCenters.columns.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="loading">
              <td colspan="6">{{ $t('adminCenters.loading') }}</td>
            </tr>
            <tr v-else-if="rows.length === 0">
              <td colspan="6">{{ $t('adminCenters.empty') }}</td>
            </tr>
            <tr v-for="row in rows" :key="row.id">
              <td>{{ row.id }}</td>
              <td>
                <template v-if="editCenterId === row.id">
                  <input v-model="editCenterName" class="input input--inline" type="text" @keyup.enter="saveEdit" />
                </template>
                <template v-else>
                  {{ row.name }}
                </template>
              </td>
              <td>{{ row.usersCount }}</td>
              <td>{{ row.copiesCount }}</td>
              <td>{{ $t('adminCenters.currency', { value: row.price.toFixed(2) }) }}</td>
              <td class="actions">
                <template v-if="editCenterId === row.id">
                  <button class="btn-primary" type="button" @click="saveEdit" :disabled="saving">{{
                    $t('adminCenters.actions.save') }}</button>
                  <button class="btn-secondary" type="button" @click="cancelEdit" :disabled="saving">{{
                    $t('adminCenters.actions.cancel') }}</button>
                </template>
                <template v-else>
                  <button class="btn-secondary" type="button" @click="startEdit(row)" :disabled="saving">{{
                    $t('adminCenters.actions.edit') }}</button>
                  <button class="btn-danger" type="button" @click="deleteCenter(row)" :disabled="saving">{{
                    $t('adminCenters.actions.delete') }}</button>
                </template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<style scoped>
.centers-page {
  padding: 16px 0 12px;
}

.centers-card {
  max-width: 1280px;
  margin: 0 auto;
  border: 1px solid var(--card-border);
  border-radius: 16px;
  background: var(--card-bg);
  padding: 18px;
  box-shadow: var(--shadow-elev-1);
}

.centers-head {
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

.create-row {
  display: flex;
  gap: 10px;
  margin-bottom: 10px;
}

.input {
  width: 100%;
  min-width: 220px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  padding: 8px 10px;
}

.input--inline {
  min-width: 180px;
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

.centers-table {
  width: 100%;
  border-collapse: collapse;
  min-width: 720px;
  background: var(--card-bg);
}

.centers-table th,
.centers-table td {
  border-bottom: 1px solid var(--card-border);
  text-align: left;
  padding: 10px 8px;
  vertical-align: middle;
}

.centers-table thead th {
  color: var(--muted);
  font-size: 12px;
  letter-spacing: 0.2px;
  background: var(--table-head-bg);
}

.centers-table tbody tr:hover {
  background: var(--nav-item-hover);
}

.actions-col {
  width: 250px;
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

.btn-primary:hover {
  background: linear-gradient(180deg, var(--btn-gradient2), var(--btn-gradient1));
}

.btn-secondary:hover {
  background: var(--nav-item-hover);
}

.btn-danger:hover {
  background: rgba(239, 68, 68, 0.2);
}

.btn-primary:disabled,
.btn-secondary:disabled,
.btn-danger:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

@media (max-width: 700px) {
  .create-row {
    flex-direction: column;
  }

  .btn-primary,
  .btn-secondary,
  .btn-danger {
    width: 100%;
  }
}
</style>
