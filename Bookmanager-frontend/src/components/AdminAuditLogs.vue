<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl } from '@/utils/api'
import { useRouter } from 'vue-router'


const router = useRouter()
const { t } = useI18n()
const rows = ref([])
const loading = ref(false)

const searchQuery = ref('')
const filterAction = ref('')
const page = ref(1)
const pageSize = ref(25)

/*date picker*/
const minDate = ref('')
const maxDate = ref('')
const showDateFilter = ref(false)


function authHeaders() {
  const token = localStorage.getItem('token')
  return token ? { Authorization: `Bearer ${token}` } : {}
}


onMounted(() => {
  if (localStorage.getItem('role') !== 'admin') {
    router.push('/')
    return
  }

  loadLogs()
})


async function loadLogs() {
  loading.value = true

  try {
    const res = await axios.get(
      apiUrl('/api/AuditLog/all'),
      { headers: authHeaders() }
    )

    rows.value = Array.isArray(res.data) ? res.data : []
  } catch (err) {
    console.error('Failed to load audit logs:', err)
    rows.value = []
  } finally {
    loading.value = false
  }
}

watch([searchQuery, filterAction, pageSize, minDate, maxDate], () => {
  page.value = 1
})

// filtering
const filtered = computed(() => {
  let data = rows.value

  if (searchQuery.value.trim()) {
    const q = searchQuery.value.toLowerCase()

    data = data.filter(x =>
      (x.entityName ?? '').toLowerCase().includes(q) ||
      (x.username ?? '').toLowerCase().includes(q) ||
      (x.requestPath ?? '').toLowerCase().includes(q)
    )
  }

  if (filterAction.value) {
    data = data.filter(x => x.action === filterAction.value)
  }

  if (minDate.value) {
    const min = new Date(minDate.value)

    data = data.filter(x =>
      new Date(x.createdAtUtc) >= min
    )
  }

  if (maxDate.value) {
    const max = new Date(maxDate.value)
    max.setHours(23, 59, 59, 999)

    data = data.filter(x =>
      new Date(x.createdAtUtc) <= max
    )
  }

  return data
})
const isDateInvalid = computed(() => {
  if (!minDate.value || !maxDate.value) return false
  return minDate.value > maxDate.value
})
// pagination
const totalPages = computed(() =>
  Math.max(1, Math.ceil(filtered.value.length / pageSize.value))
)

const paginated = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return filtered.value.slice(start, start + pageSize.value)
})

function nextPage() {
  if (page.value < totalPages.value) page.value++
}

function prevPage() {
  if (page.value > 1) page.value--
}

function formatDate(date) {
  if (!date) return '-'

  try {
    return new Date(date).toLocaleString('sl-SI', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    })
  } catch {
    return '-'
  }
}
</script>

<template>
  <div class="audit-wrapper">
    <div class="audit-container">

      <div class="audit-header">
        <div>
          <h1 class="section-title">{{ $t('auditLogs.title') }}</h1>
          <p class="audit-subtitle">{{ $t('auditLogs.subtitle') }}</p>
        </div>

        <button class="btn-page" @click="loadLogs">
          {{ t('auditLogs.refresh') }}
        </button>
      </div>

      <div class="audit-filters surface">
        <input v-model="searchQuery" :placeholder="t('auditLogs.searchPlaceholder')" />


        <select v-model="filterAction">
          <option value="">{{ $t('auditLogs.filters.allActions') }}</option>
          <option value="CREATE">Create</option>
          <option value="UPDATE">Update</option>
          <option value="DELETE">Delete</option>
        </select>
        <button class="btn-page" @click="showDateFilter = !showDateFilter">
          {{ $t('auditLogs.filters.DateFilter') }}
        </button>
        <div v-if="showDateFilter" class="date-filter surface">
          <div class="date-group">
            <label>{{ $t('auditLogs.filters.DateMin') }}</label>
            <input type="date" v-model="minDate" :class="{ 'date-error': isDateInvalid }" />
          </div>

          <div class="date-group">
            <label>{{ $t('auditLogs.filters.DateMax') }}</label>
            <input type="date" v-model="maxDate" :class="{ 'date-error': isDateInvalid }" />
          </div>
          <p v-if="isDateInvalid" class="date-warning">
            {{ $t('auditLogs.filters.DateWarning') }}
          </p>

          <button class="btn-page clear-btn" @click="() => { minDate = ''; maxDate = '' }">
            {{ $t('auditLogs.filters.Clear') }}
          </button>
        </div>
      </div>

      <div class="audit-table surface">
        <table>
          <thead>
            <tr>
              <th>{{ $t('auditLogs.table.time') }}</th>
              <th>{{ $t('auditLogs.table.action') }}</th>
              <th>{{ $t('auditLogs.table.entity') }}</th>
              <th>{{ $t('auditLogs.table.user') }}</th>
              <th>{{ $t('auditLogs.table.path') }}</th>
              <th>{{ $t('auditLogs.table.status') }}</th>
            </tr>
          </thead>

          <tbody v-if="!loading && paginated.length">
            <tr v-for="log in paginated" :key="log.id">
              <td>{{ formatDate(log.createdAtUtc) }}</td>
              <td>{{ log.action }}</td>
              <td>{{ log.entityName }}</td>
              <td>{{ log.username?.trim() ? log.username : '/' }}</td>
              <td>{{ log.requestPath }}</td>
              <td>{{ log.statusCode }}</td>

            </tr>
          </tbody>

          <tbody v-else>
            <tr>
              <td colspan="6">
                {{ loading ? t('auditLogs.loading') : t('auditLogs.empty') }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="audit-footer">
        <span class="controls">
          <button class="btn-page" @click="prevPage" :disabled="page === 1">
            {{ t('auditLogs.pagination.prev') }}
          </button>

          <span class="center">{{ page }} / {{ totalPages }}</span>

          <button class="btn-page" @click="nextPage" :disabled="page === totalPages">
            {{ t('auditLogs.pagination.next') }}
          </button>
          <select v-model.number="pageSize" class="btn-page">
            <option :value="25">{{ $t('auditLogs.pagination.PageSizeOption', { size: 25 }) }}</option>
            <option :value="50">{{ $t('auditLogs.pagination.PageSizeOption', { size: 50 }) }}</option>
            <option :value="100">{{ $t('auditLogs.pagination.PageSizeOption', { size: 100 }) }}</option>
          </select>
        </span>



      </div>



    </div>
  </div>
</template>

<style scoped>
.audit-wrapper {
  padding: 16px 0 12px;
}

.audit-container {
  max-width: 1280px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.audit-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.audit-subtitle {
  color: var(--muted);
  margin: 6px 0 0;
}

.audit-filters,
.audit-table {
  border-radius: 16px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  box-shadow: var(--shadow-elev-1);
}

.audit-filters {
  display: grid;
  grid-template-columns: 1fr 180px 120px;
  gap: 12px;
  padding: 14px;
}

.audit-filters input,
.audit-filters select {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--text);
  font-size: 14px;
  outline: none;
}

.audit-filters select option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.audit-filters input::placeholder {
  color: var(--muted);
}

.audit-filters input:focus,
.audit-filters select:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px var(--focus-ring);
}

.audit-table {
  overflow-x: auto;
  overflow-y: hidden;
}

.audit-table table {
  width: 100%;
  border-collapse: collapse;
  min-width: 860px;
}

.audit-table th,
.audit-table td {
  padding: 12px;
  border-bottom: 1px solid var(--card-border);
  text-align: center;
  vertical-align: middle;
}

.audit-table thead th {
  color: var(--muted);
  font-size: 12px;
  letter-spacing: 0.2px;
  background: var(--table-head-bg);
}

.controls {
  display: flex;
  justify-content: center;
  align-items: center;
  gap: 12px;
}


.btn-page {
  padding: 8px 12px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  cursor: pointer;
  font-weight: 700;
  transition: background 0.18s ease, border-color 0.18s ease, opacity 0.18s ease;
}

.btn-page option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.btn-page:hover {
  background: var(--nav-item-hover);
}

.btn-page:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.audit-table tbody tr:hover {
  background: var(--nav-item-hover);
}

.audit-table td:nth-child(5) {
  font-family: monospace;
  font-size: 12px;
}

@media (max-width: 960px) {
  .audit-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .audit-filters {
    grid-template-columns: 1fr;
  }

  .audit-footer {
    justify-content: space-between;
    flex-wrap: wrap;
    align-items: center;
  }
}

.date-filter {
  display: flex;
  flex-direction: column;
  gap: 5px;
  /* controls spacing between all children */
  padding: 10px;
  width: 50%;

}

.date-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
  /* label → input spacing */
  ;

}

.clear-btn {
  margin-top: 8px;
}

.date-group input.date-error {
  border: 1px solid #ff6b6b;
}
</style>
