<script setup>
import { ref, onMounted, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl } from '@/utils/api'

const API = apiUrl('/api/center')
const REVIEWS_API = apiUrl('/api/wishlist/price-reviews')
const { t } = useI18n()

const items = ref([])
const loading = ref(false)
const error = ref('')

const reviewItems = ref([])
const reviewLoading = ref(false)
const reviewError = ref('')
const yearFilter = ref('')
const yearSort = ref('desc')

const token = localStorage.getItem('token')
const auth = computed(() => ({
  headers: token ? { Authorization: `Bearer ${token}` } : {}
}))

async function load() {
  loading.value = true
  error.value = ''
  try {
    const res = await axios.get(`${API}/prices`, auth.value)
    items.value = Array.isArray(res.data) ? res.data : []
  } catch (e) {
    error.value = t('priceReview.errors.loadPrices')
  } finally {
    loading.value = false
  }
}

async function loadReviews() {
  reviewLoading.value = true
  reviewError.value = ''
  try {
    const res = await axios.get(REVIEWS_API, auth.value)
    reviewItems.value = Array.isArray(res.data) ? res.data : []
  } catch (e) {
    reviewError.value = t('priceReview.errors.loadHistory')
  } finally {
    reviewLoading.value = false
  }
}

async function resetPrices() {
  const ok = confirm(t('priceReview.confirm.resetPrices'))
  if (!ok) return

  try {
    await axios.post(
      `${API}/prices/reset`,
      {},
      auth.value
    )

    await load()
    alert(t('priceReview.success.pricesReset'))
  } catch (e) {
    alert(t('priceReview.errors.resetFailed'))
  }
}

async function exportXlsx() {
  try {
    const res = await axios.get(`${API}/prices/export`, {
      ...auth.value,
      responseType: 'blob'
    })

    const blob = new Blob([res.data], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
    })

    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = 'center-prices.xlsx'
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  } catch (e) {
    alert(t('priceReview.errors.exportFailed'))
  }
}

async function exportReviewsXlsx() {
  try {
    const year = String(yearFilter.value || '').trim()
    const qs = year ? `?year=${encodeURIComponent(year)}` : ''
    const res = await axios.get(`${REVIEWS_API}/export${qs}`, {
      ...auth.value,
      responseType: 'blob'
    })

    const blob = new Blob([res.data], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'
    })

    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    const suffix = year ? `-${year}` : `-${new Date().toISOString().slice(0, 10)}`
    a.download = `wishlist-price-reviews${suffix}.xlsx`
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
  } catch (e) {
    alert(t('priceReview.errors.exportFailed'))
  }
}

const total = computed(() =>
  items.value.reduce((sum, x) => sum + Number(x.price ?? x.Price ?? 0), 0)
)

const reviewYears = computed(() => {
  const set = new Set(
    (reviewItems.value ?? [])
      .map(x => Number(x.year ?? x.Year))
      .filter(Boolean)
  )
  return Array.from(set).sort((a, b) => b - a)
})

const filteredReviews = computed(() => {
  const year = yearFilter.value ? Number(yearFilter.value) : null
  const dir = yearSort.value === 'desc' ? -1 : 1
  const list = (reviewItems.value ?? []).filter(x => {
    const y = Number(x.year ?? x.Year)
    return !year || y === year
  })

  return list.slice().sort((a, b) => {
    const ay = Number(a.year ?? a.Year) || 0
    const by = Number(b.year ?? b.Year) || 0
    if (ay != by) return dir * (ay - by)

    const ad = new Date(a.reviewedAtUtc ?? a.ReviewedAtUtc ?? 0).getTime()
    const bd = new Date(b.reviewedAtUtc ?? b.ReviewedAtUtc ?? 0).getTime()
    return dir * (ad - bd)
  })
})

const reviewCount = computed(() => filteredReviews.value.length)
const reviewTotal = computed(() =>
  filteredReviews.value.reduce((sum, r) => sum + Number(r.price ?? r.Price ?? 0), 0)
)
const yearLabel = computed(() => (yearFilter.value ? String(yearFilter.value) : t('priceReview.filters.allYears')))

function reviewSource(row) {
  const sourceRaw = String(row.source ?? row.Source ?? '').trim().toLowerCase()
  if (sourceRaw === 'wishlist' || sourceRaw === 'isbn' || sourceRaw === 'manual' || sourceRaw === 'edited') {
    return sourceRaw
  }

  const wishlistRequestId = row.wishlistRequestId ?? row.WishlistRequestId
  if (wishlistRequestId) return 'wishlist'
  return 'manual'
}

function reviewSourceLabel(source) {
  if (source === 'wishlist') return t('priceReview.source.wishlist')
  if (source === 'isbn') return t('priceReview.source.isbn')
  if (source === 'manual') return t('priceReview.source.manual')
  if (source === 'edited') return t('priceReview.source.edited')
  return t('priceReview.source.manual')
}

function formatOrderNumber(value) {
  const raw = String(value ?? '').trim()
  return raw ? `#${raw}` : '-'
}

function toggleYearSort() {
  yearSort.value = yearSort.value === 'desc' ? 'asc' : 'desc'
}

function formatPrice(value) {
  if (value === null || value === undefined || value === '') return '-'
  const num = Number(value)
  if (Number.isNaN(num)) return '-'
  return `${num.toFixed(2)} ${t('priceReview.currency')}`
}

function formatDate(value) {
  if (!value) return '-'
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return '-'
  return d.toLocaleDateString()
}

onMounted(() => {
  load()
  loadReviews()
})
</script>

<template>
  <div class="pr-page">
    <div class="pr-card">
      <div class="pr-top">
        <div>
          <h1 class="pr-title section-title">{{ $t('priceReview.title') }}</h1>
          <p class="pr-sub">{{ $t('priceReview.subtitle') }}</p>
        </div>

        <div class="pr-actions">
          <button class="btn btn-with-icon" @click="load" :disabled="loading">
            <i class="pi pi-refresh" :class="{ 'pi-spin': loading }" aria-hidden="true"></i>
            {{ $t('priceReview.actions.refresh') }}
          </button>
          <button class="btn btn--primary" @click="exportXlsx">
            {{ $t('priceReview.actions.exportExcel') }}
            <i class="pi pi-download btn-arrow-icon" aria-hidden="true"></i>
          </button>
        </div>
        <button class="btn btn--danger" @click="resetPrices">{{ $t('priceReview.actions.resetPrices') }}</button>
      </div>

      <div v-if="loading" class="pr-state empty-state">{{ $t('priceReview.loading') }}</div>
      <div v-else-if="error" class="pr-state pr-state--err empty-state">{{ error }}</div>

      <div v-else class="pr-tablewrap pr-tablewrap--limit10">
        <table class="pr-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>{{ $t('priceReview.table.center') }}</th>
              <th class="num">{{ $t('priceReview.table.users') }}</th>
              <th class="num">{{ $t('priceReview.table.price') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="c in items" :key="c.id ?? c.Id" class="row-hover">
              <td>{{ c.id ?? c.Id }}</td>
              <td>{{ c.name ?? c.Name }}</td>
              <td class="num">{{ c.usersCount ?? c.UsersCount ?? 0 }}</td>
              <td class="num">{{ Number(c.price ?? c.Price ?? 0).toFixed(2) }} {{ $t('priceReview.currency') }}</td>
            </tr>
          </tbody>
          <tfoot>
            <tr>
              <td colspan="3" class="tfoot">{{ $t('priceReview.table.total') }}</td>
              <td class="num tfoot">{{ total.toFixed(2) }} {{ $t('priceReview.currency') }}</td>
            </tr>
          </tfoot>
        </table>
      </div>
    </div>

    <div class="pr-card pr-card--history">
      <div class="pr-top">
        <div>
          <h1 class="pr-title section-title">{{ $t('priceReview.history.title') }}</h1>
          <p class="pr-sub">{{ $t('priceReview.history.subtitle') }}</p>
        </div>

        <div class="pr-actions">
          <select class="pr-select" v-model="yearFilter">
            <option value="">{{ $t('priceReview.filters.allYears') }}</option>
            <option v-for="y in reviewYears" :key="y" :value="String(y)">
              {{ y }}
            </option>
          </select>
          <button class="btn" type="button" @click="toggleYearSort">
            {{ $t('priceReview.filters.yearSort', { order: yearSort === 'desc' ? $t('priceReview.filters.newest') : $t('priceReview.filters.oldest') }) }}
          </button>
          <button class="btn btn--primary" type="button" @click="exportReviewsXlsx">
            {{ $t('priceReview.actions.exportExcel') }}
            <i class="pi pi-download btn-arrow-icon" aria-hidden="true"></i>
          </button>
          <button class="btn btn-with-icon" type="button" @click="loadReviews" :disabled="reviewLoading">
            <i class="pi pi-refresh" :class="{ 'pi-spin': reviewLoading }" aria-hidden="true"></i>
            {{ $t('priceReview.actions.refresh') }}
          </button>
        </div>
      </div>

      <div class="pr-summary">
        <div class="pr-summary__item">
          <span class="pr-summary__label">{{ $t('priceReview.summary.year') }}</span>
          <span class="pr-chip">{{ yearLabel }}</span>
        </div>
        <div class="pr-summary__item">
          <span class="pr-summary__label">{{ $t('priceReview.summary.items') }}</span>
          <span class="pr-summary__value">{{ reviewCount }}</span>
        </div>
        <div class="pr-summary__item">
          <span class="pr-summary__label">{{ $t('priceReview.summary.total') }}</span>
          <span class="pr-summary__value">{{ reviewTotal.toFixed(2) }} {{ $t('priceReview.currency') }}</span>
        </div>
      </div>

      <div v-if="reviewLoading" class="pr-state empty-state">{{ $t('priceReview.loading') }}</div>
      <div v-else-if="reviewError" class="pr-state pr-state--err empty-state">{{ reviewError }}</div>

      <div v-else class="pr-tablewrap pr-tablewrap--history">
        <table class="pr-table pr-table--reviews">
          <thead class="pr-table-head">
            <tr>
              <th class="clickable year-col" :class="{ 'year-col--sorted': yearSort }" @click="toggleYearSort">{{ $t('priceReview.historyTable.year') }}</th>
              <th>{{ $t('priceReview.historyTable.title') }}</th>
              <th>{{ $t('priceReview.historyTable.order') }}</th>
              <th>{{ $t('priceReview.historyTable.author') }}</th>
              <th class="num">{{ $t('priceReview.historyTable.price') }}</th>
              <th>{{ $t('priceReview.historyTable.user') }}</th>
              <th>{{ $t('priceReview.historyTable.source') }}</th>
              <th>{{ $t('priceReview.historyTable.reviewedBy') }}</th>
              <th>{{ $t('priceReview.historyTable.recordedAt') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="r in filteredReviews" :key="r.id ?? r.Id" class="row-hover">
              <td class="year-col" :class="{ 'year-col--sorted': yearSort }">{{ r.year ?? r.Year }}</td>
              <td>{{ r.title ?? r.Title }}</td>
              <td class="order-col">{{ formatOrderNumber(r.orderNumber ?? r.OrderNumber) }}</td>
              <td>{{ r.author ?? r.Author }}</td>
              <td class="num">{{ formatPrice(r.price ?? r.Price) }}</td>
              <td>{{ r.username ?? r.Username ?? '-' }}</td>
              <td>
                <span class="source-badge" :class="`source-badge--${reviewSource(r)}`">
                  {{ reviewSourceLabel(reviewSource(r)) }}
                </span>
              </td>
              <td>{{ r.reviewedByUsername ?? r.ReviewedByUsername ?? '-' }}</td>
              <td>{{ formatDate(r.reviewedAtUtc ?? r.ReviewedAtUtc) }}</td>
            </tr>
          </tbody>
        </table>
        <div v-if="filteredReviews.length === 0" class="pr-state empty-state">{{ $t('priceReview.historyTable.empty') }}</div>
      </div>
    </div>
  </div>
</template>
<style scoped>
.pr-page{
  background: var(--bg);
  color: var(--text);
  padding: 16px 0 12px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  overflow-x: hidden;
}
.pr-card{
  max-width: 1280px;
  width: min(1280px, 100%);
  margin: 0 auto;
  border-radius: 16px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  box-shadow: var(--shadow-elev-1);
  padding: 16px;
  box-sizing: border-box;
}
.pr-top{
  display:flex;
  align-items:flex-start;
  justify-content:space-between;
  gap: 12px;
  flex-wrap: wrap;
}
.pr-title{ margin:0; font-size: 22px; font-weight: 900; }
.pr-sub{ margin:6px 0 0; color: var(--muted); font-size: 13px; }

.pr-actions{ display:flex; gap:10px; flex-wrap: wrap; }

.btn{
  padding: 10px 14px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--text);
  font-weight: 800;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}
.btn--primary{
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  border-color: rgba(124, 92, 255, 0.7);
  color: var(--on-accent);
}
.btn--primary:hover{
  background: linear-gradient(180deg, var(--btn-gradient2), var(--btn-gradient1));
}

.btn-arrow-icon{
  font-size: 14px;
  margin-left: 6px;
  vertical-align: -2px;
}

.btn-with-icon{
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.btn-with-icon .pi{
  font-size: 13px;
}

.pr-summary{
  margin-top: 10px;
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
}
.pr-summary__item{
  display: inline-flex;
  gap: 8px;
  align-items: center;
}
.pr-summary__label{
  font-size: 12px;
  color: var(--muted);
  font-weight: 700;
}
.pr-summary__value{
  font-weight: 900;
}
.pr-chip{
  padding: 4px 10px;
  border-radius: 999px;
  border: 1px solid var(--card-border);
  background: rgba(124, 92, 255, 0.15);
  color: var(--text);
  font-weight: 800;
  font-size: 12px;
}

.pr-select{
  appearance: none;
  -webkit-appearance: none;
  -moz-appearance: none;
  padding: 10px 36px 10px 14px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background-color: var(--select-bg, var(--card-bg));
  background-image:
    linear-gradient(45deg, transparent 50%, var(--muted) 50%),
    linear-gradient(135deg, var(--muted) 50%, transparent 50%);
  background-position: calc(100% - 18px) 50%, calc(100% - 12px) 50%;
  background-size: 6px 6px, 6px 6px;
  background-repeat: no-repeat;
  color: var(--text);
  font-weight: 800;
  cursor: pointer;
  transition: background-color 0.18s ease, border-color 0.18s ease, box-shadow 0.18s ease;
}

.pr-select:hover{
  border-color: rgba(124, 92, 255, 0.7);
  background-color: var(--nav-item-hover);
}

.pr-select:focus{
  outline: none;
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.pr-select option{
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.pr-state{ margin-top: 12px; padding: 12px; border-radius: 12px; background: rgba(255,255,255,0.04); }
.pr-state--err{ border: 1px solid rgba(239,68,68,0.35); }

.pr-tablewrap{
  margin-top: 12px;
  overflow: auto;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
  width: 100%;
  max-width: 100%;
  box-sizing: border-box;
  background: var(--card-bg);
}

.pr-tablewrap--limit10{
  /* Header + ~10 rows, then internal scroll */
  max-height: 500px;
  overflow-y: auto;
  overflow-x: auto;
}
.pr-table{ width:100%; border-collapse: collapse; }
.pr-table th, .pr-table td{ padding: 10px 12px; border-bottom: 1px solid rgba(255,255,255,0.08);  vertical-align: middle;}
.pr-table th{ text-align:left; color: var(--muted); font-size: 12px; letter-spacing: .2px; background: var(--table-head-bg); }
.num{ text-align:right; font-variant-numeric: tabular-nums; }
.mono{ font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace; }

/* History table: no horizontal scrollbar on laptop/desktop */
.pr-table--reviews{
  min-width: 980px;
  width: 100%;
  table-layout: auto;
}

.pr-table--reviews th,
.pr-table--reviews td{
  white-space: normal;
  overflow-wrap: anywhere;
}

.pr-table--reviews .order-col,
.pr-table--reviews th:nth-child(3){
  white-space: nowrap;
}

/* remove horizontal scrollbar only on wishlist history table */
.pr-tablewrap--history{
  max-height: 500px;
  overflow: auto !important;
  overflow-y: auto !important;
  overflow-x: auto !important;
}

.pr-tablewrap--history::-webkit-scrollbar:horizontal{
  height: 0;
  display: none;
}

.pr-tablewrap--history::-webkit-scrollbar-track{
  background: transparent;
}

.pr-tablewrap--history::-webkit-scrollbar-corner{
  background: transparent;
}

.pr-table--reviews th,
.pr-table--reviews td{
  padding: 8px 10px;
}

.pr-table--reviews thead th{
  position: sticky;
  top: 0;
  z-index: 1;
  background: var(--table-head-bg);
  opacity: 1;
}

.pr-table--reviews tbody tr:nth-child(odd){
  background: rgba(255,255,255,0.03);
}

.year-col--sorted{
  background: rgba(124, 92, 255, 0.10);
}

tfoot td{ border-bottom: 0; }
.tfoot{ font-weight: 900; }
.pr-table th:nth-child(1),
.pr-table td:nth-child(1) {
  width: 60px;
  text-align: left;
}
.pr-table th:nth-child(2),
.pr-table td:nth-child(2) {
  text-align: left;
}

/* Users */
.pr-table th:nth-child(3),
.pr-table td:nth-child(3) {
  width: 80px;
  text-align: center;
}

/* Cena */
.pr-table th:nth-child(4),
.pr-table td:nth-child(4) {
  width: 120px;
  text-align: right;
  font-variant-numeric: tabular-nums;
}

.price{
    color: #e5e7eb;
    letter-spacing: 0.2px;
    white-space: none;
}

.tfoot{
    font-weight: 900;
}

.btn--danger {
  border-color: var(--status-red-border);
  background: var(--status-red-bg);
  color: var(--status-red-text);
}

.btn--danger:hover {
  background: rgba(239,68,68,0.20);
}

.source-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 11px;
  font-weight: 800;
  border: 1px solid transparent;
}

.source-badge--wishlist {
  color: var(--status-info-text);
  background: var(--status-info-bg);
  border-color: var(--status-info-border);
}

.source-badge--isbn {
  color: var(--status-yellow-text);
  background: var(--status-yellow-bg);
  border-color: var(--status-yellow-border);
}

.source-badge--manual {
  color: var(--status-green-text);
  background: var(--status-green-bg);
  border-color: var(--status-green-border);
}

.source-badge--edited {
  color: var(--status-red-text);
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
}

.pr-card--history{
  margin-bottom: 15px;
}

@media (max-width: 960px) {
  .pr-top {
    flex-direction: column;
    align-items: flex-start;
  }

  .pr-actions {
    width: 100%;
  }

  .pr-actions .btn,
  .pr-actions .pr-select {
    width: 100%;
  }
}

@media (min-width: 1921px){
  .pr-card--history{
    max-width: 1680px;
    width: min(1680px, 100%);
  }

  .pr-table--reviews{
    min-width: 1280px;
  }
}
</style>



