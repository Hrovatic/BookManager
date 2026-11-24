<script setup>
import { ref, reactive, onMounted, onBeforeUnmount, computed, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl } from '@/utils/api'
import { fetchCenters } from '@/utils/centers'

const API = apiUrl('/api/wishlist')
const CATEGORIES_API = apiUrl('/api/categories')
const { t } = useI18n()

const editingId = ref(null)
const successMessage = ref("")
const submitting = ref(false)
const userFilter = ref('')
const deleting = ref(null)

const selectedIds = ref(new Set()) // selected wishlist request IDs
const bulkStatus = ref(1) // default "v obdelavi" => review (enum 1)

const showPriceModal = ref(false)
const priceByCenter = reactive({}) // { [centerId]: "12.34" }
const groupedByCenter = ref([]) // [{ centerId, centerCode, items: [...] }]
const formRef = ref(null)
const modalFromSingle = ref(false)
const prevSelectedIds = ref(null)
const reviewYear = ref(new Date().getFullYear())
const lookupLoading = ref(false)
const lookupNotFound = ref(false)
const lookupSuccess = ref(false)
const showIssnModal = ref(false)
const issnVolume = ref('')
const issnNumber = ref('')
const lookupDebounceTimer = ref(null)
const lookupSuccessTimer = ref(null)
const lastLookupKey = ref('')
const centers = ref([])

// Category logic (from EditBook) per center
const mainOption = ref([
  { code: '1.00.00', name: 'Naravoslovje', level: 1 },
  { code: '2.00.00', name: 'Tehnika', level: 1 },
  { code: '4.00.00', name: 'Biotehnika', level: 1 },
  { code: '5.00.00', name: 'Družboslovje', level: 1 },
  { code: '6.00.00', name: 'Humanistika', level: 1 },
  { code: '7.00.00', name: 'Interdisciplinarne raziskave', level: 1 },
  { code: '9.00.00', name: 'Drugo', level: 1 },
  { code: '10.00.00', name: 'Serijske publikacije', level: 1 },
])
const subOptionsByCenter = reactive({})
const categoryMainByCenter = reactive({})
const categoryCodeByCenter = reactive({})
const orderNumberByCenter = reactive({})

const centerNameById = computed(() =>
  Object.fromEntries(
    centers.value.map((center) => [center.id, center.name])
  )
)

function centerLabel(centerId) {
  return centerNameById.value[Number(centerId)] ?? `Center ${centerId}`
}

function resolveRequestCenterId(r) {
  const raw =
    r?.centerId ??
    r?.CenterId ??
    r?.center_id ??
    r?.user?.centerId ??
    r?.User?.CenterId ??
    null

  const n = Number(raw)
  return Number.isInteger(n) && n > 0 ? n : null
}

function resolveRequestCenterCode(r, centerId) {
  return (
    r?.centerCode ??
    r?.CenterCode ??
    r?.center_code ??
    (centerId ? centerLabel(centerId) : null)
  )
}

const centerOptions = computed(() =>
  centers.value.slice().sort((a, b) => a.id - b.id)
)
function toggleSelected(id) {
  const s = new Set(selectedIds.value)
  if (s.has(id)) s.delete(id)
  else s.add(id)
  selectedIds.value = s
}

function clearSelection() {
  selectedIds.value = new Set()
}

const selectedCount = computed(() => selectedIds.value.size)

async function bulkDeleteSelected() {
  if (!selectedCount.value) return

  const ok = confirm(`Izbrišem ${selectedCount.value} izbranih prošenj? Tega ni možno razveljaviti.`)
  if (!ok) return

  deleting.value = true
  try {
    await axios.post(
      `${API}/bulk-delete`,
      { ids: Array.from(selectedIds.value) },
      authHeaders.value
    )

    await loadRequests()
    clearSelection()

    successMessage.value = `Izbrisano`
    setTimeout(() => (successMessage.value = ''), 2500)
  } catch (e) {
    console.error(e)
    alert(extractError(e))
  } finally {
    deleting.value = false
  }
}

const allVisibleSelected = computed(() => {
  const ids = filteredRequests.value.map(x => x.id)
  if (!ids.length) return false
  return ids.every(id => selectedIds.value.has(id))
})

function toggleSelectAllVisible() {
  const ids = filteredRequests.value.map(x => x.id)
  const s = new Set(selectedIds.value)
  if (allVisibleSelected.value) {
    ids.forEach(id => s.delete(id))
  } else {
    ids.forEach(id => s.add(id))
  }
  selectedIds.value = s
}

// auth
const token = localStorage.getItem('token')
const isAdmin = computed(() => localStorage.getItem('role') === 'admin')
const authHeaders = computed(() => ({
  headers: { Authorization: `Bearer ${token}` }
}))

async function loadCenters() {
  try {
    centers.value = await fetchCenters()
  } catch (e) {
    console.error('Centers load failed:', e)
    centers.value = []
  }
}

function initCenterMeta(centerId) {
  if (!categoryMainByCenter[centerId]) categoryMainByCenter[centerId] = '9.00.00'
  if (!categoryCodeByCenter[centerId]) categoryCodeByCenter[centerId] = '9.00.00'
  if (!subOptionsByCenter[centerId]) subOptionsByCenter[centerId] = []
  if (orderNumberByCenter[centerId] === undefined) orderNumberByCenter[centerId] = ''
}

function mainFromCode(code) {
  if (!code) return '9.00.00'
  const parts = String(code).split('.')
  return parts.length === 3 ? `${parts[0]}.00.00` : '9.00.00'
}

async function loadSubCategoriesFor(centerId, parentCode) {
  try {
    const res = await axios.get(
      `${CATEGORIES_API}?parentCode=${encodeURIComponent(parentCode)}`,
      authHeaders.value
    )

    const arr = Array.isArray(res.data) ? res.data : []
    subOptionsByCenter[centerId] = arr
      .map(x => ({
        code: x.code ?? x.Code,
        name: x.name ?? x.Name,
        level: Number(x.level ?? x.Level),
        parentCode: x.parentCode ?? x.ParentCode
      }))
      .filter(x => x.code)
  } catch (e) {
    console.error('loadSubCategories failed', e?.response?.status, e?.response?.data, e?.message)
    subOptionsByCenter[centerId] = []
  }
}

async function onCategoryMainChange(centerId) {
  const v = categoryMainByCenter[centerId]
  if (!v) return

  if (v === '9.00.00') {
    subOptionsByCenter[centerId] = []
    categoryCodeByCenter[centerId] = '9.00.00'
    return
  }

  await loadSubCategoriesFor(centerId, v)
  const ok = (subOptionsByCenter[centerId] || []).some(x => x.code === categoryCodeByCenter[centerId])
  if (!ok) categoryCodeByCenter[centerId] = ''
}

function buildLocationForCenter(centerId) {
  const main = String(categoryMainByCenter[centerId] || '9.00.00').trim()
  return main || '9.00.00'
}

// ADMIN TAB
const activeTab = ref('mine') // 'mine' | 'all'

const usersInAll = computed(() => {
  if (activeTab.value !== 'all') return []
  const set = new Set(
    (requests.value ?? [])
      .map(r => r.username)
      .filter(Boolean)
      .map(u => String(u).trim())
  )
  return Array.from(set).sort((a, b) => a.localeCompare(b))
})

const STATUS_ENUM_BY_KEY = {
  new: 0,
  review: 1,
  ordered: 2,
  added: 3,
  rejected: 4
}

// FORM STATE
const form = reactive({
  isbn: '',
  priority: 'normal',
  price: '',
  fundingSource: '',
  title: '',
  author: '',
  language: '',
  note: '',
  notifyMe: false,
  centerId: ''
})

const normalizeIdentifier = (value) =>
  String(value ?? '').trim().replace(/[-\s]/g, '').toUpperCase()

const toCanonicalIssn = (normalized) =>
  normalized.length === 8 ? `${normalized.slice(0, 4)}-${normalized.slice(4)}` : normalized

const normalizedIdentifier = computed(() => normalizeIdentifier(form.isbn))
const isIssnIdentifier = computed(() => /^\d{7}[\dX]$/.test(normalizedIdentifier.value))
const isIsbn10Identifier = computed(() => /^\d{9}[\dX]$/.test(normalizedIdentifier.value))
const isIsbn13Identifier = computed(() => /^\d{13}$/.test(normalizedIdentifier.value))
const isIsbnIdentifier = computed(() => isIsbn10Identifier.value || isIsbn13Identifier.value)

function clearForm() {
  editingId.value = null
  form.isbn = ''
  form.priority = 'normal'
  form.price = ''
  form.fundingSource = ''
  form.title = ''
  form.author = ''
  form.language = ''
  form.note = ''
  form.notifyMe = false
  form.centerId = ''
  issnVolume.value = ''
  issnNumber.value = ''
  showIssnModal.value = false
  lastLookupKey.value = ''
  lookupNotFound.value = false
  lookupSuccess.value = false
  if (lookupDebounceTimer.value) {
    clearTimeout(lookupDebounceTimer.value)
    lookupDebounceTimer.value = null
  }
  if (lookupSuccessTimer.value) {
    clearTimeout(lookupSuccessTimer.value)
    lookupSuccessTimer.value = null
  }
}

function groupSelectedByCenter() {
  const selected = (requests.value ?? []).filter(r => selectedIds.value.has(r.id))
  const map = new Map()

  for (const r of selected) {
    const centerId = resolveRequestCenterId(r)
    if (!centerId) continue

    const centerCode = resolveRequestCenterCode(r, centerId)

    if (!map.has(centerId)) map.set(centerId, { centerId, centerCode, items: [] })
    map.get(centerId).items.push(r)
  }

  groupedByCenter.value = Array.from(map.values()).sort((a, b) => String(a.centerCode).localeCompare(String(b.centerCode)))

  // init price fields
  for (const g of groupedByCenter.value) {
    priceByCenter[g.centerId] = ''
    initCenterMeta(g.centerId)
  }

  reviewYear.value = new Date().getFullYear()
}

async function applyBulkStatus() {
  if (!selectedCount.value) return

  // če admin nastavi "added", najprej odpremo modal
  if (Number(bulkStatus.value) === 3) {
    modalFromSingle.value = false
    prevSelectedIds.value = null
    groupSelectedByCenter()
    showPriceModal.value = true
    return
  }

  // sicer samo bulk status update
  try {
    await axios.post(
      `${API}/bulk-status`,
      {
        ids: Array.from(selectedIds.value),
        status: Number(bulkStatus.value)
      },
      authHeaders.value
    )
    await loadRequests()
    clearSelection()
    successMessage.value = "Status je bil uspešno posodobljen."
    setTimeout(() => (successMessage.value = ""), 2500)
  } catch (err) {
    console.error(err)
    alert(extractError(err))
  }
}

async function confirmAddedWithPrices() {
  const year = Number(reviewYear.value)
  if (!year || year < 1900) {
    alert('Vnesi leto.')
    return
  }

  try {
    // 1) added + price review + delete in one backend call (per center)
    for (const g of groupedByCenter.value) {
      const groupIds = g.items.map(x => x.id)
      const main = categoryMainByCenter[g.centerId]
      const code = main === '9.00.00'
        ? '9.00.00'
        : (categoryCodeByCenter[g.centerId] || main || '9.00.00')

      await axios.post(
        `${API}/added-with-prices`,
        {
          wishlistIds: groupIds,
          year,
          centerId: g.centerId,
          price: priceByCenter[g.centerId] ? Number(priceByCenter[g.centerId]) : null,
          categoryCode: code,
          location: buildLocationForCenter(g.centerId),
          orderNumber: (orderNumberByCenter[g.centerId] || '').trim() || null
        },
        authHeaders.value
      )
    }

    const payload = groupedByCenter.value.map(g => ({
      centerId: g.centerId,
      price: priceByCenter[g.centerId] ? Number(priceByCenter[g.centerId]) : null
    }))

    await axios.post(apiUrl('/api/Center/bulk-price'), { items: payload }, authHeaders.value)

    showPriceModal.value = false
    modalFromSingle.value = false
    prevSelectedIds.value = null
    await loadRequests()
    clearSelection()

    successMessage.value = "Predlogi označeni kot dodani, cene shranjene."
    setTimeout(() => (successMessage.value = ""), 3000)
  } catch (err) {
    console.error(err)
    alert(extractError(err))
  }
}

function closePriceModal() {
  showPriceModal.value = false
  if (modalFromSingle.value) {
    selectedIds.value = prevSelectedIds.value ?? new Set()
    modalFromSingle.value = false
    prevSelectedIds.value = null
  }
}

function statusKey(s) {
  const v = Number(s)
  if (v === 1) return 'review'
  if (v === 2) return 'ordered'
  if (v === 3) return 'added'
  if (v === 4) return 'rejected'
  return 'new'
}

function priorityKey(p) {
  const v = String(p ?? '').toLowerCase()
  if (v === 'medium') return 'normal'
  if (v === 'low' || v === 'normal' || v === 'high') return v
  return 'normal'
}


function statusEnum(v) {
  if (typeof v === 'number') return v
  const s = String(v ?? '').toLowerCase()
  return STATUS_ENUM_BY_KEY[s] ?? 0
}

function openIssnModal() {
  if (isIssnIdentifier.value) showIssnModal.value = true
}

function closeIssnModal() {
  showIssnModal.value = false
}

function onIdentifierCommit() {
  if (!isIssnIdentifier.value && !isIsbnIdentifier.value) return
  if (isIssnIdentifier.value) openIssnModal()
  lookupByIdentifier()
}

async function lookupByIdentifier() {
  const normalized = normalizedIdentifier.value
  if (!normalized || (!isIsbnIdentifier.value && !isIssnIdentifier.value)) return
  if (lookupLoading.value) return

  lookupNotFound.value = false
  lookupSuccess.value = false
  lookupLoading.value = true
  try {
    const endpoint = isIssnIdentifier.value
      ? `${API}/by-issn/${encodeURIComponent(toCanonicalIssn(normalized))}`
      : `${API}/by-isbn/${encodeURIComponent(normalized)}`

    const res = await axios.get(endpoint, authHeaders.value)
    const data = res?.data ?? {}

    if (!String(form.title ?? '').trim()) form.title = data.title ?? ''
    if (!String(form.author ?? '').trim()) form.author = data.author ?? ''
    if (!String(form.language ?? '').trim()) form.language = data.language ?? ''

    lookupSuccess.value = true
    if (lookupSuccessTimer.value) clearTimeout(lookupSuccessTimer.value)
    lookupSuccessTimer.value = setTimeout(() => {
      lookupSuccess.value = false
      lookupSuccessTimer.value = null
    }, 1800)
  } catch (err) {
    const statusCode = err?.response?.status
    if (statusCode === 404) {
      lookupNotFound.value = true
      lookupSuccess.value = false
      return
    }
    lookupSuccess.value = false
    console.error('wishlist lookup failed:', err)
  } finally {
    lookupLoading.value = false
  }
}

function getLookupKey() {
  const normalized = normalizedIdentifier.value
  // Auto lookup only for ISBN-13 to avoid accidental early search while typing ISBN-13 on mobile.
  if (!normalized || !isIsbn13Identifier.value) return ''
  return `isbn:${normalized}`
}

function queueIdentifierLookup() {
  if (lookupDebounceTimer.value) {
    clearTimeout(lookupDebounceTimer.value)
    lookupDebounceTimer.value = null
  }

  const key = getLookupKey()
  if (!key || key === lastLookupKey.value) return

  lookupDebounceTimer.value = setTimeout(async () => {
    lookupDebounceTimer.value = null

    // user may have changed input while waiting
    if (getLookupKey() !== key) return

    lastLookupKey.value = key
    await lookupByIdentifier()
  }, 350)
}

watch(
  () => normalizedIdentifier.value,
  () => {
    lookupNotFound.value = false
    lookupSuccess.value = false
    queueIdentifierLookup()

    if (!isIssnIdentifier.value) {
      closeIssnModal()
      issnVolume.value = ''
      issnNumber.value = ''
    }
  }
)

function normalizeGenresToString(genres) {
  if (!genres) return ''
  if (Array.isArray(genres)) return genres.join(', ')
  // če pride iz backenda kot "a, b, c"
  return String(genres)
}

function genresStringToArray(s) {
  if (!s) return []
  return String(s)
    .split(',')
    .map(g => g.trim())
    .filter(Boolean)
}

// SUBMIT FORM
async function submitForm(e) {
  e.preventDefault()
  if (submitting.value) return

  if (!form.title || !form.author) {
    alert('Naslov in avtor sta obvezna.')
    return
  }

  const normalized = normalizedIdentifier.value
  const issnValue = isIssnIdentifier.value ? toCanonicalIssn(normalized) : null
  const isbnValue = !issnValue && isIsbnIdentifier.value ? normalized : null

  const payload = {
    isbn: isbnValue,
    issn: issnValue,
    issnVolume: issnValue ? (issnVolume.value?.trim() || null) : null,
    issnNumber: issnValue ? (issnNumber.value?.trim() || null) : null,
    priority: form.priority,
    price: form.price ? Number(form.price) : null,
    fundingSource: form.fundingSource || null,
    title: form.title,
    author: form.author,
    language: form.language || null,
    note: form.note || null,
    notifyMe: form.notifyMe,
    centerId: form.centerId ? Number(form.centerId) : null
  }

  if (isAdmin.value && !payload.centerId) {
    alert('Izberi center za zahtevek.')
    return
  }

  submitting.value = true
  try {
    if (editingId.value) {
      await axios.put(`${API}/${editingId.value}`, payload, authHeaders.value)
      successMessage.value = t('wishlist.success.updated')
    } else {
      await axios.post(`${API}/add`, payload, authHeaders.value)
      successMessage.value = t('wishlist.success.created')
    }

    await loadRequests()
    clearForm()

    setTimeout(() => (successMessage.value = ""), 3000)
  } catch (err) {
    console.error(err)
    alert(extractError(err))
  } finally {
    submitting.value = false
  }
}

// LOAD REQUESTS
const requests = ref([])
const loadingRequests = ref(false)

async function loadRequests() {
  loadingRequests.value = true
  try {
    // mine: user+admin, all: admin-only
    const url = activeTab.value === 'all' ? `${API}/all` : `${API}/mine`
    const res = await axios.get(url, authHeaders.value)
    
    const data = res.data
    console.log(res.data)
    const raw = Array.isArray(data)
      ? data
      : (data?.$values ?? data?.items ?? data?.data ?? [])

    requests.value = raw.map(r => {
      const centerId = resolveRequestCenterId(r)
      return {
        ...r,
        centerId,
        centerCode: resolveRequestCenterCode(r, centerId)
      }
    })

    console.log('wishlist data:', requests.value)
  } catch (err) {
    console.error(err)
  } finally {
    loadingRequests.value = false
  }
}

onMounted(async () => {
  await loadCenters()
  if (!isAdmin.value) activeTab.value = 'mine'
  await loadRequests()
})

onBeforeUnmount(() => {
  if (lookupDebounceTimer.value) {
    clearTimeout(lookupDebounceTimer.value)
    lookupDebounceTimer.value = null
  }
  if (lookupSuccessTimer.value) {
    clearTimeout(lookupSuccessTimer.value)
    lookupSuccessTimer.value = null
  }
})

watch(activeTab, () => {
  if (!isAdmin.value) activeTab.value = 'mine'
  if (activeTab.value !== 'all') userFilter.value = ''
  loadRequests()
})

// FILTERS + SEARCH
const search = ref('')
const statusFilter = ref('')

const filteredRequests = computed(() => {
  const s = search.value.toLowerCase()

  return (requests.value ?? []).filter(r => {
    const title = String(r.title ?? r.Title ?? r.bookTitle ?? '').toLowerCase()
    const author = String(r.author ?? r.Author ?? '').toLowerCase()
    const isbn = String(r.isbn ?? r.ISBN ?? '')

    const matchesSearch =
      title.includes(s) ||
      author.includes(s) ||
      (isbn && isbn.includes(search.value))

    const matchesStatus =
      !statusFilter.value || (r.status ?? r.Status) === statusFilter.value

    const matchesUser =
      activeTab.value !== 'all' ||
      !userFilter.value ||
      String(r.username ?? '').toLowerCase() === userFilter.value.toLowerCase()

    return matchesSearch && matchesStatus && matchesUser
  })
})


// DELETE REQUEST
async function deleteRequest(id) {
  if (!confirm('Ali res želiš izbrisati predlog?')) return

  try {
    await axios.delete(`${API}/delete/${id}`, authHeaders.value)
    await loadRequests()
  } catch (err) {
    console.error(err)
    alert('Napaka pri brisanju.')
  }
}

function extractError(err) {
  const data = err?.response?.data

  if (!data) return err?.message || 'Napaka'
  if (typeof data === 'string') return data

  // ASP.NET validation problem details
  if (data.title && data.status) {
    const details = data.detail ? `\n${data.detail}` : ''
    const errors = data.errors
      ? '\n' + Object.entries(data.errors).map(([k, v]) => `${k}: ${v.join(', ')}`).join('\n')
      : ''
    return `${data.title}${details}${errors}`
  }

  if (data.message) return data.message

  return JSON.stringify(data, null, 2)
}

async function updateStatus(item, event) {
  const newStatusEnum = Number(event?.target?.value)
  if (newStatusEnum === 3) {
    if (event?.target) {
      event.target.value = statusEnum(item.status)
    }
    prevSelectedIds.value = new Set(selectedIds.value)
    modalFromSingle.value = true
    selectedIds.value = new Set([item.id])
    groupSelectedByCenter()
    showPriceModal.value = true
    return
  }

  try {
    await axios.post(
      `${API}/status/${item.id}`,
      { status: newStatusEnum },
      authHeaders.value
    )
    await loadRequests()
  } catch (err) {
    console.error(err)
    alert(extractError(err))
  }
}

// EDIT ITEM
function editItem(item) {
  editingId.value = item.id

  form.isbn = item.isbn ?? item.ISBN ?? item.issn ?? item.ISSN ?? ''
  form.title = item.title ?? ''
  form.author = item.author ?? ''
  form.language = item.language ?? ''
  form.note = item.note ?? ''
  form.priority = item.priority ?? 'normal'
  form.price = item.price ?? ''
  form.fundingSource = item.fundingSource ?? item.FundingSource ?? ''
  form.notifyMe = !!item.notifyMe
  form.centerId = String(item.centerId ?? item.CenterId ?? '')
  issnVolume.value = String(item.issnVolume ?? item.IssnVolume ?? '')
  issnNumber.value = String(item.issnNumber ?? item.IssnNumber ?? '')

  nextTick(() => {
    if (formRef.value?.scrollIntoView) {
      formRef.value.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }

    const firstField = formRef.value?.querySelector?.('input, textarea, select')
    if (firstField?.focus) firstField.focus()
  })
}
</script>

<template>
  <div class="wishlist-wrapper">
    <div class="wishlist-container">

      <!-- Header -->
      <div class="wishlist-header">
        <div>
          <h2 class="wishlist-title section-title">{{ $t('wishlist.title') }}</h2>
          <p class="wishlist-subtitle">{{ $t('wishlist.subtitle') }}</p>
        </div>

        <div class="wishlist-header-actions">
          <button v-if="isAdmin" class="btn" type="button" :class="{ primary: activeTab === 'mine' }" @click="activeTab='mine'">
            {{ $t('wishlist.tabs.mine') }}
          </button>
          <button v-if="isAdmin" class="btn" type="button" :class="{ primary: activeTab === 'all' }" @click="activeTab='all'">
            {{ $t('wishlist.tabs.allAdmin') }}
          </button>
        </div>
      </div>

      <transition name="fade">
        <div v-if="successMessage" class="success-message toast">
          {{ successMessage }}
        </div>
      </transition>

      <!-- Create request -->
      <section class="wishlist-card surface">
        <h3 class="card-title">
          {{ editingId ? $t('wishlist.form.editTitle') ?? 'Uredi predlog' : $t('wishlist.form.title') }}
        </h3>

        <form ref="formRef" class="request-form" @submit="submitForm">
          <div class="form-row three">
            <div class="form-group">
              <label class="label-with-help">
                {{ $t('wishlist.form.isbn') }}
                <span
                  class="label-help"
                  :title="$t('wishlist.form.isbnAutoSearchHint') ?? 'Auto-iskanje se sproži pri ISBN-13; za ISBN-10 in ISSN uporabi Enter ali zapusti polje.'"
                  :aria-label="$t('wishlist.form.isbnAutoSearchHint') ?? 'Auto-iskanje se sproži pri ISBN-13; za ISBN-10 in ISSN uporabi Enter ali zapusti polje.'"
                  tabindex="0"
                >
                  <i class="pi pi-info-circle" aria-hidden="true"></i>
                  <span class="label-help__tooltip" role="tooltip">
                    {{ $t('wishlist.form.isbnAutoSearchHint') ?? 'Auto-iskanje se sproži pri ISBN-13; za ISBN-10 in ISSN uporabi Enter ali zapusti polje.' }}
                  </span>
                </span>
              </label>
              <input
                type="text"
                :placeholder="$t('wishlist.form.isbnPlaceholder')"
                v-model="form.isbn"
                @blur="onIdentifierCommit"
                @keydown.enter.prevent="onIdentifierCommit"
              />
              <div v-if="lookupLoading" class="lookup-meta lookup-meta--pending">
                {{ $t('wishlist.form.lookupSearching') ?? 'Iščem podatke...' }}
              </div>
              <div v-else-if="lookupSuccess" class="lookup-meta lookup-meta--ok">
                {{ $t('wishlist.form.lookupSuccess') ?? 'Knjiga je najdena.' }}
              </div>
              <div v-else-if="lookupNotFound" class="lookup-meta lookup-meta--warn">
                {{ $t('wishlist.form.lookupNotFound') ?? 'Knjiga ni najdena. Podatke vnesi ročno.' }}
              </div>
            </div>

            <div class="form-group">
              <label>{{ $t('wishlist.form.priority') }}</label>
              <select v-model="form.priority">
                <option value="low">{{ $t('wishlist.priority.low') }}</option>
                <option value="normal">{{ $t('wishlist.priority.normal') }}</option>
                <option value="high">{{ $t('wishlist.priority.high') }}</option>
              </select>
            </div>

            <div class="form-group">
              <label>{{ $t('wishlist.form.price') }}</label>
              <input type="number" step="0.01" min="0" :placeholder="$t('wishlist.form.pricePlaceholder')" v-model="form.price" />
            </div>

            <div class="form-group">
              <label>{{ $t('wishlist.form.funding') }}</label>
              <input type="text" :placeholder="$t('wishlist.form.fundingPlaceholder')" v-model="form.fundingSource"/>
            </div>
          </div>

          <div v-if="isIssnIdentifier" class="issn-meta issn-meta--full">
            <div class="issn-meta__info">
              <span class="meta">{{ $t('wishlist.form.issnMeta') }}</span>
              <strong>
                {{ $t('wishlist.form.issnVolumeLabel') }}: {{ issnVolume || '—' }}
                ·
                {{ $t('wishlist.form.issnNumberLabel') }}: {{ issnNumber || '—' }}
              </strong>
            </div>
            <button type="button" class="issn-meta__btn" @click="openIssnModal">
              {{ $t('wishlist.form.issnEdit') }}
            </button>
          </div>

          <div v-if="isAdmin" class="form-group">
            <label>{{ $t('wishlist.form.center') }}</label>
            <select v-model="form.centerId">
              <option value="">{{$t('wishlist.form.centerPlaceholder')}}</option>
              <option v-for="c in centerOptions" :key="c.id" :value="String(c.id)">
                {{ c.id }} — {{ c.name }}
              </option>
            </select>
          </div>

          <div class="form-group">
            <label>{{ $t('wishlist.form.bookTitle') }}</label>
            <input type="text" :placeholder="$t('wishlist.form.bookTitlePlaceholder')" v-model="form.title" />
          </div>

          <div class="form-group">
            <label>{{ $t('wishlist.form.author') }}</label>
            <input type="text" :placeholder="$t('wishlist.form.authorPlaceholder')" v-model="form.author"/>
          </div>

          <div class="form-row">
            <div class="form-group">
              <label>{{ $t('wishlist.form.language') }}</label>
              <input type="text" :placeholder="$t('wishlist.form.languagePlaceholder')" v-model="form.language"/>
            </div>
          </div>

          <div class="form-group">
            <label>{{ $t('wishlist.form.note') }}</label>
            <textarea rows="3" :placeholder="$t('wishlist.form.notePlaceholder')" v-model="form.note"></textarea>
          </div>

          <div class="form-group">
            <label>
              <input type="checkbox" v-model="form.notifyMe" />
              {{ $t('wishlist.form.notify') }}
            </label>
          </div>

          <div class="form-actions">
            <button class="btn primary" type="submit" :disabled="submitting">
              {{ submitting
                ? ($t('wishlist.form.submitting') ?? 'Submitting...')
                : (editingId ? ($t('wishlist.form.save') ?? 'Shrani') : $t('wishlist.form.submit')) }}
            </button>
            <button class="btn" type="button" @click="clearForm">
              {{ editingId ? ($t('wishlist.form.cancel') ?? 'Prekliči') : $t('wishlist.form.clear') }}
            </button>
          </div>
        </form>
      </section>

      <!-- Search -->
      <section class="wishlist-controls surface" :class="{ three: isAdmin && activeTab === 'all' }">
        <input
          class="wishlist-search"
          type="text"
          :placeholder="$t('wishlist.search')"
          v-model="search"
        />
      </section>

      <!-- Bulk bar-->
      <section v-if="isAdmin && activeTab === 'all'" class="wishlist-card surface bulkbar">
        <div class="bulkbar__left">
          <label class="bulkbar__check" title="Označi vse (filtrirano)">
            <input type="checkbox" :checked="allVisibleSelected" @change="toggleSelectAllVisible" />
            <span class="bulkbar__checktext">Označi vse</span>
          </label>

          <span class="bulkbar__count">Označeno: <strong>{{ selectedCount }}</strong></span>
        </div>


          <div class="bulkbar__right">
            <select class="wishlist-filter bulkbar__select" v-model="bulkStatus" :disabled="selectedCount === 0">
              <option :value="1">{{ $t('wishlist.status.review') }}</option>
              <option :value="2">{{ $t('wishlist.status.ordered') }}</option>
              <option :value="3">{{ $t('wishlist.status.added') }}</option>
              <option :value="4">{{ $t('wishlist.status.rejected') }}</option>
            </select>

            <button class="btn primary bulkbar__btn" type="button" :disabled="selectedCount === 0" @click="applyBulkStatus">
              Nastavi status
            </button>

            <button class="btn bulkbar__btn" type="button" :disabled="selectedCount === 0" @click="clearSelection">
              Počisti izbor
            </button>

            <button class="btn danger bulkbar__btn" type="button" :disabled="selectedCount === 0" @click="bulkDeleteSelected">
              Izbriši izbrane
            </button>
          </div>
      </section>

      <!-- Requests list -->
      <TransitionGroup name="WishlistItemFadeStagger" tag="section" class="wishlist-list" appear>
          <div class="request-item row-hover" v-for="(item, index) in filteredRequests" :key="item.id" :style="{ transitionDelay: `${index * 50}ms` }">
            <div class="request-left">
              <div class="request-top">
                <label v-if="isAdmin && activeTab === 'all'" style="display:flex; align-items:center; gap:8px;">
                <input
                  type="checkbox"
                  :checked="selectedIds.has(item.id)"
                  @change="toggleSelected(item.id)"
                />
                <span class="meta">izberi</span>
                </label>
                <span class="badge status" :class="statusKey(item.status)">{{ $t('wishlist.status.' + statusKey(item.status)) }}</span>
                <span class="badge prio" :class="priorityKey(item.priority)">{{ $t('wishlist.priority.' + priorityKey(item.priority)) }}</span>
                  <span v-if="activeTab === 'all' && (item.username || item.Username)" class="meta">
                    👤 {{ item.username ?? item.Username }}
                  </span>
                  <span class="bd-dot">•</span>
                <span class="meta">{{ $t('wishlist.added') }} {{ new Date(item.createdAt).toLocaleDateString() }}</span>
              </div>

              <h3 class="request-title">{{ item.title }}</h3>
              <p class="request-sub">
                <span class="muted">{{ $t('wishlist.author') }}:</span> {{ item.author }}
                <span class="sep">•</span>
                <span class="muted">{{ (item.issn ?? item.ISSN) ? 'ISSN' : 'ISBN' }}:</span> {{ item.issn ?? item.ISSN ?? item.isbn ?? item.ISBN ?? '-' }}
                <template v-if="item.issn ?? item.ISSN">
                  <span class="sep">•</span>
                  <span class="muted">{{ $t('wishlist.form.issnVolumeLabel') }} / {{ $t('wishlist.form.issnNumberLabel') }}:</span>
                  {{ item.issnVolume ?? item.IssnVolume ?? '—' }} / {{ item.issnNumber ?? item.IssnNumber ?? '—' }}
                </template>
                <span class="sep">•</span>
                <span class="muted">{{ $t('wishlist.price') }}:</span> {{ item.price }} <span class="muted">€</span>
                <span v-if="item.fundingSource || item.FundingSource">
                  <span class="sep">•</span>
                  <span class="muted">{{ $t('wishlist.form.funding') }}:</span>
                  {{ item.fundingSource ?? item.FundingSource }}
                </span>
              </p>

              <p class="request-note" v-if="item.note">
                {{ $t('wishlist.note') }}: {{ item.note }}
              </p>
            </div>

            <div class="request-actions">
              <select
                v-if="isAdmin"
                class="wishlist-filter"
                :value="statusEnum(item.status)"
                @change="updateStatus(item, $event)"
              >
                <option :value="1">{{ $t('wishlist.status.review') }}</option>
                <option :value="2">{{ $t('wishlist.status.ordered') }}</option>
                <option :value="3">{{ $t('wishlist.status.added') }}</option>
                <option :value="4">{{ $t('wishlist.status.rejected') }}</option>
              </select>

              <button class="btn small" @click="editItem(item)">
                {{ $t('wishlist.edit') }}
              </button>
              <button class="btn small danger" @click="deleteRequest(item.id)">
                {{ $t('wishlist.delete') }}
              </button>
            </div>
          </div>
        </TransitionGroup>

      <!-- Empty -->
      <div class="empty-state" v-if="filteredRequests.length == 0">
        {{ $t('wishlist.empty') }}
      </div>

    </div>
    <div v-if="showPriceModal" class="modal-backdrop" @click.self="closePriceModal">
      <div class="modal modal--added surface-3">
        <div class="modal-header">
          <h3 style="margin:0;">Dodano v inventar — vnesi cene po centrih</h3>
          <button class="btn small" type="button" @click="closePriceModal">Zapri</button>
        </div>

        <div style="display:flex; gap:12px; align-items:center; flex-wrap: wrap; margin: 6px 0 12px;">
          <div style="min-width: 140px;">
            <div class="meta" style="margin-bottom:4px;">Leto</div>
            <input
              class="wishlist-filter"
              type="number"
              min="1900"
              max="2100"
              v-model.number="reviewYear"
            />
          </div>
          <div class="meta">Cena se shrani v zgodovino za to leto.</div>
        </div>

        <div v-if="groupedByCenter.length === 0" class="empty-state">
          Manjka centerId v podatkih (backend mora vrniti centerId/centerCode).
        </div>

        <div v-for="g in groupedByCenter" :key="g.centerId" class="modal-group">
          <div style="display:flex; align-items:center; justify-content:space-between; gap:12px;">
            <div style="font-weight:900;">
              {{ g.centerCode }} <span class="meta">(ID: {{ g.centerId }})</span>
            </div>

            <div style="display:flex; gap:8px; align-items:center;">
              <span class="meta">Cena (€):</span>
              <input
                class="wishlist-filter"
                style="width:140px;"
                type="number"
                step="0.01"
                min="0"
                v-model="priceByCenter[g.centerId]"
                placeholder="npr. 12.50"
              />
            </div>
          </div>

          <div style="display:flex; gap:12px; align-items:center; margin-top:10px; flex-wrap: wrap;">
            <div style="min-width: 180px;">
              <div class="meta" style="margin-bottom:4px;">Order #</div>
              <input
                class="wishlist-filter"
                type="text"
                v-model="orderNumberByCenter[g.centerId]"
                placeholder="npr. 2025-001"
              />
            </div>

            <div style="min-width: 220px;">
              <div class="meta" style="margin-bottom:4px;">Kategorija (glavna)</div>
              <select
                class="wishlist-filter"
                v-model="categoryMainByCenter[g.centerId]"
                :data-category="String(categoryMainByCenter[g.centerId] || '9.00.00').split('.')[0]"
                @change="onCategoryMainChange(g.centerId)"
              >
                <option v-for="m in mainOption" :key="m.code" :value="m.code">
                  {{ m.code }} — {{ m.name }}
                </option>
              </select>
            </div>

            <div v-if="categoryMainByCenter[g.centerId] !== '9.00.00'" style="min-width: 220px;">
              <div class="meta" style="margin-bottom:4px;">Podkategorija</div>
              <select class="wishlist-filter" v-model="categoryCodeByCenter[g.centerId]">
                <option value="">— izberi —</option>
                <option v-for="s in (subOptionsByCenter[g.centerId] || [])" :key="s.code" :value="s.code">
                  {{ s.code }} — {{ s.name }}
                </option>
              </select>
            </div>
          </div>

          <div class="meta" style="margin-top:8px;">
            Prošnje:
            <ul style="margin:6px 0 0 18px;">
              <li v-for="it in g.items" :key="it.id">
                {{ it.title }} — {{ it.author }}
              </li>
            </ul>
          </div>
        </div>

        <div class="modal-actions">
          <button class="btn" type="button" @click="closePriceModal">Prekliči</button>
          <button class="btn primary" type="button" @click="confirmAddedWithPrices" :disabled="groupedByCenter.length===0">
            Potrdi
          </button>
        </div>
      </div>
    </div>

    <div v-if="showIssnModal" class="issn-modal-backdrop" @click.self="closeIssnModal">
      <div class="issn-modal">
        <h3 class="issn-modal__title">{{ $t('wishlist.form.issnModalTitle') }}</h3>
        <p class="issn-modal__sub">{{ $t('wishlist.form.issnModalSubtitle') }}</p>

        <label class="issn-modal__label">{{ $t('wishlist.form.issnVolumeLabel') }}</label>
        <input
          v-model="issnVolume"
          type="text"
          class="issn-modal__input"
          :placeholder="$t('wishlist.form.issnVolumePlaceholder')"
        />

        <label class="issn-modal__label">{{ $t('wishlist.form.issnNumberLabel') }}</label>
        <input
          v-model="issnNumber"
          type="text"
          class="issn-modal__input"
          :placeholder="$t('wishlist.form.issnNumberPlaceholder')"
        />

        <div class="issn-modal__actions">
          <button class="btn" type="button" @click="closeIssnModal">
            {{ $t('wishlist.form.issnModalCancel') }}
          </button>
          <button class="btn primary" type="button" @click="closeIssnModal">
            {{ $t('wishlist.form.issnModalSave') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style>
/* -------------------------------------------------------
   Animations
------------------------------------------------------- */
.WishlistItemFadeStagger-enter-active {
  transition: opacity .75s ease, transform .75s ease;
}
.WishlistItemFadeStagger-leave-active {
  transition: opacity .75s ease, transform .75s ease;
}

.WishlistItemFadeStagger-enter-from,
.WishlistItemFadeStagger-leave-to {
  opacity: 0;
  transform: translateY(8px);
}

.WishlistItemFadeStagger-move {
  transition: transform .75s ease;
}

/* -------------------------------------------------------
   Base layout
------------------------------------------------------- */
.wishlist-wrapper {
  padding: 16px;
  background: var(--bg);
  color: var(--text);
}

.wishlist-container {
  max-width: 1000px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* -------------------------------------------------------
   Header
------------------------------------------------------- */
.wishlist-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 12px;
}

.wishlist-header-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
}

.wishlist-header-actions .btn {
  min-width: 122px;
  white-space: nowrap;
  justify-content: center;
}

.wishlist-title {
  margin: 0;
  font-size: 22px;
  font-weight: 800;
}

.wishlist-subtitle {
  margin: 6px 0 0;
  font-size: 13px;
  color: var(--muted);
}

/* -------------------------------------------------------
   Messages
------------------------------------------------------- */
.success-message {
  padding: 10px 14px;
  border-radius: 10px;
  border: 1px solid var(--status-green-border);
  background: var(--status-green-bg);
  color: var(--status-green-text);
  font-weight: 600;
}

.success-message.toast {
  position: fixed;
  right: 16px;
  top: calc(72px + env(safe-area-inset-top));
  z-index: 1200;
  box-shadow: var(--shadow-elev-2);
}

/* -------------------------------------------------------
   Shared surfaces (cards/sections)
------------------------------------------------------- */
.wishlist-card,
.wishlist-controls {
  padding: 16px;
  border-radius: 16px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  backdrop-filter: blur(10px);
}

.wishlist-controls {
  display: grid;
  grid-template-columns: 1fr 240px;
  gap: 12px;
  padding: 14px;
}

.card-title {
  margin: 0 0 12px;
  font-weight: 800;
  font-size: 14px;
}

/* -------------------------------------------------------
   Form
------------------------------------------------------- */
.request-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 220px;
  gap: 12px;
}

.form-row.three {
  grid-template-columns: minmax(0, 1fr) 180px 160px 220px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 12px;
  font-weight: 700;
  color: var(--muted);
}

/* -------------------------------------------------------
   Controls (inputs/select/textarea/search/filter) - unified
------------------------------------------------------- */
.form-group input,
.form-group select,
.form-group textarea,
.wishlist-search,
.wishlist-filter {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  outline: none;
  font-size: 14px;
  color: var(--text);
  background: var(--select-bg); /* unified for theme-switch */
}

/* textarea specifics */
.form-group textarea {
  resize: vertical;
  min-height: 80px;
}

/* placeholders */
.form-group input::placeholder,
.form-group textarea::placeholder,
.wishlist-search::placeholder {
  color: var(--muted);
}

/* focus ring */
.form-group input:focus,
.form-group select:focus,
.form-group textarea:focus,
.wishlist-search:focus,
.wishlist-filter:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.issn-meta {
  margin-top: 6px;
  padding: 8px 10px;
  border-radius: 10px;
  border: 1px dashed var(--card-border);
  background: rgba(124, 92, 255, 0.08);
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.issn-meta--full {
  width: 100%;
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: -4px;
}

.issn-meta__info {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.issn-meta__btn {
  border: 0;
  background: transparent;
  color: var(--text);
  font-weight: 800;
  cursor: pointer;
  padding: 0;
}

.lookup-meta {
  margin-top: 6px;
  font-size: 12px;
  font-weight: 700;
  color: var(--muted);
}

.lookup-meta--warn {
  color: var(--status-yellow-text);
}

.lookup-meta--pending {
  color: var(--status-yellow-text);
}

.lookup-meta--ok {
  color: #0f9f4f;
}

.label-with-help {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.label-help {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  color: var(--status-yellow-text);
  cursor: help;
  font-size: 13px;
  line-height: 1;
}

.label-help__tooltip {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  min-width: 220px;
  max-width: min(300px, 78vw);
  padding: 8px 10px;
  border-radius: 10px;
  background: #1f2937;
  color: #fff;
  font-size: 12px;
  font-weight: 600;
  line-height: 1.3;
  box-shadow: 0 10px 24px rgba(0, 0, 0, 0.28);
  opacity: 0;
  transform: translateY(-4px);
  pointer-events: none;
  transition: opacity 0.16s ease, transform 0.16s ease;
  z-index: 20;
}

.label-help:hover .label-help__tooltip,
.label-help:focus .label-help__tooltip,
.label-help:focus-visible .label-help__tooltip {
  opacity: 1;
  transform: translateY(0);
}

/* -------------------------------------------------------
   Select styling (native-friendly)
------------------------------------------------------- */
.form-group select,
.wishlist-filter {
  cursor: pointer;
  appearance: none;
  -webkit-appearance: none;
  -moz-appearance: none;
}

/* Option colors (works where OS allows it) */
.form-group select option,
.wishlist-filter option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.form-group select option:checked,
.wishlist-filter option:checked {
  background-color: #4f46e5;
  color: #fff;
}

.wishlist-filter:hover {
  border-color: var(--nav-item-active-border);
}

.wishlist-controls.three {
  grid-template-columns: 1fr 240px 240px;
}

@media (max-width: 900px) {
  .wishlist-controls.three {
    grid-template-columns: 1fr;
  }
}

/* -------------------------------------------------------
   List
------------------------------------------------------- */
.wishlist-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.request-item {
  display: grid;
  grid-template-columns: 1fr 180px;
  gap: 14px;
  padding: 14px;
  border-radius: 18px;
  border: 1px solid var(--card-border);
  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: 0 18px 40px rgba(0,0,0,0.35), inset 0 1px 0 rgba(255,255,255,0.05);
  transition: transform 0.18s ease, box-shadow 0.18s ease;
}

.request-item:hover {
  transform: translateY(-2px);
  box-shadow: 0 26px 55px rgba(0,0,0,0.45), inset 0 1px 0 rgba(255,255,255,0.06);
}

.request-top {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 8px;
}

.request-title {
  margin: 0;
  font-size: 16px;
  font-weight: 900;
}

.request-sub {
  margin: 6px 0 0;
  font-size: 13px;
  color: var(--text);
  opacity: 0.9;
}

.sep {
  opacity: 0.6;
  margin: 0 6px;
}

.meta {
  font-size: 12px;
  color: var(--muted);
}

.request-tags {
  margin-top: 10px;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.tag {
  padding: 5px 10px;
  border-radius: 999px;
  font-size: 11px;
  background: rgba(99, 102, 241, 0.16);
  border: 1px solid rgba(99, 102, 241, 0.35);
  color: var(--text);
}

.request-note {
  margin: 10px 0 0;
  font-size: 12px;
  color: var(--muted);
}

/* -------------------------------------------------------
   Actions + Buttons
------------------------------------------------------- */
.request-actions {
  display: flex;
  flex-direction: column;
  gap: 10px;
  justify-content: center;
}

.btn {
  border-radius: 12px;
  padding: 8px 14px;
  border: 1px solid rgba(99, 102, 241, 0.35);
  color: var(--text);
  background: linear-gradient(180deg, var(--card-bg), var(--card-bg));
  transition: background 0.18s ease, border-color 0.18s ease, opacity 0.18s ease;
}

.btn-with-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.btn-with-icon .pi {
  font-size: 13px;
}

.btn:hover {
  border-color: rgba(124, 92, 255, 0.7);
  background: var(--nav-item-hover);
}

.btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.btn.primary {
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  border: 1px solid rgba(124, 92, 255, 0.65);
  color: #fff;
}

.btn.danger {
  background: var(--status-red-bg);
  border: 1px solid var(--status-red-border);
  color: var(--status-red-text);
}

.btn.danger:hover{
  border-color: rgba(239,68,68,0.55);
  background: rgba(239,68,68,0.20);
}

.btn.small {
  width: 100%;
  padding: 8px 10px;
  font-size: 13px;
}

/* -------------------------------------------------------
   Badges
------------------------------------------------------- */
.badge {
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 900;
  border: 1px solid transparent;
}

/* status */
.badge.status.new {
  background: rgba(99, 102, 241, 0.16);
  border-color: rgba(99, 102, 241, 0.35);
  color: #c7d2fe;
}
.badge.status.review {
  background: var(--status-yellow-bg);
  border-color: var(--status-yellow-border);
  color: var(--status-yellow-text);
}
.badge.status.ordered {
  background: rgba(56, 189, 248, 0.12);
  border-color: rgba(56, 189, 248, 0.35);
  color: #bae6fd;
}

.light .badge.status.ordered {
  background: rgba(14, 116, 144, 0.16);
  border-color: rgba(14, 116, 144, 0.35);
  color: #0f4c81;
}
.badge.status.added {
  background: var(--status-green-bg);
  border-color: var(--status-green-border);
  color: var(--status-green-text);
}
.badge.status.rejected {
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
  color: var(--status-red-text);
}

/* priority */
.badge.prio.low {
  background: var(--status-green-bg);
  border-color: var(--status-green-border);
  color: var(--status-green-text);
}
.badge.prio.normal {
  background: var(--status-yellow-bg);
  border-color: var(--status-yellow-border);
  color: var(--status-yellow-text);
}
.badge.prio.high {
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
  color: var(--status-red-text);
}

/* -------------------------------------------------------
   Empty state
------------------------------------------------------- */
.empty-state {
  padding: 16px;
  border-radius: 16px;
  border: 1px dashed var(--card-border);
  background: rgba(255,255,255,0.05);
  color: var(--muted);
  text-align: center;
  font-size: 13px;
}

/* -------------------------------------------------------
   Animations
------------------------------------------------------- */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.5s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

.modal-backdrop{
  position: fixed;
  inset: 0;
  background: rgba(0,0,0,.6);
  z-index: 9999;
  display: grid;
  place-items: center;
  padding: 16px;
}

.modal{
  width: min(820px, 95vw);
  max-height: 85vh;
  overflow: auto;
  border-radius: 16px;
  border: 1px solid var(--card-border);
  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: 0 26px 60px rgba(0,0,0,.55);
  padding: 14px;
}

.modal--added{
  background: var(--modal-bg);
}

.modal-header{
  display:flex;
  align-items:center;
  justify-content:space-between;
  gap:12px;
}

.modal-group{
  margin-top: 12px;
  padding: 12px;
  border-radius: 14px;
  border: 1px solid var(--card-border);
  background: rgba(255,255,255,0.04);
}

.modal-actions{
  display:flex;
  justify-content:flex-end;
  gap:10px;
  margin-top: 14px;
}

.issn-modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 10000;
  background: var(--modal-overlay);
  display: grid;
  place-items: center;
  padding: 16px;
}

.issn-modal {
  width: min(520px, 92vw);
  border-radius: 16px;
  border: 1px solid var(--card-border);
  background: var(--modal-bg);
  box-shadow: 0 22px 60px rgba(0,0,0,0.55);
  padding: 16px;
}

.issn-modal__title {
  margin: 0;
  font-size: 18px;
  font-weight: 900;
}

.issn-modal__sub {
  margin: 6px 0 12px;
  color: var(--muted);
  font-size: 13px;
}

.issn-modal__label {
  display: block;
  font-size: 12px;
  color: var(--muted);
  font-weight: 800;
  margin: 0 0 6px;
}

.issn-modal__input {
  width: 100%;
  padding: 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--select-text, var(--text));
  outline: none;
  margin-bottom: 10px;
}

.issn-modal__actions {
  margin-top: 10px;
  display: flex;
  gap: 10px;
  justify-content: flex-end;
  flex-wrap: wrap;
}

.bulkbar{
  padding: 12px 16px;
  display: grid;
  grid-template-columns: 1fr auto;
  align-items: center;
  gap: 14px;
}

.bulkbar__left{
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.bulkbar__check{
  display: inline-flex;
  align-items: center;
  gap: 8px;
  font-weight: 800;
  white-space: nowrap;
  flex: 0 0 auto;       
}

.bulkbar__check input{
  flex: 0 0 auto;
}

.bulkbar__checktext{
  display: inline-block;
  min-width: 0;
  max-width: 260px;         
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis; 
  display: inline-block;
}

.bulkbar__meta{
  white-space: nowrap;
  opacity: .85;
}

.bulkbar__right{
  display: inline-flex;
  align-items: center;
  gap: 10px;
  flex-wrap: nowrap;        
}

.bulkbar__select{
  min-width: 170px;
  height: 40px;
}

.bulkbar__btn{
  height: 40px;
  white-space: nowrap;
  padding: 0 14px;
}

.bulkbar__count{
  flex: 0 0 auto;        
  white-space: nowrap;
  padding: 6px 10px;
  border-radius: 999px;
  border: 1px solid rgba(255,255,255,0.14);
  background: rgba(255,255,255,0.06);
  opacity: .9;
}

.bulkbar__right .wishlist-filter,
.bulkbar__right .btn{
  height: 40px;
}

.bd-dot {
  opacity: 0.6;
  margin: 0 6px;
}


/* -------------------------------------------------------
   Responsive
------------------------------------------------------- */
@media (max-width: 900px) {
  .form-row,
  .form-row.three,
  .wishlist-controls,
  .request-item {
    grid-template-columns: 1fr;
  }

  .request-actions {
    flex-direction: row;
  }
}

@media (max-width: 720px){
  .bulkbar{
    grid-template-columns: 1fr;
  }
  .bulkbar__checktext{
    max-width: 100%;
  }
  .bulkbar__right{
    flex-wrap: wrap;
  }
  .bulkbar__select,
  .bulkbar__btn{
    width: 100%;
  }

  .wishlist-header {
    flex-direction: column;
    align-items: stretch;
  }

  .wishlist-header-actions {
    flex-direction: column;
    width: 100%;
  }

  .wishlist-header-actions .btn {
    width: 100%;
    min-width: 0;
  }
}

</style>





