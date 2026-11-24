<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl } from '@/utils/api'

const API = apiUrl('/api/Inventory')
const COPIES_ENDPOINT = `${API}/all`
const { t } = useI18n()

const token = localStorage.getItem('token')

const loading = ref(false)
const error = ref('')
const items = ref([])
const successMessage = ref('')

// filters
const q = ref('')
const available = ref('')
const location = ref('')
const sort = ref('inv_asc')

// pagination
const page = ref(1)
const pageSize = ref(20)

watch([q, available, location, sort, pageSize], () => {
  page.value = 1
})

const totalLabel = computed(() => {
  const a = filtered.value.length
  const b = items.value.length
  return a === b
    ? t('inventory.total.all', { total: b })
    : t('inventory.total.filtered', { filtered: a, total: b })
})

const filtered = computed(() => {
  let arr = [...items.value]

  const qq = (q.value || '').trim().toLowerCase()
  if (qq) {
    arr = arr.filter(x => {
      const hay = [
        x.inventoryNumber,
        x.orderNumber,
        x.issnVolume,
        x.issnNumber,
        x.location,
        x.borrowedBy,
        x.bookTitle,
        x.bookAuthor,
        x.bookIsbn
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase()

      return hay.includes(qq)
    })
  }

  if (available.value !== '') {
    const want = available.value === 'true'
    arr = arr.filter(x => (want ? !x.borrowedBy : !!x.borrowedBy))
  }

  if (location.value.trim()) {
    const loc = location.value.trim().toLowerCase()
    arr = arr.filter(x => (x.location || '').toLowerCase().includes(loc))
  }

  // sort
  const s = sort.value
  arr.sort((a, b) => {
    const invA = (a.inventoryNumber || '').toLowerCase()
    const invB = (b.inventoryNumber || '').toLowerCase()
    const tA = (a.bookTitle || '').toLowerCase()
    const tB = (b.bookTitle || '').toLowerCase()

    if (s === 'inv_asc') return invA.localeCompare(invB)
    if (s === 'inv_desc') return invB.localeCompare(invA)
    if (s === 'title_asc') return tA.localeCompare(tB)
    if (s === 'title_desc') return tB.localeCompare(tA)
    if (s === 'avail_desc') return (b.isAvailable === true) - (a.isAvailable === true)
    if (s === 'avail_asc') return (a.isAvailable === true) - (b.isAvailable === true)
    return 0
  })

  return arr
})

const totalPages = computed(() => {
  return Math.max(1, Math.ceil(filtered.value.length / pageSize.value))
})

const paged = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return filtered.value.slice(start, start + pageSize.value)
})

watch(page, (p) => {
  if (p < 1) page.value = 1
  if (p > totalPages.value) page.value = totalPages.value
})

async function reload() {
  loading.value = true
  error.value = ''
  try {
    const res = await axios.get(COPIES_ENDPOINT, {
      headers: { Authorization: `Bearer ${token}` }
    })

    items.value = (res.data || []).map(mapCopy)
  } catch (e) {
    console.error(e)
    error.value = t('inventory.errors.loadFailed')
  } finally {
    loading.value = false
  }
}

function mapCopy(x) {
  return {
    id: x.id ?? x.Id,
    bookId: x.bookId ?? x.BookId,

    inventoryNumber: x.inventoryNumber ?? x.InventoryNumber,
    orderNumber: x.orderNumber ?? x.OrderNumber,
    issnVolume: x.issnVolume ?? x.IssnVolume,
    issnNumber: x.issnNumber ?? x.IssnNumber,
    location: x.location ?? x.Location,
    centerName: x.centerName ?? x.CenterName,
    condition: x.condition ?? x.Condition,
    borrowedBy: x.borrowedBy ?? x.BorrowedBy,
    borrowedByCenter: x.borrowedByCenter ?? x.BorrowedByCenter,
    isAvailable: x.isAvailable ?? x.IsAvailable,

    bookTitle: x.bookTitle ?? x.BookTitle,
    bookAuthor: x.bookAuthor ?? x.BookAuthor,
    bookIsbn: x.bookIsbn ?? x.BookIsbn
  }
}

function formatOrderNumber(value) {
  const raw = String(value ?? '').trim()
  return raw ? `#${raw}` : '-'
}

function normalizeLocationCode(rawValue) {
  const raw = String(rawValue ?? '').trim()
  if (!raw) return ''

  const parts = raw.split(/\s+[—-]\s+/)
  if (parts.length > 1 && /^\d+\.\d+\.\d+$/.test(parts[0])) {
    return parts[0]
  }

  return raw
}

function displayLocation(copy) {
  const normalized = normalizeLocationCode(copy?.location)
  return normalized || '-'
}

onMounted(reload)

const editOpen = ref(false)
const editing = ref(null)
const saving = ref(false)
const saveError = ref('')

const editForm = ref({
  inventoryNumber: '',
  location: '',
  condition: '',
  isAvailable: true
})

function openEdit(copy) {
  editing.value = copy
  editForm.value = {
    inventoryNumber: copy.inventoryNumber || '',
    location: copy.location || '',
    condition: copy.condition || '',
    isAvailable: !!copy.isAvailable
  }
  saveError.value = ''
  editOpen.value = true
}

function closeEdit() {
  editOpen.value = false
  editing.value = null
}

async function save() {
  if (!editing.value) return
  saving.value = true
  saveError.value = ''

  try {
    const id = editing.value.id

    const payload = {
      inventoryNumber: editForm.value.inventoryNumber?.trim() || null,
      inventoryNumberSet: editForm.value.inventoryNumber !== editing.value.inventoryNumber,

      location: editForm.value.location?.trim() || null,
      locationSet: editForm.value.location !== editing.value.location,

      condition: editForm.value.condition || null,
      conditionSet: editForm.value.condition !== editing.value.condition
    }

    const res = await axios.patch(
      `${API}/${id}`,
      payload,
      { headers: { Authorization: `Bearer ${token}` } }
    )

    const merged = { ...editing.value, ...payload, ...(res.data || {}) }
    const updated = mapCopy(merged)

    const idx = items.value.findIndex(x => x.id === id)
    if (idx >= 0) items.value[idx] = updated

    closeEdit()
  } catch (e) {
    console.error(e)
    saveError.value = t('inventory.errors.saveFailed')
  } finally {
    saving.value = false
  }
}

async function confirmDelete(copy) {
  if (!copy) return

  const label = copy.inventoryNumber
    ? `INV ${copy.inventoryNumber}`
    : `ID ${copy.id}`

  const ok = window.confirm(t('inventory.confirm.deleteCopy', { label }))
  if (!ok) return

  try {
    const res = await axios.delete(`${API}/${copy.id}`, {
      headers: { Authorization: `Bearer ${token}` }
    })

    items.value = items.value.filter(x => x.id !== copy.id)

    const deletedBook = !!res.data?.deletedBook
    successMessage.value = deletedBook
      ? t('inventory.success.copyDeletedAndBookRemoved', { label })
      : t('inventory.success.copyDeleted', { label })

    closeEdit()
    setTimeout(() => { successMessage.value = '' }, 3000)
  } catch (e) {
    console.error(e)
    const msg =
      e?.response?.data?.message ||
      (typeof e?.response?.data === 'string' ? e.response.data : '') ||
      t('inventory.errors.deleteFailed')

    saveError.value = msg
  }
}
</script>

<template>
  <div class="page">
    <header class="top">
      <div>
        <h1 class="section-title">{{ $t('inventory.title') }}</h1>
        <p class="muted">{{ $t('inventory.subtitle') }}</p>
      </div>

      <div class="actions">
        <button class="btn" @click="reload" :disabled="loading" :aria-label="$t('inventory.actions.refresh')">
          <i class="pi pi-refresh"></i>
        </button>
      </div>
    </header>

    <section class="panel surface">
      <div class="filters">
        <div class="field">
          <label>{{ $t('inventory.filters.search') }}</label>
          <input
            v-model="q"
            type="text"
            :placeholder="$t('inventory.filters.searchPlaceholder')"
          />
        </div>

        <div class="field">
          <label>{{ $t('inventory.filters.availability') }}</label>
          <select v-model="available">
            <option value="">{{ $t('inventory.filters.availabilityAll') }}</option>
            <option value="true">{{ $t('inventory.filters.availabilityOnlyAvailable') }}</option>
            <option value="false">{{ $t('inventory.filters.availabilityOnlyBorrowed') }}</option>
          </select>
        </div>

        <div class="field">
          <label>{{ $t('inventory.filters.location') }}</label>
          <input v-model="location" type="text" :placeholder="$t('inventory.filters.locationPlaceholder')" />
        </div>

        <div class="field">
          <label>{{ $t('inventory.filters.sort') }}</label>
          <select v-model="sort">
            <option value="inv_asc">{{ $t('inventory.sort.invAsc') }}</option>
            <option value="inv_desc">{{ $t('inventory.sort.invDesc') }}</option>
            <option value="title_asc">{{ $t('inventory.sort.titleAsc') }}</option>
            <option value="title_desc">{{ $t('inventory.sort.titleDesc') }}</option>
            <option value="avail_desc">{{ $t('inventory.sort.availableFirst') }}</option>
            <option value="avail_asc">{{ $t('inventory.sort.borrowedFirst') }}</option>
          </select>
        </div>
      </div>

      <div class="meta">
        <div class="chips">
          <span class="chip" v-if="available !== ''">
            {{ $t('inventory.chips.availability') }}: {{ available === 'true' ? $t('inventory.common.yes') : $t('inventory.common.no') }}
            <button class="x" @click="available = ''" :aria-label="$t('inventory.aria.clearAvailability')">
              <i class="pi pi-times" aria-hidden="true"></i>
            </button>
          </span>
          <span class="chip" v-if="location">
            {{ $t('inventory.chips.location') }}: {{ location }}
            <button class="x" @click="location = ''" :aria-label="$t('inventory.aria.clearLocation')">
              <i class="pi pi-times" aria-hidden="true"></i>
            </button>
          </span>
          <span class="chip" v-if="q">
            {{ $t('inventory.chips.search') }}: {{ q }}
            <button class="x" @click="q = ''" :aria-label="$t('inventory.aria.clearSearch')">
              <i class="pi pi-times" aria-hidden="true"></i>
            </button>
          </span>
        </div>

        <div class="right muted">
          {{ totalLabel }}
        </div>
      </div>
    </section>

    <section class="panel surface">
      <div v-if="error" class="error">
        {{ error }}
      </div>

      <div v-if="loading" class="loading">
        {{ $t('inventory.loading') }}
      </div>

      <div v-if="successMessage" class="success">{{ successMessage }}</div>

      <div class="tableWrap" v-if="!loading && paged.length">
        <table class="table">
          <thead>
            <tr>
              <th>{{ $t('inventory.table.inventoryNumber') }}</th>
              <th>{{ $t('inventory.table.book') }}</th>
              <th>ISBN</th>
              <th>{{ $t('inventory.table.volume') }}</th>
              <th>{{ $t('inventory.table.issue') }}</th>
              <th>{{ $t('inventory.table.order') }}</th>
              <th>{{ $t('inventory.table.location') }}</th>
              <th>{{ $t('inventory.table.condition') }}</th>
              <th>{{ $t('inventory.table.status') }}</th>
              <th style="width: 1%"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="c in paged" :key="c.id" class="row-hover">
              <td>
                <div class="mono">
                  {{ c.inventoryNumber || '-' }}
                </div>
                <div class="muted tiny">{{ $t('inventory.table.idPrefix') }}: {{ c.id }}</div>
              </td>

              <td class="book-cell">
                <div class="title">{{ c.bookTitle || $t('inventory.table.unknownBook') }}</div>
                <div class="muted author">{{ c.bookAuthor || $t('inventory.table.unknownAuthor') }}</div>
              </td>

              <td class="mono">{{ c.bookIsbn || '-' }}</td>
              <td>{{ c.issnVolume || '-' }}</td>
              <td>{{ c.issnNumber || '-' }}</td>
              <td class="order-cell">{{ formatOrderNumber(c.orderNumber) }}</td>
              <td>{{ displayLocation(c) }}</td>
              <td>
                <span
                  v-if="c.borrowedBy"
                  class="cursor-help underline decoration-dotted"
                  :title="t('inventory.table.borrowedCenter', { center: c.borrowedByCenter || '-' })"
                >
                  {{ $t('inventory.table.borrowedByLabel') }}: <b>{{ c.borrowedBy }}</b>
                </span>
              </td>

              <td>
                <span class="badge" :class="c.isAvailable ? 'ok' : 'warn'">
                  {{ c.isAvailable ? $t('inventory.status.available') : $t('inventory.status.borrowed') }}
                </span>
              </td>

              <td class="tdActions">
                <button class="btn small" @click="openEdit(c)">{{ $t('inventory.actions.edit') }}</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="!loading && !paged.length" class="empty empty-state">
        {{ $t('inventory.empty') }}
      </div>

      <div class="pager" v-if="!loading && filtered.length">
        <button class="btn small" @click="page--" :disabled="page <= 1" :aria-label="$t('inventory.pagination.prev')">
          <i class="pi pi-angle-left" aria-hidden="true"></i>
        </button>
        <div class="muted">
          {{ $t('inventory.pagination.page', { page, totalPages }) }}
        </div>
        <button class="btn small" @click="page++" :disabled="page >= totalPages" :aria-label="$t('inventory.pagination.next')">
          <i class="pi pi-angle-right" aria-hidden="true"></i>
        </button>

        <div class="spacer"></div>

        <select v-model.number="pageSize" class="selectSmall">
          <option :value="10">{{ $t('inventory.pagination.perPage', { count: 10 }) }}</option>
          <option :value="20">{{ $t('inventory.pagination.perPage', { count: 20 }) }}</option>
          <option :value="50">{{ $t('inventory.pagination.perPage', { count: 50 }) }}</option>
        </select>
      </div>
    </section>

    <div v-if="editOpen" class="modalBackdrop" @click.self="closeEdit">
      <div class="modal">
        <div class="modalHead">
          <div>
            <div class="modalTitle">{{ $t('inventory.modal.title') }}</div>
            <div class="muted">
              {{ editing?.bookTitle }} - {{ editing?.bookAuthor }}
            </div>
          </div>
          <button class="btn small" @click="closeEdit">{{ $t('inventory.actions.close') }}</button>
        </div>

        <div class="field">
          <label>{{ $t('inventory.modal.inventoryNumber') }}</label>
          <input v-model="editForm.inventoryNumber" :placeholder="$t('inventory.modal.inventoryNumberPlaceholder')" />
        </div>

        <div class="modalActions">
          <button
            class="btn danger"
            @click="confirmDelete(editing)"
            :disabled="saving"
          >
            {{ $t('inventory.actions.deleteCopy') }}
          </button>

          <div class="spacer"></div>

          <button class="btn primary" @click="save" :disabled="saving">
            {{ saving ? $t('inventory.actions.saving') : $t('inventory.actions.save') }}
          </button>
        </div>

        <div v-if="saveError" class="error" style="margin-top:12px;">
          {{ saveError }}
        </div>
      </div>
    </div>
  </div>
</template>
<style scoped>
.page { max-width: 1280px; margin: 0 auto; padding: 16px 0 12px; }
.top { display:flex; align-items:flex-start; justify-content:space-between; gap:12px; margin-bottom: 14px; }
h1 { margin: 0; font-size: 24px; }
.muted { color: var(--muted); }
.tiny { font-size: 12px; }
.mono { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace; }
.panel { background: var(--card-bg); border: 1px solid var(--card-border); border-radius: 16px; padding: 14px; margin-bottom: 14px; box-shadow: var(--shadow-elev-1); }
.actions { display:flex; gap:10px; }

.filters { display:grid; grid-template-columns: 1.3fr 0.7fr 0.9fr 0.7fr 0.8fr; gap: 10px; }
@media (max-width: 900px) { .filters { grid-template-columns: 1fr 1fr; } }

.field label { display:block; font-size: 12px; color: var(--text); margin-bottom: 6px; }
.field input, .field select {
  width: 100%;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  padding: 10px 10px;
  outline: none;
}

.field input::placeholder {
  color: var(--muted);
}

.field input:focus,
.field select:focus,
.selectSmall:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px var(--focus-ring);
}

.field select option,
.selectSmall option {
  background: var(--option-bg);
  color: var(--option-text);
}

.meta { display:flex; align-items:center; justify-content:space-between; gap:10px; margin-top: 10px; }
.chips { display:flex; flex-wrap:wrap; gap:8px; }
.chip {
  display:inline-flex; align-items:center; gap:8px;
  background: var(--nav-item-bg);
  border: 1px solid var(--card-border);
  padding: 6px 10px;
  border-radius: 999px;
  font-size: 13px;
}
.x {
  border: none; background: transparent; color: var(--muted);
  cursor: pointer; line-height: 1; display: inline-flex; align-items: center; justify-content: center;
}
.x .pi {
  font-size: 12px;
}

.tableWrap { overflow:auto; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { text-align:left; padding: 12px; border-bottom: 1px solid var(--card-border); vertical-align: top; }
.table thead th {
  position: sticky;
  top: 0;
  z-index: 1;
  color: var(--muted);
  font-size: 12px;
  letter-spacing: 0.2px;
  background: var(--table-head-bg);
}
.order-cell { white-space: nowrap; }
.title { font-weight: 700; color: var(--text);}

.book-cell {
  max-width: 420px;
}

.title,
.author {
  display: block;
  max-width: 100%;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
}

.badge {
  display:inline-block; padding: 6px 10px; border-radius: 999px;
  border: 1px solid transparent;
  font-size: 13px;
  white-space: nowrap;
}
.badge.ok {
  background: var(--status-green-bg);
  border-color: var(--status-green-border);
  color: var(--status-green-text);
}
.badge.warn {
  background: var(--status-yellow-bg);
  border-color: var(--status-yellow-border);
  color: var(--status-yellow-text);
}

.btn {
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  padding: 10px 12px;
  border-radius: 12px;
  cursor: pointer;
  font-weight: 700;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}
.btn:hover { background: var(--nav-item-hover); }
.btn:disabled { opacity: 0.6; cursor: not-allowed; }
.btn.small { padding: 8px 10px; border-radius: 10px; }
.btn.primary { background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2)); border-color: rgba(124, 92, 255, 0.7); color: var(--on-accent); }
.btn.primary:hover { background: linear-gradient(180deg, var(--btn-gradient2), var(--btn-gradient1)); }
.btn.danger { background: var(--status-red-bg); border-color: var(--status-red-border); color: var(--status-red-text); }
.btn.danger:hover { background: rgba(239, 68, 68, 0.2); }

.tdActions { white-space: nowrap; }

.loading, .empty, .error { padding: 10px; }
.error { background: var(--status-red-bg); border: 1px solid var(--status-red-border); border-radius: 12px; color: var(--status-red-text); }

.pager { display:flex; align-items:center; gap:10px; margin-top: 12px; }
.pager .btn.small { min-width: 34px; }
.spacer { flex: 1; }
.selectSmall {
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  padding: 8px 10px;
}

.modalBackdrop{
  position: fixed; inset: 0;
  background: rgba(0,0,0,0.55);
  display:flex; align-items:center; justify-content:center;
  padding: 16px;
  z-index: 9999;
}
.modal{
  width: min(760px, 100%);
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 16px;
  padding: 14px;
  box-shadow: var(--shadow-elev-2);
}

.light .modal{
  background: var(--card-bg);
  border-color: var(--card-border);
  box-shadow: var(--shadow-elev-2);
}

.light .modalBackdrop{
  background: rgba(0,0,0,0.35);
}
.modalHead{
  display:flex; align-items:flex-start; justify-content:space-between; gap:12px;
  margin-bottom: 12px;
}
.modalTitle{ font-weight: 800; font-size: 18px; }
.grid{
  display:grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.success {
  background: var(--status-green-bg);
  border: 1px solid var(--status-green-border);
  color: var(--status-green-text);
  padding: 10px 14px;
  border-radius: 12px;
  margin-bottom: 12px;
}

.borrowCell { min-width: 180px; }


@media (max-width: 700px) {
  .top {
    flex-direction: column;
    align-items: flex-start;
  }

  .book-cell{
    max-width: 320px;
  }

  .actions {
    width: 100%;
  }

  .actions .btn {
    width: 100%;
  }

  .filters {
    grid-template-columns: 1fr;
  }

  .meta {
    flex-direction: column;
    align-items: flex-start;
  }

  .table th,
  .table td {
    padding: 10px 8px;
  }

  .table th:nth-child(3),
  .table td:nth-child(3),
  .table th:nth-child(4),
  .table td:nth-child(4),
  .table th:nth-child(5),
  .table td:nth-child(5),
  .table th:nth-child(6),
  .table td:nth-child(6),
  .table th:nth-child(7),
  .table td:nth-child(7) {
    display: none;
  }

  .grid {
    grid-template-columns: 1fr;
  }
}
.modalActions{
  display:flex; align-items:center; gap:10px;
  margin-top: 14px;
}

.modal .btn{
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
}
.modal .btn:hover { background: var(--nav-item-hover); }
.modal .btn.primary {
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  border-color: rgba(124, 92, 255, 0.65);
  color: #fff;
}
.modal .btn.danger {
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
  color: var(--status-red-text);
}

@media (min-width: 1921px) {
  .page {
    max-width: 1680px;
  }

  .book-cell {
    max-width: 620px;
  }
}
</style>




