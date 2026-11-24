<script setup>
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl, resolveAssetUrl } from '@/utils/api'
import { fetchCenters } from '@/utils/centers'

const router = useRouter()
const { t } = useI18n()

const BOOK_API = apiUrl('/api/Book')
const BORROWING_API = apiUrl('/api/Borrowing')
const INVENTORY_API = apiUrl('/api/inventory')
const WISHLIST_API = apiUrl('/api/wishlist')
const USERS_API = apiUrl('/Users')

// state
const status = ref('idle') // idle | loading | error | ready
const errorMsg = ref('')
const successMessage = ref('')
const borrowModalOpen = ref(false)
const borrowModalBook = ref(null)
const borrowMode = ref('self') // self | user
const borrowUsers = ref([])
const borrowUsersLoading = ref(false)
const borrowUsersError = ref('')
const borrowUserQuery = ref('')
const borrowTargetUserId = ref('')
const borrowSubmitting = ref(false)
const borrowModalError = ref('')

function showPageMessage(message) {
  successMessage.value = message
  setTimeout(() => {
    successMessage.value = ''
  }, 3000)
}

const viewMode = ref('grid')
const saved = localStorage.getItem('viewMode')
if (saved === 'grid' || saved === 'list') viewMode.value = saved
const books = ref([])
const getBookId = (b) => b?.id ?? b?.Id
const availabilityMap = ref({}) // { [bookId]: AvailabilityDto }
const isAdmin = computed(() => localStorage.getItem('role') === 'admin')
const thumbFallbackByBook = ref({})
const centers = ref([])

// filters
const q = ref('')
const selectedLanguage = ref('')
const selectedCategory = ref('')
const selectedCenter = ref('')
const availabilityFilter = ref('all')

// pagination
const page = ref(1)
const pageSize = ref(50)
const STAGGER_LIMIT = 6
const STAGGER_STEP_MS = 18

// helpers
const capitalizeGenre = (g) => {
  const text = String(g ?? '').trim()
  if (!text) return ''
  const lower = text.toLocaleLowerCase('sl-SI')
  return lower.replace(/(^|[\s/.,-])(\p{L})/gu, (match, sep, ch) =>
    `${sep}${ch.toLocaleUpperCase('sl-SI')}`
  )
}
const normalizeIsbn = (v) => String(v ?? '').replace(/[\s-]/g, '').toUpperCase()
const normalizeText = (v) => String(v ?? '').trim().toLowerCase()
const makeTitleAuthorKey = (title, author) =>
  `${normalizeText(title)}|${normalizeText(author)}`

function makeThumbCoverUrl(rawCoverUrl) {
  const raw = String(rawCoverUrl ?? '')
  if (!raw.startsWith('/covers/')) return raw // external URL ali druga pot

  const qIndex = raw.indexOf('?')
  const clean = qIndex >= 0 ? raw.slice(0, qIndex) : raw
  const query = qIndex >= 0 ? raw.slice(qIndex) : ''

  const dot = clean.lastIndexOf('.')
  if (dot < 0) return raw

  return `${clean.slice(0, dot)}_thumb.webp${query}`
}

function getCoverSrc(book) {
  const raw = book?.coverUrl ?? book?.coverURL
  if (!raw) return ''

  const key = String(getBookId(book) ?? raw)
  if (thumbFallbackByBook.value[key]) return resolveAssetUrl(raw)

  return resolveAssetUrl(makeThumbCoverUrl(raw))
}

function onCoverError(event, book) {
  const raw = book?.coverUrl ?? book?.coverURL
  if (!raw) return

  const img = event?.target
  if (!img) return

  // Prepreči zanko, če fallback full-size tudi odpove.
  if (img.dataset.fallbackApplied === '1') return

  img.dataset.fallbackApplied = '1'

  const key = String(getBookId(book) ?? raw)
  thumbFallbackByBook.value = { ...thumbFallbackByBook.value, [key]: true }
  img.src = resolveAssetUrl(raw)
}

const centerNameById = computed(() =>
  Object.fromEntries(
    centers.value.map((center) => [center.id, center.name])
  )
)
const centerIdByName = computed(() =>
  Object.fromEntries(
    centers.value
      .map((center) => [normalizeText(center?.name), String(center.id)])
      .filter(([name]) => !!name)
  )
)

const orderedByCenter = ref({}) // { [centerId]: { isbns:Set, keys:Set } }
const borrowedByCenter = ref({}) // { [centerId]: { bookIds:Set, isbns:Set, keys:Set } }
const borrowedCenterLabelById = ref({})
let refreshTimer = null
let appIsSuspended = false
const APP_SUSPEND_EVENT = 'libraryapp:suspend'
const APP_RESUME_EVENT = 'libraryapp:resume'

function clearRefreshTimer() {
  if (refreshTimer) {
    clearInterval(refreshTimer)
    refreshTimer = null
  }
}

async function loadAllBookData() {
  await loadCenters()
  await loadBooks()
  await loadCenterOrders()
  await loadBorrowedByCenter()
  await loadAvailabilityAll()
}

function startRefreshTimer() {
  if (refreshTimer !== null || appIsSuspended) return

  refreshTimer = setInterval(async () => {
    await loadBooks({ silent: true })
    await loadCenterOrders()
    await loadBorrowedByCenter()
    await loadAvailabilityAll()
  }, 30000)
}

function resetBookPageState() {
  books.value = []
  availabilityMap.value = {}
  orderedByCenter.value = {}
  borrowedByCenter.value = {}
  borrowedCenterLabelById.value = {}
  centers.value = []
  thumbFallbackByBook.value = {}
  borrowUsers.value = []
  borrowUsersError.value = ''
  borrowUserQuery.value = ''
  borrowTargetUserId.value = ''
  borrowSubmitting.value = false
  borrowModalError.value = ''
  borrowModalOpen.value = false
  borrowModalBook.value = null
  page.value = 1
  status.value = 'idle'
  errorMsg.value = ''
  successMessage.value = ''
}

function handleAppSuspend() {
  appIsSuspended = true
  clearRefreshTimer()
  resetBookPageState()
}

async function handleAppResume() {
  appIsSuspended = false
  if (refreshTimer !== null) return

  await loadAllBookData()
  startRefreshTimer()
}

function getAuthHeaders() {
  const token = localStorage.getItem('token')
  return token ? { Authorization: `Bearer ${token}` } : {}
}

function getCurrentUserId() {
  const u = localStorage.getItem('user')
  if (u) {
    try {
      const parsed = JSON.parse(u)
      if (parsed?.id != null) return String(parsed.id)
      if (parsed?.Id != null) return String(parsed.Id)
    } catch {}
  }
  const uid = localStorage.getItem('userId')
  return uid ? String(uid) : null
}

function getCurrentUsername() {
  const u = localStorage.getItem('user')
  if (!u) return ''

  try {
    const parsed = JSON.parse(u)
    return String(parsed?.username ?? parsed?.Username ?? '').trim()
  } catch {
    return ''
  }
}

const borrowCurrentUserLabel = computed(() => {
  const username = getCurrentUsername()
  const id = getCurrentUserId()
  if (username) return username
  if (id) return `ID ${id}`
  return 'Trenutni uporabnik'
})

const filteredBorrowUsers = computed(() => {
  const query = String(borrowUserQuery.value || '').trim().toLowerCase()
  if (!query) return borrowUsers.value

  return borrowUsers.value.filter(u => {
    const haystack = [
      u.username,
      u.email,
      u.centerName,
      u.role,
      String(u.id)
    ].join(' ').toLowerCase()

    return haystack.includes(query)
  })
})

async function loadBorrowUsers() {
  borrowUsersLoading.value = true
  borrowUsersError.value = ''

  try {
    const res = await axios.get(`${USERS_API}/all`, { headers: getAuthHeaders() })
    const arr = Array.isArray(res.data) ? res.data : []

    borrowUsers.value = arr
      .map(x => {
        const id = Number(x?.id ?? x?.Id)
        const usernameRaw = String(x?.username ?? x?.Username ?? '').trim()

        return {
          id,
          username: usernameRaw || `ID ${id}`,
          email: String(x?.email ?? x?.Email ?? '').trim(),
          centerName: String(x?.centerName ?? x?.CenterName ?? '').trim(),
          role: String(x?.role ?? x?.Role ?? '').trim()
        }
      })
      .filter(x => Number.isInteger(x.id) && x.id > 0)
      .sort((a, b) => a.username.localeCompare(b.username))
  } catch (e) {
    borrowUsers.value = []
    borrowUsersError.value = e?.response?.data || 'Nalaganje uporabnikov ni uspelo.'
  } finally {
    borrowUsersLoading.value = false
  }
}

async function openBorrowModal(book) {
  borrowModalBook.value = book
  borrowMode.value = 'self'
  borrowUserQuery.value = ''
  borrowTargetUserId.value = ''
  borrowModalError.value = ''
  borrowModalOpen.value = true

  await loadBorrowUsers()
}

function closeBorrowModal() {
  borrowModalOpen.value = false
  borrowModalBook.value = null
  borrowMode.value = 'self'
  borrowUserQuery.value = ''
  borrowTargetUserId.value = ''
  borrowModalError.value = ''
}

// fetch books
async function loadBooks(options = {}) {
  const { silent = false } = options
  if (!silent) {
    status.value = 'loading'
    errorMsg.value = ''
  }

  try {
    const res = await axios.get(`${BOOK_API}/vse`, { headers: getAuthHeaders() })
    if (appIsSuspended) return
    const data = res.data

    books.value = (Array.isArray(data) ? data : []).map(b => ({
      ...b,
      genres: Array.isArray(b.genres) ? b.genres : (Array.isArray(b.genre) ? b.genre : [])
    }))
    thumbFallbackByBook.value = {}

    status.value = 'ready'
  } catch (e) {
    if (!silent) {
      status.value = 'error'
      errorMsg.value = e?.response?.data || e?.message || 'Napaka pri branju knjig'
    } else {
      console.error('Books load failed:', e)
    }
  }
}

async function loadAvailabilityAll() {
  try {
    const res = await axios.get(`${INVENTORY_API}/availability/all`, {
      headers: getAuthHeaders()
    })
    if (appIsSuspended) return
    availabilityMap.value = Object.fromEntries((res.data ?? []).map(a => [a.bookId, a]))
  } catch (e) {
    // availability ni kritičen za render, zato samo log
    console.error('Availability load failed:', e)
    availabilityMap.value = {}
  }
}

async function loadCenters() {
  try {
    centers.value = await fetchCenters()
    if (appIsSuspended) return
  } catch (e) {
    console.error('Centers load failed:', e)
    centers.value = []
  }
}

function centerLabel(centerId) {
  const id = String(centerId)
  return borrowedCenterLabelById.value[id] ?? centerNameById.value[Number(id)] ?? `Center ${id}`
}

const centerOptions = computed(() => {
  const ids = new Set([
    ...Object.keys(orderedByCenter.value || {}).map(String),
    ...Object.keys(borrowedByCenter.value || {}).map(String)
  ])

  return Array.from(ids)
    .map(id => ({ id: String(id), label: centerLabel(id) }))
    .sort((a, b) => a.label.localeCompare(b.label))
})

const languageOptions = computed(() => {
  const s = new Set()
  for (const b of books.value) if (b.language) s.add(String(b.language).toUpperCase())
  return Array.from(s).sort((a, b) => a.localeCompare(b))
})

const mainCategories = [
  { code: '1.00.00', name: 'Naravoslovje' },
  { code: '2.00.00', name: 'Tehnika' },
  { code: '4.00.00', name: 'Biotehnika' },
  { code: '5.00.00', name: 'Družboslovje' },
  { code: '6.00.00', name: 'Humanistika' },
  { code: '7.00.00', name: 'Interdisciplinarne raziskave' },
  { code: '9.00.00', name: 'Drugo' },
  { code: '10.00.00', name: 'Serijske publikacije' }
]

const categoryMainFromCode = (code) => {
  if (!code) return ''
  const parts = String(code).split('.')
  return parts.length === 3 ? `${parts[0]}.00.00` : ''
}

function categoryNameKey(code) {
  return `books.categoryNames.${String(code || '').replaceAll('.', '_')}`
}

function getCenterEntry(centerId) {
  return orderedByCenter.value?.[centerId] ?? null
}

function isBookInCenterSet(book, centerId, key) {
  const entry = getCenterEntry(centerId)
  if (!entry) return false

  const isbn = normalizeIsbn(book?.isbn ?? book?.ISBN)
  if (isbn && entry[key]?.isbns?.has(isbn)) return true

  const titleAuthorKey = makeTitleAuthorKey(book?.title, book?.author)
  return !!titleAuthorKey && entry[key]?.keys?.has(titleAuthorKey)
}

function isBookOrderedInCenter(book, centerId) {
  return isBookInCenterSet(book, centerId, 'ordered')
}

function isBookAddedInCenter(book, centerId) {
  return isBookInCenterSet(book, centerId, 'added')
}

function isBookOrderedByCenter(book, centerId) {
  return isBookOrderedInCenter(book, centerId) || isBookAddedInCenter(book, centerId)
}

function isBookBorrowedByCenter(book, centerId) {
  const entry = borrowedByCenter.value?.[centerId]
  if (!entry) return false

  const bookId = Number(getBookId(book))
  if (Number.isInteger(bookId) && entry.bookIds?.has(bookId)) return true

  const isbn = normalizeIsbn(book?.isbn ?? book?.ISBN)
  if (isbn && entry.isbns?.has(isbn)) return true

  const key = makeTitleAuthorKey(book?.title, book?.author)
  return key !== '|' && entry.keys?.has(key)
}

function isBookInCenter(book, centerId) {
  return isBookOrderedByCenter(book, centerId) || isBookBorrowedByCenter(book, centerId)
}

function isBookInAnyCenterSet(book, key) {
  const map = orderedByCenter.value || {}
  const entries = Object.values(map)
  if (entries.length === 0) return false

  const isbn = normalizeIsbn(book?.isbn ?? book?.ISBN)
  const titleAuthorKey = makeTitleAuthorKey(book?.title, book?.author)

  for (const entry of entries) {
    const bucket = entry?.[key]
    if (!bucket) continue
    if (isbn && bucket.isbns?.has(isbn)) return true
    if (titleAuthorKey && titleAuthorKey !== '|' && bucket.keys?.has(titleAuthorKey)) return true
  }

  return false
}

function isBookOrderedAnywhere(book) {
  return isBookInAnyCenterSet(book, 'ordered')
}

function isBookAddedAnywhere(book) {
  return isBookInAnyCenterSet(book, 'added')
}

function isBookOrderedPending(book) {
  if (selectedCenter.value) {
    return isBookOrderedInCenter(book, selectedCenter.value) && !isBookAddedInCenter(book, selectedCenter.value)
  }

  return isBookOrderedAnywhere(book) && !isBookAddedAnywhere(book)
}

function isBookFullyBorrowed(book) {
  const a = availabilityMap.value[getBookId(book)]
  if (!a) return false
  return a.total > 0 && a.available === 0
}
async function loadCenterOrders() {
  try {
    const res = await axios.get(`${WISHLIST_API}/center-orders`, { headers: getAuthHeaders() })
    if (appIsSuspended) return
    const data = Array.isArray(res.data) ? res.data : []
    const map = {}

    for (const r of data) {
      const status = r?.status ?? r?.Status
      if (status !== 2 && status !== 3) continue

      const centerId = r?.centerId ?? r?.CenterId
      if (!centerId) continue

      if (!map[centerId]) {
        map[centerId] = {
          ordered: { isbns: new Set(), keys: new Set() },
          added: { isbns: new Set(), keys: new Set() }
        }
      }

      const bucket = status === 2 ? map[centerId].ordered : map[centerId].added

      const isbn = normalizeIsbn(r?.isbn ?? r?.ISBN)
      if (isbn) bucket.isbns.add(isbn)

      const key = makeTitleAuthorKey(r?.title ?? r?.Title, r?.author ?? r?.Author)
      if (key !== '|') bucket.keys.add(key)
    }

    orderedByCenter.value = map
  } catch (e) {
    console.error('Center orders load failed:', e)
    orderedByCenter.value = {}
  }
}

async function loadBorrowedByCenter() {
  try {
    const res = await axios.get(`${INVENTORY_API}/all`, { headers: getAuthHeaders() })
    if (appIsSuspended) return
    const data = Array.isArray(res.data) ? res.data : []
    const map = {}
    const labels = {}

    for (const row of data) {
      const centerName = String(row?.borrowedByCenter ?? row?.BorrowedByCenter ?? '').trim()
      if (!centerName) continue

      const normalizedCenterName = normalizeText(centerName)
      if (!normalizedCenterName) continue

      const centerId = centerIdByName.value[normalizedCenterName]
      const centerKey = centerId || `name:${normalizedCenterName}`

      if (!map[centerKey]) {
        map[centerKey] = { bookIds: new Set(), isbns: new Set(), keys: new Set() }
      }
      if (!centerId) labels[centerKey] = centerName

      const bookId = Number(row?.bookId ?? row?.BookId)
      if (Number.isInteger(bookId) && bookId > 0) {
        map[centerKey].bookIds.add(bookId)
      }

      const isbn = normalizeIsbn(row?.bookIsbn ?? row?.BookIsbn)
      if (isbn) map[centerKey].isbns.add(isbn)

      const key = makeTitleAuthorKey(row?.bookTitle ?? row?.BookTitle, row?.bookAuthor ?? row?.BookAuthor)
      if (key !== '|') map[centerKey].keys.add(key)
    }

    borrowedByCenter.value = map
    borrowedCenterLabelById.value = labels
  } catch (e) {
    console.error('Borrowed-by-center load failed:', e)
    borrowedByCenter.value = {}
    borrowedCenterLabelById.value = {}
  }
}

function change(){
  viewMode.value = viewMode.value === 'grid' ? 'list' : 'grid'
}

function getCardStaggerStyle(index) {
  const delay = Math.min(index, STAGGER_LIMIT) * STAGGER_STEP_MS
  return { '--stagger': `${delay}ms` }
}

// filtering
const filteredBooks = computed(() => {
  const query = q.value.trim().toLowerCase()
  const queryIsbn = normalizeIsbn(q.value)

  return books.value.filter(b => {
    const title = (b.title || '').toLowerCase()
    const author = (b.author || '').toLowerCase()
    const isbn = normalizeIsbn(b?.isbn ?? b?.ISBN)

    const matchesQuery =
      !query ||
      title.includes(query) ||
      author.includes(query) ||
      (!!queryIsbn && !!isbn && isbn.includes(queryIsbn))

    const matchesLang =
      !selectedLanguage.value ||
      String(b.language || '').toUpperCase() === selectedLanguage.value

    const matchesCategory =
      !selectedCategory.value ||
      categoryMainFromCode(b.categoryCode ?? b.CategoryCode) === selectedCategory.value

    const matchesCenter =
      !selectedCenter.value ||
      isBookInCenter(b, selectedCenter.value)

    const availability = availabilityMap.value[getBookId(b)]
    const isBorrowedOnly = !!availability && availability.total > 0 && availability.available === 0
    const isAvailableOnly = !!availability && availability.available > 0
    const matchesAvailability =
      availabilityFilter.value === 'all' ||
      (availabilityFilter.value === 'available' && isAvailableOnly) ||
      (availabilityFilter.value === 'borrowed' && isBorrowedOnly)

    return matchesQuery && matchesLang && matchesCategory && matchesCenter && matchesAvailability
  })
})

// pagination
const totalPages = computed(() =>
  Math.max(1, Math.ceil(filteredBooks.value.length / pageSize.value))
)

const pagedBooks = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return filteredBooks.value.slice(start, start + pageSize.value)
})

function resetPage() { page.value = 1 }
function prevPage() { if (page.value > 1) page.value-- }
function nextPage() { if (page.value < totalPages.value) page.value++ }

// reset pagination when filters change
watch([q, selectedLanguage, selectedCategory, selectedCenter, availabilityFilter, pageSize], resetPage)
watch(viewMode, (val) => {
  localStorage.setItem('viewMode', val)
})

// actions
const goToAddBook = () => router.push('/addbook')

async function performBorrow(b, targetUserId, targetUsername = '') {
  const a = availabilityMap.value[getBookId(b)]
  if (a && a.available === 0) {
    if (borrowModalOpen.value) borrowModalError.value = 'Ni prostih kopij.'
    else showPageMessage(t('books.messages.noCopies'))
    return false
  }

  try {
    await axios.post(`${BORROWING_API}/borrow`, {
      bookId: getBookId(b),
      userId: String(targetUserId)
    }, { headers: getAuthHeaders() })

    const currentUserId = getCurrentUserId()
    if (targetUsername && String(currentUserId || '') !== String(targetUserId)) {
      showPageMessage(t('books.success.borrowedForUser', {
        title: b.title,
        username: targetUsername
      }))
    } else {
      showPageMessage(t('books.success.borrowed', {
        title: b.title
      }))
    }
    await loadAvailabilityAll()
    return true
  } catch (e) {
    const errText = e?.response?.status === 409 ? 'Ni prostih kopij.' : 'Napaka pri izposoji knjige.'

    if (borrowModalOpen.value) borrowModalError.value = errText
    else showPageMessage(errText)
    return false
  }
}

async function confirmBorrowForAdmin() {
  borrowModalError.value = ''
  if (!borrowModalBook.value) {
    borrowModalError.value = 'Izbrana knjiga manjka.'
    return
  }

  let targetUserId = ''
  let targetUsername = ''

  if (borrowMode.value === 'self') {
    targetUserId = getCurrentUserId() || ''
    targetUsername = borrowCurrentUserLabel.value
  } else {
    targetUserId = String(borrowTargetUserId.value || '')
    if (!targetUserId) {
      borrowModalError.value = 'Izberi uporabnika.'
      return
    }
    const selected = borrowUsers.value.find(u => String(u.id) === targetUserId)
    targetUsername = selected?.username || `ID ${targetUserId}`
  }

  if (!targetUserId) {
    borrowModalError.value = 'Manjka userId (prijava).'
    return
  }

  borrowSubmitting.value = true
  try {
    const ok = await performBorrow(borrowModalBook.value, targetUserId, targetUsername)
    if (ok) closeBorrowModal()
  } finally {
    borrowSubmitting.value = false
  }
}

async function borrowBook(b) {
  if (isAdmin.value) {
    await openBorrowModal(b)
    return
  }

  const userId = getCurrentUserId()
  if (!userId) {
    showPageMessage(t('books.messages.missingUserId'))
    return
  }

  await performBorrow(b, userId)
}

onMounted(async () => {
  window.addEventListener(APP_SUSPEND_EVENT, handleAppSuspend)
  window.addEventListener(APP_RESUME_EVENT, handleAppResume)

  await loadAllBookData()
  startRefreshTimer()
})

onBeforeUnmount(() => {
  window.removeEventListener(APP_SUSPEND_EVENT, handleAppSuspend)
  window.removeEventListener(APP_RESUME_EVENT, handleAppResume)
  clearRefreshTimer()
})
</script>


<template>
  <div class="books-wrapper">
    <div class="books-container">

      <!-- HEADER -->
      <div class="books-header">
        <div>
          <h2 class="books-title section-title">{{ $t('books.title') }}</h2>
          <p class="books-subtitle">{{ $t('books.subtitle') }}</p>
        </div>
        <div class="books-actions" v-if="isAdmin">
          <button class="btn" type="button" @click="goToAddBook">
            {{ $t('books.addBook') }}
          </button>
        </div>
      </div>

      <transition name="fade">
        <div v-if="successMessage" class="success-message">
          {{ successMessage }}
        </div>
      </transition>

      <!-- FILTERS -->
      <div class="books-filters surface">
        <input
          v-model="q"
          @input="resetPage"
          type="text"
          :placeholder="$t('books.searchPlaceholder')"
        />

        <select v-model="selectedCenter" @change="resetPage">
          <option value="">{{ $t('books.allCenters') }}</option>
          <option
            v-for="c in centerOptions"
            :key="c.id"
            :value="c.id"
          >
            {{ c.label }}
          </option>
        </select>

        <select v-model="selectedCategory" @change="resetPage">
          <option value="">{{ $t('books.allCategories') }}</option>
          <option v-for="c in mainCategories" :key="c.code" :value="c.code">
            {{ c.code }} - {{ $t(categoryNameKey(c.code)) }}
          </option>
        </select>

        <select v-model="selectedLanguage" @change="resetPage">
          <option value="">{{ $t('books.allLanguages') }}</option>
          <option v-for="l in languageOptions" :key="l" :value="l">
            {{ l }}
          </option>
        </select>
      </div>

      <!-- STATES -->
      <div v-if="status === 'loading'" class="books-empty empty-state">
        {{ $t('books.loading') }}
      </div>
      <div v-else-if="status === 'error'" class="books-empty empty-state">
        {{ $t('books.error') }}: {{ errorMsg }}
      </div>

      <!--  DISPLAY TAB -->
      <div class="display-tools">
        <div class="display-toggle">
          <button
            class="view-btn"
            :class="{ active: viewMode === 'grid' }"
            type="button"
            @click="viewMode = 'grid'"
            aria-label="Grid view"
            title="Grid view"
          >
            <i class="pi pi-th-large view-icon" aria-hidden="true"></i>
          </button>
          <button
            class="view-btn"
            :class="{ active: viewMode === 'list' }"
            type="button"
            @click="viewMode = 'list'"
            aria-label="List view"
            title="List view"
          >
            <i class="pi pi-list view-icon" aria-hidden="true"></i>
          </button>
        </div>
        <select v-model="availabilityFilter" class="availability-filter" aria-label="Availability filter">
          <option value="all">{{ $t('books.availabilityFilter.all') }}</option>
          <option value="available">{{ $t('books.availabilityFilter.availableOnly') }}</option>
          <option value="borrowed">{{ $t('books.availabilityFilter.borrowedOnly') }}</option>
        </select>
      </div>

      <!-- GRID -->
      <transition name="ViewModeFadeIn" mode="out-in" appear>
        <div v-if="viewMode === 'grid'" key="grid" class="books-grid">
            <div
              v-for="(b, index) in pagedBooks"
              :key="b.id"
              class="book-card"
              :class="{ 'book-card--borrowed': isBookFullyBorrowed(b) }"
              :style="getCardStaggerStyle(index)"
            >
              <div class="book-cover" :class="{ 'book-cover--borrowed': isBookFullyBorrowed(b) }">
                <img
                  v-if="b.coverUrl || b.coverURL"
                  :src="getCoverSrc(b)"
                  alt="Cover"
                  loading="lazy"
                  decoding="async"
                  width="320"
                  height="460"
                  @error="onCoverError($event, b)"
                />
                <p v-else class="book-cover-fallback-icon" aria-hidden="true">📘</p>
              </div>

              <div class="book-body">
                <h3 class="book-title">{{ b.title }}</h3>
                <p class="book-author">{{ b.author }}</p>

                <div class="book-meta">
                  <span v-if="b.yearPublished">{{ b.yearPublished }}</span>
                  <span v-if="b.language">{{ String(b.language).toUpperCase() }}</span>

                  <!-- AVAILABILITY (from inventory copies) -->
                  <span
                    v-if="isBookOrderedPending(b)"
                    class="ordered-note"
                  >
                    • {{ $t('books.orderedNotAvailable') }}
                  </span>
                  <span
                    v-else-if="availabilityMap[getBookId(b)]"
                    :class="{ 'borrowed-note': availabilityMap[getBookId(b)].total > 0 && availabilityMap[getBookId(b)].available === 0 }"
                  >
                    • {{ $t('books.available') }}: {{ availabilityMap[getBookId(b)].available }}/{{ availabilityMap[getBookId(b)].total }}
                  </span>
                  <span v-else>
                    • {{ $t('books.available') }}: -
                  </span>
                </div>

                <!-- GENRES -->
                <div class="book-genres" v-if="b.genres?.length">
                  <span
                    v-for="g in b.genres"
                    :key="g"
                    class="tag"
                  >
                    {{ capitalizeGenre(g) }}
                  </span>
                </div>
              </div>

              <div class="book-actions">
                <router-link class="btn small" :to="`/book/${b.id}`">
                  {{ $t('books.view') }}
                </router-link>

                <button
                  class="btn small"
                  type="button"
                  @click="borrowBook(b)"
                  :disabled="(availabilityMap[getBookId(b)] ? availabilityMap[getBookId(b)].available === 0 : false) || isBookOrderedPending(b)"
                >
                  {{ $t('books.borrow') }}
                </button>
              </div>
            </div>
        </div>

        <!-- LIST -->
        <div v-else key="list" class="books-list">
          <div
            v-for="(b, index) in pagedBooks"
            :key="b.id"
            class="book-row"
            :class="{ 'book-row--borrowed': isBookFullyBorrowed(b) }"
            :style="getCardStaggerStyle(index)"
          >
            <div class="book-row-cover" :class="{ 'book-row-cover--borrowed': isBookFullyBorrowed(b) }">
              <img
                v-if="b.coverUrl || b.coverURL"
                :src="getCoverSrc(b)"
                alt="Cover"
                loading="lazy"
                decoding="async"
                width="320"
                height="460"
                @error="onCoverError($event, b)"
              />
              <i v-else class="pi pi-book book-cover-fallback-icon" aria-hidden="true"></i>
            </div>

            <div class="book-row-body">
              <div class="book-row-title">{{ b.title }}</div>
              <div class="book-row-sub">
                <span class="book-row-author">{{ b.author }}</span>
                <span v-if="b.yearPublished">• {{ b.yearPublished }}</span>
                <span v-if="b.language">• {{ String(b.language).toUpperCase() }}</span>
              </div>

              <div class="book-row-meta">
                <span v-if="isBookOrderedPending(b)" class="ordered-note">
                  • {{ $t('books.orderedNotAvailable') }}
                </span>
                <span
                  v-else-if="availabilityMap[getBookId(b)]"
                  :class="{ 'borrowed-note': availabilityMap[getBookId(b)].total > 0 && availabilityMap[getBookId(b)].available === 0 }"
                >
                  • {{ $t('books.available') }}: {{ availabilityMap[getBookId(b)].available }}/{{ availabilityMap[getBookId(b)].total }}
                </span>
                <span v-else>
                  • {{ $t('books.available') }}: -
                </span>
              </div>

              <div class="book-row-genres" v-if="b.genres?.length">
                <span v-for="g in b.genres" :key="g" class="tag">
                  {{ capitalizeGenre(g) }}
                </span>
              </div>
            </div>

            <div class="book-row-actions">
              <router-link class="btn small" :to="`/book/${b.id}`">
                {{ $t('books.view') }}
              </router-link>
              <button
                class="btn small"
                type="button"
                @click="borrowBook(b)"
                :disabled="(availabilityMap[getBookId(b)] ? availabilityMap[getBookId(b)].available === 0 : false) || isBookOrderedPending(b)"
              >
                {{ $t('books.borrow') }}
              </button>
            </div>
          </div>
        </div>
      </transition>


      <!-- EMPTY AFTER FILTER -->
      <div v-if="status === 'ready' && filteredBooks.length === 0" class="books-empty empty-state">
        {{ $t('books.noBooks') }}
      </div>

      <!-- PAGINATION -->
      <div v-if="status === 'ready' && filteredBooks.length > 0" class="books-pagination">
        <button class="btn small" @click="prevPage" :disabled="page === 1">
          <i class="pi pi-angle-left" aria-hidden="true"></i>
        </button>
        <span>{{ page }} / {{ totalPages }}</span>
        <button class="btn small" @click="nextPage" :disabled="page === totalPages">
          <i class="pi pi-angle-right" aria-hidden="true"></i>
        </button>
        <span class="pager-spacer"></span>
        <select v-model.number="pageSize" class="selectSmall">
          <option :value="50">{{ $t('books.pageSizeOption', { size: 50 }) }}</option>
          <option :value="100">{{ $t('books.pageSizeOption', { size: 100 }) }}</option>
          <option :value="150">{{ $t('books.pageSizeOption', { size: 150 }) }}</option>
        </select>
      </div>

      <div
        v-if="borrowModalOpen && isAdmin"
        class="borrow-modal-backdrop"
        @click.self="closeBorrowModal"
      >
        <div class="borrow-modal surface" role="dialog" aria-modal="true">
          <h3 class="borrow-modal-title">{{ $t('books.borrowModal.title') }}</h3>
          <p class="borrow-modal-subtitle">
            {{ borrowModalBook?.title || '-' }}
          </p>

          <div class="borrow-mode-options">
            <label class="borrow-mode-option">
              <input v-model="borrowMode" type="radio" value="self" />
              <span>{{ $t('books.borrowModal.forSelf') }}: <b>{{ borrowCurrentUserLabel }}</b></span>
            </label>
            <label class="borrow-mode-option">
              <input v-model="borrowMode" type="radio" value="user" />
              <span>{{ $t('books.borrowModal.forUser') }}</span>
            </label>
          </div>

          <div v-if="borrowMode === 'user'" class="borrow-user-picker">
            <input
              v-model="borrowUserQuery"
              class="borrow-user-search"
              type="text"
              :placeholder="$t('books.borrowModal.searchUsersPlaceholder')"
            />
            <select v-model="borrowTargetUserId" class="borrow-user-select">
              <option value="">{{ $t('books.borrowModal.selectUser') }}</option>
              <option
                v-for="u in filteredBorrowUsers"
                :key="u.id"
                :value="String(u.id)"
              >
                {{ u.username }} ({{ u.centerName || '-' }})
              </option>
            </select>
            <p v-if="borrowUsersLoading" class="borrow-modal-info">{{ $t('books.borrowModal.loadingUsers') }}</p>
            <p v-else-if="!filteredBorrowUsers.length" class="borrow-modal-info">{{ $t('books.borrowModal.noUsers') }}</p>
          </div>

          <p v-if="borrowUsersError" class="borrow-modal-error">{{ borrowUsersError }}</p>
          <p v-if="borrowModalError" class="borrow-modal-error">{{ borrowModalError }}</p>

          <div class="borrow-modal-actions">
            <button class="btn small" type="button" @click="closeBorrowModal">
              {{ $t('books.borrowModal.cancel') }}
            </button>
            <button
              class="btn small"
              type="button"
              @click="confirmBorrowForAdmin"
              :disabled="borrowSubmitting || (borrowMode === 'user' && borrowUsersLoading)"
            >
              {{ borrowSubmitting ? '...' : $t('books.borrowModal.confirm') }}
            </button>
          </div>
        </div>
      </div>
  </div>
</div>
</template>

<style>
/* -------------------------------------------------------
   Animations
------------------------------------------------------- */

.ViewModeFadeIn-enter-active,
.ViewModeFadeIn-leave-active {
  transition: opacity 0.28s ease, transform 0.28s ease;
}

.ViewModeFadeIn-enter-from,
.ViewModeFadeIn-leave-to {
  opacity: 0;
  transform: translateY(8px) scale(0.995);
}

.books-grid .book-card,
.books-list .book-row {
  animation: bookCardIn 0.42s cubic-bezier(0.22, 1, 0.36, 1) both;
  animation-delay: var(--stagger, 0ms);
}

@keyframes bookCardIn {
  from {
    opacity: 0;
    transform: translateY(14px) scale(0.985);
    filter: saturate(0.85);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
    filter: saturate(1);
  }
}

/* -------------------------------------------------------
   Layout
------------------------------------------------------- */
.books-wrapper {
  background: var(--bg);
  color: var(--text);
  padding-top: 16px;
  padding-bottom: max(28px, calc(env(safe-area-inset-bottom) + 18px));
}

.books-container {
  max-width: 1200px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* -------------------------------------------------------
   Header
------------------------------------------------------- */
.books-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-end;
  gap: 12px;
}

.books-title {
  margin: 0;
  font-size: 22px;
  font-weight: 800;
}

.books-subtitle {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--muted);
}

.books-actions .btn {
  padding: 8px 14px;
}

.success-message {
  padding: 10px 14px;
  border-radius: 10px;
  border: 1px solid var(--status-green-border);
  background: var(--status-green-bg);
  color: var(--status-green-text);
  font-weight: 600;
}

/* -------------------------------------------------------
   Shared surfaces + controls
------------------------------------------------------- */
.books-filters {
  display: grid;
  grid-template-columns: 1fr 180px 200px 160px;
  gap: 12px;

  padding: 14px;
  border-radius: 16px;
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  backdrop-filter: blur(10px);
}

/* Inputs + selects (unified) */
.books-filters input,
.books-filters select {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--text);
  font-size: 14px;
  outline: none;
}

.books-filters input::placeholder {
  color: var(--muted);
}

.books-filters input:focus,
.books-filters select:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

/* -------------------------------------------------------
   Select (theme-safe)
------------------------------------------------------- */
.books-filters select {
  cursor: pointer;
  padding-right: 34px;
  appearance: none;
  -webkit-appearance: none;
  -moz-appearance: none;

  /* arrow + separator, but keep base background from variables */
  background-image:
    linear-gradient(45deg, transparent 50%, rgba(199, 210, 254, 0.95) 50%),
    linear-gradient(135deg, rgba(199, 210, 254, 0.95) 50%, transparent 50%),
    linear-gradient(to right, rgba(255,255,255,0.18), rgba(255,255,255,0.18));
  background-position:
    calc(100% - 18px) calc(50% - 3px),
    calc(100% - 13px) calc(50% - 3px),
    calc(100% - 36px) 50%;
  background-size:
    5px 5px,
    5px 5px,
    1px 60%;
  background-repeat: no-repeat;
}

.books-filters select:hover {
  border-color: rgba(124, 92, 255, 0.7);
}

/* Dropdown options (best-effort, OS lahko override-a) */
.books-filters select option,
.borrow-user-picker select option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.books-filters select option:checked {
  background-color: #4f46e5;
  color: #fff;
}

/* Optional: special color for empty option */
.books-filters select option[value=""] {
  background: var(--card-bg-gradient1);
}

/* -------------------------------------------------------
   Display toggle
------------------------------------------------------- */
.display-toggle {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.06);
  width: fit-content;
}

.display-tools {
  display: inline-flex;
  align-items: center;
  gap: 10px;
  width: fit-content;
}

.availability-filter {
  height: 46px;
  min-width: 180px;
  padding: 0 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--text);
  font-size: 14px;
  outline: none;
}

.availability-filter:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.availability-filter option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.view-btn {
  height: 34px;
  width: 34px;
  border-radius: 8px;
  border: 1px solid transparent;
  background: transparent;
  color: var(--muted);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: background 0.2s ease, border-color 0.2s ease, color 0.2s ease;
}

.view-btn:hover {
  background: var(--nav-item-hover);
  color: var(--text);
}

.view-btn.active {
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  border-color: rgba(124, 92, 255, 0.6);
  color: #fff;
  box-shadow: 0 6px 16px rgba(124, 92, 255, 0.25);
}

.view-icon {
  font-size: 16px;
}

@media (max-width: 720px) {
  .display-tools {
    width: 100%;
    flex-direction: column;
    align-items: stretch;
  }

  .display-toggle {
    width: 100%;
    justify-content: center;
  }

  .availability-filter {
    width: 100%;
  }
  .view-btn:nth-child(2) {
    display: none;
  }
}

/* -------------------------------------------------------
   Grid
------------------------------------------------------- */
.books-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 14px;
}

/* -------------------------------------------------------
   List
------------------------------------------------------- */
.books-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.book-row {
  display: flex;
  gap: 12px;
  align-items: stretch;
  padding: 12px;
  border-radius: 16px;
  border: 1px solid var(--card-border);
  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: 0 14px 32px rgba(0, 0, 0, 0.32), inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

.book-row--borrowed {
  background: linear-gradient(180deg, var(--status-yellow-bg), var(--status-yellow-bg));
  border-color: var(--status-yellow-border);
  box-shadow: 0 14px 32px rgba(245, 158, 11, 0.2), inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

.book-row-cover {
  width: 88px;
  height: 120px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: linear-gradient(145deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  display: grid;
  place-items: center;
  overflow: hidden;
  flex: 0 0 auto;
}

.book-row-cover--borrowed {
  border-color: var(--status-yellow-border);
  background: linear-gradient(145deg, var(--status-yellow-bg), var(--status-yellow-bg));
}

.book-row-cover img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.book-row-body {
  flex: 1;
  min-width: 0;
}

.book-row-title {
  font-size: 15px;
  font-weight: 800;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.book-row-sub {
  margin-top: 4px;
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  font-size: 12px;
  color: var(--muted);
  min-width: 0;
}

.book-row-author {
  font-weight: 700;
  display: inline-block;
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.book-row-meta {
  margin-top: 6px;
  font-size: 12px;
  color: var(--muted);
}

.book-row-genres {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.book-row-genres .tag{
  color: var(--text);
}

.book-row-actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
  justify-content: flex-start;
}

@media (max-width: 720px) {
  .book-row {
    flex-direction: column;
  }

  .book-row-actions {
    flex-direction: row;
  }
}

/* -------------------------------------------------------
   Book card
------------------------------------------------------- */
.book-card {
  display: flex;
  flex-direction: column;
  border-radius: 18px;
  padding: 14px;
  border: 1px solid var(--card-border);

  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: 0 18px 40px rgba(0, 0, 0, 0.35), inset 0 1px 0 rgba(255, 255, 255, 0.05);
  transition: transform 0.18s ease, box-shadow 0.18s ease;
}

.book-card--borrowed {
  background: linear-gradient(180deg, var(--status-yellow-bg), var(--status-yellow-bg));
  border-color: var(--status-yellow-border);
  box-shadow: 0 18px 40px rgba(245, 158, 11, 0.22), inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

.book-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 26px 55px rgba(0, 0, 0, 0.45), inset 0 1px 0 rgba(255, 255, 255, 0.06);
}

/* Cover */
.book-cover {
  width: 100%;
  height: 180px;
  border-radius: 14px;
  margin-bottom: 10px;
  border: 1px solid var(--card-border);

  background: linear-gradient(145deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  display: grid;
  place-items: center;
  overflow: hidden;
}

.book-cover--borrowed {
  border-color: var(--status-yellow-border);
  background: linear-gradient(145deg, var(--status-yellow-bg), var(--status-yellow-bg));
}

.book-cover img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.book-cover-fallback-icon {
  font-size: 28px;
  opacity: 0.6;
  color: var(--muted);
}

/* Body */
.book-body {
  flex: 1;
}

.book-title {
  margin: 0;
  font-size: 15px;
  font-weight: 800;
  line-height: 1.25;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.book-author {
  margin: 4px 0 0;
  font-size: 13px;
  color: var(--muted);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

/* Meta */
.book-meta {
  margin-top: 6px;
  display: flex;
  gap: 10px;
  font-size: 12px;
  color: var(--muted);
}

.ordered-note {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--status-yellow-bg);
  border: 1px solid var(--status-yellow-border);
  color: var(--status-yellow-text);
  font-weight: 700;
}

.borrowed-note {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--status-yellow-bg);
  border: 1px solid var(--status-yellow-border);
  color: var(--status-yellow-text);
  font-weight: 700;
}

/* Genres */
.book-genres {
  margin-top: 8px;
  display: flex;
  flex-wrap: nowrap;
  overflow: hidden;
  gap: 6px;
}

.book-genres .tag {
  white-space: nowrap;
  padding: 4px 8px;
  border-radius: 999px;
  max-width: 140px;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  background: rgba(99, 102, 241, 0.16);
  border: 1px solid rgba(99, 102, 241, 0.35);
  color: var(--text);
}

/* Actions */
.book-actions {
  margin-top: 10px;
  display: flex;
  gap: 8px;
}

.book-actions .btn.small {
  flex: 1;
  padding: 6px 10px;
  font-size: 13px;
}

/* -------------------------------------------------------
   Empty
------------------------------------------------------- */
.books-empty {
  padding: 16px;
  border-radius: 16px;
  border: 1px dashed var(--card-border);
  background: rgba(255, 255, 255, 0.05);
  color: var(--muted);
  text-align: center;
  font-size: 13px;
}

/* -------------------------------------------------------
   Pagination
------------------------------------------------------- */
.books-pagination {
  margin-top: 10px;
  margin-bottom: 8px;
  display: flex;
  align-items: center;
  justify-content: flex-start;
  gap: 12px;
  white-space: nowrap;
  width: 100%;
}

.books-pagination span {
  display: inline-block;
  font-size: 13px;
  color: var(--muted);
}

.pager-spacer {
  flex: 1;
}

.selectSmall {
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
  padding: 6px 10px;
  font-size: 13px;
}

.selectSmall option {
  background: var(--option-bg, var(--bg));
  color: var(--text);
}

/* Base .btn.small (define once!) */
.btn.small {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 6px 14px;
  font-size: 14px;
  font-weight: 700;
  border-radius: 6px;
  background: var(--card-bg);
  color: var(--text);
  border: 1px solid var(--card-border);
  text-decoration: none;
  cursor: pointer;
  transition: background 0.2s ease, border-color 0.2s ease, opacity 0.2s ease;
}

.btn.small:hover {
  background: var(--nav-item-hover);
}

/* Pagination variant */
.books-pagination .btn.small {
  min-width: 34px;
  background: linear-gradient(180deg, rgba(99,102,241,0.25), rgba(99,102,241,0.15));
  border-color: rgba(99,102,241,0.45);
  color: var(--issn-meta-value);
}

.books-pagination .btn.small:hover {
  background: linear-gradient(180deg, rgba(99,102,241,0.35), rgba(99,102,241,0.2));
}

.books-pagination .btn.small:focus-visible {
  outline: none;
  border-color: var(--nav-item-active-border);
  box-shadow: inset 0 0 0 2px var(--focus-ring);
}

.books-pagination .btn.small:disabled {
  opacity: 0.45;
  cursor: not-allowed;
  background: rgba(255,255,255,0.08);
  border-color: var(--card-border);
}

/* -------------------------------------------------------
   Borrow Modal
------------------------------------------------------- */
.borrow-modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(10, 14, 28, 0.6);
  display: grid;
  place-items: center;
  z-index: 1200;
  padding: 14px;
}

.borrow-modal {
  width: min(560px, 100%);
  border: 1px solid var(--card-border);
  border-radius: 14px;
  padding: 16px;
  background: var(--modal-bg);
  box-shadow: var(--shadow-elev-2, 0 16px 40px rgba(0, 0, 0, 0.28));
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.borrow-modal-title {
  margin: 0;
  font-size: 18px;
  font-weight: 800;
}

.borrow-modal-subtitle {
  margin: 0;
  color: var(--muted);
  font-size: 14px;
}

.borrow-mode-options {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.borrow-mode-option {
  display: flex;
  gap: 8px;
  align-items: center;
  font-size: 14px;
}

.borrow-user-picker {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.borrow-user-search,
.borrow-user-select {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--text);
  font-size: 14px;
  outline: none;
}

.borrow-user-select {
  min-height: 38px;
}

.borrow-modal-info {
  margin: 0;
  font-size: 13px;
  color: var(--muted);
}

.borrow-modal-error {
  margin: 0;
  color: var(--status-red-text);
  font-size: 13px;
  font-weight: 700;
}

.borrow-modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

/* -------------------------------------------------------
   Fade
------------------------------------------------------- */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.5s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

@media (prefers-reduced-motion: reduce) {
  .books-grid .book-card,
  .books-list .book-row {
    animation: none;
  }

  .ViewModeFadeIn-enter-active,
  .ViewModeFadeIn-leave-active {
    transition: none;
  }
}

/* -------------------------------------------------------
   Responsive
------------------------------------------------------- */
@media (max-width: 900px) {
  .books-filters {
    grid-template-columns: 1fr;
  }
}
</style>









