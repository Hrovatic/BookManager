<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl } from '@/utils/api'

const API = apiUrl('/api/borrowing')

const items = ref([])
const selected = ref([])
const searchQuery = ref('')
const filterStatus = ref('')
const filterSort = ref('date_desc')
const page = ref(1)
const pageSize = ref(10)
const activeTab = ref('my')
const filterPeriod = ref('')
const dateFrom = ref('')
const dateTo = ref('')
const successMessage = ref('')

const { t } = useI18n()
const isAdmin = computed(() => localStorage.getItem('role') === 'admin')
const authHeaders = computed(() => {
  const token = localStorage.getItem('token')
  return token ? { headers: { Authorization: `Bearer ${token}` } } : {}
})

function showSuccess(msg) {
  successMessage.value = msg
  setTimeout(() => {
    successMessage.value = ''
  }, 3000)
}

async function loadData() {
  const userId = localStorage.getItem('userId')
  const token = localStorage.getItem('token')

  const url =
    isAdmin.value && activeTab.value === 'all'
      ? `${API}/all`
      : `${API}/my?userId=${userId}`

  const res = await axios.get(url, {
    headers: { Authorization: `Bearer ${token}` }
  })
  
  items.value = (Array.isArray(res.data) ? res.data : []).map((item) => ({
    id: Number(item?.id ?? item?.Id),
    bookTitle: String(item?.bookTitle ?? item?.BookTitle ?? '').trim(),
    userName: String(item?.userName ?? item?.UserName ?? '').trim(),
    borrowedAt: item?.borrowedAt ?? item?.BorrowedAt ?? null,
    dueDate: item?.dueDate ?? item?.DueDate ?? null,
    status: normalizeStatus(item?.status ?? item?.Status)
  }))
  selected.value = []
  page.value = 1
}

function normalizeStatus(status) {
  const s = String(status || '').trim().toLowerCase()

  switch (s) {
    case 'active':
      return 'borrowed'

    case 'borrowed':
      return 'borrowed'

    case 'returned':
      return 'returned'

    case 'overdue':
      return 'overdue'

    default:
      return 'borrowed'
  }
}
watch(activeTab, () => {
  loadData()
})

watch([filterStatus, filterSort, filterPeriod, dateFrom, dateTo, searchQuery, pageSize], () => {
  page.value = 1
})

watch(isAdmin, (value) => {
  if (!value) {
    activeTab.value = 'my'
  }
})

onMounted(loadData)

const periodLabel = computed(() => {
  if (filterPeriod.value === '7d') return t('borrowings.filters.last7')
  if (filterPeriod.value === '30d') return t('borrowings.filters.last30')
  if (filterPeriod.value === 'thisMonth') return t('borrowings.filters.thisMonth')
  if (filterPeriod.value === 'custom') return t('borrowings.filters.custom')
  return t('borrowings.filters.allTime')
})


const filtered = computed(() => {
  let data = [...items.value]

  if (searchQuery.value.trim()) {
    const q = searchQuery.value.toLowerCase()
    data = data.filter((x) =>
      String(x.bookTitle ?? '').toLowerCase().includes(q) ||
      String(x.userName ?? '').toLowerCase().includes(q)
    )
  }

  if (filterStatus.value) {
    data = data.filter((x) => x.status === filterStatus.value)
  }

  if (filterSort.value === 'date_desc') {
    data.sort((a, b) => new Date(b.borrowedAt) - new Date(a.borrowedAt))
  }
  if (filterSort.value === 'date_asc') {
    data.sort((a, b) => new Date(a.borrowedAt) - new Date(b.borrowedAt))
  }
  if (filterSort.value === 'name_asc') {
    data.sort((a, b) => String(a.bookTitle ?? '').localeCompare(String(b.bookTitle ?? '')))
  }
  if (filterSort.value === 'name_desc') {
    data.sort((a, b) => String(b.bookTitle ?? '').localeCompare(String(a.bookTitle ?? '')))
  }

  let from = null
  let to = null

  if (filterPeriod.value === '7d') {
    from = daysAgo(7)
    to = endOfDay(new Date())
  } else if (filterPeriod.value === '30d') {
    from = daysAgo(30)
    to = endOfDay(new Date())
  } else if (filterPeriod.value === 'thisMonth') {
    from = firstDayOfThisMonth()
    to = endOfDay(new Date())
  } else if (filterPeriod.value === 'custom') {
    from = parseDateInput(dateFrom.value, false)
    to = parseDateInput(dateTo.value, true)
  }

  if (from || to) {
    data = data.filter((x) => inRange(x.borrowedAt, from, to))
  }

  return data
})

const totalPages = computed(() =>
  Math.max(1, Math.ceil(filtered.value.length / pageSize.value))
)

const paginated = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return filtered.value.slice(start, start + pageSize.value)
})

const allSelectedOnPage = computed(() =>
  paginated.value.length > 0 && paginated.value.every((item) => selected.value.includes(item.id))
)

const selectedItems = computed(() =>
  items.value.filter((item) => selected.value.includes(item.id))
)

const canReturnSelected = computed(() =>
  selectedItems.value.length > 0 && selectedItems.value.every((item) => item.status !== 'returned')
)

const canExtendSelected = computed(() =>
  selectedItems.value.length > 0 && selectedItems.value.every((item) => item.status !== 'returned')
)

const canDeleteSelected = computed(() =>
  isAdmin.value &&
  selectedItems.value.length > 0 &&
  selectedItems.value.every(item => item.status === 'returned')
)

function nextPage() {
  if (page.value < totalPages.value) page.value++
}

function prevPage() {
  if (page.value > 1) page.value--
}

function toggleAll() {
  if (allSelectedOnPage.value) {
    const pageIds = new Set(paginated.value.map((item) => item.id))
    selected.value = selected.value.filter((id) => !pageIds.has(id))
    return
  }

  const merged = new Set(selected.value)
  paginated.value.forEach((item) => merged.add(item.id))
  selected.value = Array.from(merged)
}

async function returnSelected() {
  if (!canReturnSelected.value) return
  await axios.post(`${API}/return`, selected.value)
  await loadData()
  showSuccess(t('borrowings.success.returned'))
  selected.value = []
}

async function extendSelected() {
  if (!canExtendSelected.value) return
  await axios.post(`${API}/extend`, selected.value, authHeaders.value)
  showSuccess(t('borrowings.success.extended'))
  selected.value = []
  await loadData()
}

async function deleteSelected() {
  if (!canDeleteSelected.value) return
  await axios.post(`${API}/delete`, selected.value, authHeaders.value)
  showSuccess(t('borrowings.success.deleted'))
  selected.value = []
  await loadData()
}

const counts = computed(() => ({
  active: items.value.filter((x) => x.status === 'active').length,
  overdue: items.value.filter((x) => x.status === 'overdue').length,
  returned: items.value.filter((x) => x.status === 'returned').length,
  borrowed: items.value.filter((x) => x.status === 'borrowed').length
}))

function formatDate(date) {
  if (!date) return '-'
  const d = date instanceof Date ? date : new Date(date)
  if (isNaN(d.getTime())) return '-'
  return d.toLocaleDateString('sl-SI')
}

function startOfDay(date) {
  const value = new Date(date)
  value.setHours(0, 0, 0, 0)
  return value
}

function endOfDay(date) {
  const value = new Date(date)
  value.setHours(23, 59, 59, 999)
  return value
}

function daysAgo(days) {
  const date = new Date()
  date.setDate(date.getDate() - days)
  return startOfDay(date)
}

function firstDayOfThisMonth() {
  const date = new Date()
  return startOfDay(new Date(date.getFullYear(), date.getMonth(), 1))
}

function parseDateInput(value, end = false) {
  if (!value) return null
  const date = new Date(value)
  return end ? endOfDay(date) : startOfDay(date)
}

function inRange(date, from, to) {
  if (!date) return false
  const value = new Date(date)
  if (isNaN(value.getTime())) return false
  if (from && value < from) return false
  if (to && value > to) return false
  return true
}
</script>

<template>
  <div class="borrowings-wrapper">
    <div class="borrowings-container">
      <div class="borrowings-header">
        <div>
          <h1 class="borrowings-title section-title">{{ $t('borrowings.title') }}</h1>
          <p class="borrowings-subtitle">{{ $t('borrowings.subtitle') }}</p>
        </div>
      </div>

      <transition name="fade">
        <div v-if="successMessage" class="success-message">
          {{ successMessage }}
        </div>
      </transition>

      <div v-if="isAdmin" class="borrowings-tabs">
        <button class="borrowings-tab" :class="{ active: activeTab === 'my' }" type="button" @click="activeTab = 'my'">
          {{ $t('borrowings.tabs.my') }}
        </button>
        <button class="borrowings-tab" :class="{ active: activeTab === 'all' }" type="button" @click="activeTab = 'all'">
          {{ $t('borrowings.tabs.all') }}
        </button>
      </div>

      <div class="borrowings-filters surface">
        <input v-model="searchQuery" class="borrowings-search" type="text" :placeholder="$t('borrowings.searchPlaceholder')" />

        <select v-model="filterStatus">
          <option value="">{{ $t('borrowings.filters.allStatuses') }}</option>
          <option value="active">{{ $t('borrowings.status.active') }}</option>
          <option value="overdue">{{ $t('borrowings.status.overdue') }}</option>
          <option value="returned">{{ $t('borrowings.status.returned') }}</option>
          <option value="borrowed">{{ $t('borrowings.status.borrowed') }}</option>
        </select>

        <select v-model="filterSort">
          <option value="date_desc">{{ $t('borrowings.sort.newest') }}</option>
          <option value="date_asc">{{ $t('borrowings.sort.oldest') }}</option>
          <option value="name_asc">{{ $t('borrowings.sort.nameAsc') }}</option>
          <option value="name_desc">{{ $t('borrowings.sort.nameDesc') }}</option>
        </select>

        <select v-model="filterPeriod">
          <option value="">{{ $t('borrowings.filters.allTime') }}</option>
          <option value="7d">{{ $t('borrowings.filters.last7') }}</option>
          <option value="30d">{{ $t('borrowings.filters.last30') }}</option>
          <option value="thisMonth">{{ $t('borrowings.filters.thisMonth') }}</option>
          <option value="custom">{{ $t('borrowings.filters.custom') }}</option>
        </select>
      </div>

      <div v-if="filterPeriod === 'custom'" class="borrowings-date-range surface">
        <input v-model="dateFrom" type="date" />
        <input v-model="dateTo" type="date" />
      </div>

      <div class="borrowings-summary">
        <div class="borrowings-summary-card surface">
          <span class="borrowings-summary-label">{{ $t('borrowings.active') }}</span>
          <strong class="borrowings-summary-value">{{ counts.active }}</strong>
        </div>
        <div class="borrowings-summary-card surface">
          <span class="borrowings-summary-label">{{ $t('borrowings.overdue') }}</span>
          <strong class="borrowings-summary-value">{{ counts.overdue }}</strong>
        </div>
        <div class="borrowings-summary-card surface">
          <span class="borrowings-summary-label">{{ $t('borrowings.returned') }}</span>
          <strong class="borrowings-summary-value">{{ counts.returned }}</strong>
        </div>
        <div class="borrowings-summary-card surface">
          <span class="borrowings-summary-label">{{ $t('borrowings.borrowed') }}</span>
          <strong class="borrowings-summary-value">{{ counts.borrowed }}</strong>
        </div>
      </div>

      <div class="borrowings-meta surface">
        <span>{{ $t('borrowings.selected') }} {{ selected.length }}</span>
        <span>{{ $t('borrowings.filters.period') }}: {{ periodLabel }}</span>
        <span>{{ $t('borrowings.results', { count: filtered.length }) }}</span>
      </div>

      <div class="borrowings-table surface">
        <table>
          <thead>
            <tr>
              <th class="checkbox-col"><input type="checkbox" :checked="allSelectedOnPage" @change="toggleAll" /></th>
              <th>{{ $t('borrowings.table.book') }}</th>
              <th v-if="isAdmin && activeTab === 'all'">{{ $t('borrowings.table.user') }}</th>
              <th>{{ $t('borrowings.table.borrowedAt') }}</th>
              <th>{{ $t('borrowings.table.dueDate') }}</th>
              <th>{{ $t('borrowings.table.status') }}</th>
            </tr>
          </thead>

          <tbody v-if="paginated.length">
            <tr v-for="item in paginated" :key="item.id">
              <td class="checkbox-col"><input v-model="selected" type="checkbox" :value="item.id" /></td>
              <td><strong class="borrowings-book-title">{{ item.bookTitle }}</strong></td>
              <td v-if="isAdmin && activeTab === 'all'">{{ item.userName }}</td>
              <td>{{ formatDate(item.borrowedAt) }}</td>
              <td>{{ formatDate(item.dueDate) }}</td>
              <td>
                <span class="status-pill" :class="`status-pill--${item.status}`">
                  {{ $t('borrowings.status.' + item.status) }}
                </span>
              </td>
            </tr>
          </tbody>

          <tbody v-else>
            <tr>
              <td :colspan="isAdmin && activeTab === 'all' ? 6 : 5" class="borrowings-empty">
                {{ $t('borrowings.empty') }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="borrowings-footer">
        <div class="borrowings-actions">
          <button class="btn-action btn-action--return" type="button" @click="returnSelected" :disabled="!canReturnSelected">
            {{ $t('borrowings.actions.return') }}
          </button>
          <button v-if="isAdmin" class="btn-action btn-action--extend" type="button" @click="extendSelected" :disabled="!canExtendSelected">
          {{ $t('borrowings.actions.extend') }}
        </button>
          <button v-if="isAdmin && activeTab === 'all'" class="btn-action btn-action--delete" type="button" @click="deleteSelected" :disabled="!canDeleteSelected">
            {{ $t('borrowings.actions.delete') }}
          </button>
        </div>

        <div class="borrowings-pagination">
          <button class="btn-page" type="button" @click="prevPage" :disabled="page === 1">{{ $t('borrowings.pagination.prev') }}</button>
          <span>{{ $t('borrowings.pagination.page') }} {{ page }} / {{ totalPages }}</span>
          <button class="btn-page" type="button" @click="nextPage" :disabled="page === totalPages">{{ $t('borrowings.pagination.next') }}</button>
          <select v-model.number="pageSize" class="page-size-select">
            <option :value="10">{{ $t('borrowings.pagination.perPage', { count: 10 }) }}</option>
            <option :value="25">{{ $t('borrowings.pagination.perPage', { count: 25 }) }}</option>
            <option :value="50">{{ $t('borrowings.pagination.perPage', { count: 50 }) }}</option>
          </select>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.borrowings-wrapper {
  background: var(--bg);
  color: var(--text);
  padding-top: 16px;
  padding-bottom: max(28px, calc(env(safe-area-inset-bottom) + 18px));
}

.borrowings-container {
  max-width: 1200px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.borrowings-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: 12px;
}

.borrowings-title {
  margin: 0;
  font-size: 22px;
  font-weight: 800;
}

.borrowings-subtitle {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--muted);
}

.success-message {
  padding: 10px 14px;
  border-radius: 10px;
  border: 1px solid var(--status-green-border);
  background: var(--status-green-bg);
  color: var(--status-green-text);
  font-weight: 600;
}

.borrowings-tabs {
  display: inline-flex;
  gap: 10px;
  width: fit-content;
}

.borrowings-tab {
  padding: 8px 12px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
  cursor: pointer;
  font-weight: 700;
  opacity: 0.82;
}

.borrowings-tab.active {
  opacity: 1;
  border-color: var(--nav-item-active-border);
  background: var(--nav-item-active);
}

.borrowings-filters,
.borrowings-date-range,
.borrowings-meta,
.borrowings-table {
  border-radius: 16px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
}

.borrowings-filters {
  display: grid;
  grid-template-columns: minmax(280px, 1fr) 180px 200px 180px;
  gap: 12px;
  padding: 14px;
}

.borrowings-date-range {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 220px));
  gap: 12px;
  padding: 14px;
}

.borrowings-filters input,
.borrowings-filters select,
.borrowings-date-range input,
.page-size-select {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--text);
  font-size: 14px;
  outline: none;
}

.borrowings-filters input::placeholder {
  color: var(--muted);
}

.borrowings-filters input:focus,
.borrowings-filters select:focus,
.borrowings-date-range input:focus,
.page-size-select:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.borrowings-filters select,
.page-size-select {
  cursor: pointer;
}

.borrowings-filters select option,
.page-size-select option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.borrowings-summary {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.borrowings-summary-card {
  padding: 14px;
  border-radius: 16px;
  border: 1px solid var(--card-border);
  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: 0 14px 32px rgba(0, 0, 0, 0.24);
}

.borrowings-summary-label {
  display: block;
  font-size: 12px;
  font-weight: 700;
  color: var(--muted);
}

.borrowings-summary-value {
  display: block;
  margin-top: 6px;
  font-size: 22px;
  font-weight: 900;
  color: var(--text);
}

.borrowings-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  padding: 10px 12px;
  font-size: 13px;
  color: var(--muted);
}

.borrowings-table {
  overflow-x: auto;
}

.borrowings-table table {
  width: 100%;
  min-width: 760px;
  border-collapse: collapse;
}

.borrowings-table th,
.borrowings-table td {
  padding: 12px 14px;
  border-bottom: 1px solid rgba(255,255,255,0.08);
  text-align: left;
  vertical-align: middle;
}

.borrowings-table th {
  color: var(--muted);
  font-size: 12px;
  letter-spacing: 0.2px;
  background: var(--table-head-bg);
}

.checkbox-col {
  width: 52px;
}

.borrowings-book-title {
  display: block;
  font-size: 14px;
  font-weight: 800;
  color: var(--text);
}

.status-pill {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 800;
  border: 1px solid transparent;
}

.status-pill--active {
  color: var(--status-info-text);
  background: var(--status-info-bg);
  border-color: var(--status-info-border);
}

.status-pill--overdue {
  color: var(--status-red-text);
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
}

.status-pill--returned {
  color: var(--status-green-text);
  background: var(--status-green-bg);
  border-color: var(--status-green-border);
}

.status-pill--borrowed {
  color: var(--status-yellow-text);
  background: var(--status-yellow-bg);
  border-color: var(--status-yellow-border);
}

.borrowings-empty {
  text-align: center;
  color: var(--muted);
  padding: 22px 14px;
}

.borrowings-footer {
  display: flex;
  flex-wrap: wrap;
  gap: 14px;
  align-items: center;
  justify-content: space-between;
}

.borrowings-actions,
.borrowings-pagination {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.btn-action,
.btn-page {
  padding: 10px 14px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  cursor: pointer;
  font-weight: 800;
  transition: opacity 0.2s ease, filter 0.2s ease, background 0.2s ease;
}

.btn-action:disabled,
.btn-page:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.btn-action--return {
  background: var(--status-green-bg);
  color: var(--status-green-text);
  border-color: var(--status-green-border);
}

.btn-action--extend {
  background: var(--status-info-bg);
  color: var(--status-info-text);
  border-color: var(--status-info-border);
}

.btn-action--delete {
  background: var(--status-red-bg);
  color: var(--status-red-text);
  border-color: var(--status-red-border);
}

.btn-page {
  background: var(--card-bg);
  color: var(--text);
}

.btn-page:hover:not(:disabled) {
  background: var(--nav-item-hover);
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.5s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

@media (max-width: 980px) {
  .borrowings-filters {
    grid-template-columns: 1fr 1fr;
  }

  .borrowings-summary {
    grid-template-columns: 1fr 1fr;
  }
}

@media (max-width: 700px) {
  .borrowings-filters,
  .borrowings-date-range {
    grid-template-columns: 1fr;
  }

  .borrowings-summary {
    grid-template-columns: 1fr;
  }

  .borrowings-footer {
    align-items: stretch;
  }

  .borrowings-actions,
  .borrowings-pagination {
    width: 100%;
  }

  .btn-action,
  .btn-page,
  .page-size-select {
    width: 100%;
  }
}
</style>
