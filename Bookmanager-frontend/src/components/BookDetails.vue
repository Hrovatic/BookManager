<script setup>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { apiUrl, resolveAssetUrl } from '@/utils/api'

const BOOK_API = apiUrl('/api/Book')
const BORROWING_API = apiUrl('/api/Borrowing')
const INVENTORY_API = apiUrl('/api/Inventory')

const route = useRoute()
const router = useRouter()
const { t } = useI18n()

const book = ref(null)
const relatedBooks = ref([])
const activeBorrowing = ref(null)
const addCopyLoading = ref(false)
const newInventoryNumber = ref('')
const newLocation = ref("")

const availability = ref(null) // { bookId, total, available, isAvailable }
const copies = ref([]) // CopyDto[]
const primaryCopy = computed(() => {
  const list = copies.value || []
  if (!list.length) return null

  const free = list.find(c => c.status === true)
  if (free) return free

  return [...list].sort((a, b) => Number(a.inventoryNumber) - Number(b.inventoryNumber))[0]
})

const extractCategoryCode = (raw) => {
  const text = String(raw ?? '').trim()
  if (!text) return ''
  const match = text.match(/\b\d{1,2}\.\d{2}\.\d{2}\b/)
  return match ? match[0] : text
}

const inventoryCategoryCodeText = computed(() => {
  const raw =
    primaryCopy.value?.categoryCode ??
    primaryCopy.value?.CategoryCode ??
    book.value?.categoryCode ??
    book.value?.CategoryCode ??
    primaryCopy.value?.location ??
    primaryCopy.value?.Location ??
    ''

  const code = extractCategoryCode(raw)
  return code || '—'
})

const toast = ref({
  show: false,
  message: '',
  type: 'success' // success | error | info
})

function showToast(message, type = 'success', duration = 2500) {
  toast.value.message = message
  toast.value.type = type
  toast.value.show = true

  setTimeout(() => {
    toast.value.show = false
  }, duration)
}

const categoryName = computed(() => {
  const code = String(inventoryCategoryCodeText.value ?? '').trim()
  const major = code.split('.')[0]

  const map ={
    '1': 'Naravoslovje',
    '2': 'Tehnika',
    '4': 'Biotehnika',
    '5': 'Družboslovje',
    '6': 'Humanistika',
    '7': 'Interdisciplinarne',
    '10': 'Serijske publikacije',
    '9': 'Drugo',
  }

  return map[major] ?? 'Drugo'
})

const loading = ref(false)
const error = ref('')

const borrowLoading = ref(false)
const borrowError = ref('')
const notifyLoading = ref(false)
const deleteLoading = ref(false)

function formatDateSL(dateStr) {
  if (!dateStr) return ''
  return new Date(dateStr).toLocaleDateString('sl-SI')
}

const normalizeIsbn = (isbn) => String(isbn ?? '').replace(/-/g, '')

const uiRole = computed(() => (localStorage.getItem('role') === 'admin' ? 'admin' : 'user'))

const primaryCopyReason = computed(() => {
  const list = copies.value || []
  if (!list.length) return 'Ni kopij v inventarju.'

  const shown = primaryCopy.value
  if (!shown) return 'Ni kopij v inventarju.'

  const anyFree = list.some(c => c.status === true)

  if (anyFree && shown.status === true) {
    return 'Prikazana je prva prosta kopija (najbolj uporabna za izposojo).'
  }

  return 'Prikazana je kopija z najnižjo inventarno številko (ker ni prostih kopij).'
})

// ŽANRI
const genres = computed(() => {
  const g = book.value?.genre ?? book.value?.Genre
  return Array.isArray(g) ? g : []
})

const upperText = (text) =>
  String(text ?? '').toUpperCase()

const capitalizeGenre = (g) => {
  const text = String(g ?? '').trim()
  if (!text) return ''
  const lower = text.toLocaleLowerCase('sl-SI')
  return lower.replace(/(^|[\s/.,-])(\p{L})/gu, (match, sep, ch) =>
    `${sep}${ch.toLocaleUpperCase('sl-SI')}`
  )
}

const identifierType = computed(() =>
  Number(book.value?.identifierType ?? book.value?.IdentifierType ?? 1)
)

const issnText = computed(() => book.value?.issn ?? book.value?.ISSN ?? '')
const isbnText = computed(() => book.value?.isbn ?? book.value?.ISBN ?? '')

const isIssnBook = computed(() => {
  if (identifierType.value === 2) return true
  return !isbnText.value && !!issnText.value
})

const identifierLabel = computed(() =>
  isIssnBook.value ? 'ISSN' : t('bookdetails.details.label_isbn')
)

const identifierText = computed(() => {
  if (isIssnBook.value) return issnText.value || '—'
  return normalizeIsbn(isbnText.value) || '—'
})

const primaryIssnNumber = computed(() => primaryCopy.value?.issnNumber ?? '—')
const primaryIssnVolume = computed(() => primaryCopy.value?.issnVolume ?? '—')

// UI state now depends on availability
const uiState = computed(() => {
  if (!availability.value) return 'unavailable'
  if (availability.value.available > 0) return 'available'
  // če total > 0 in available == 0 -> borrowed
  if (availability.value.total > 0 && availability.value.available === 0) return 'borrowed'
  return 'unavailable'
})

const canDeleteUnavailableBook = computed(() =>
  uiRole.value === 'admin' &&
  Number(availability.value?.total ?? -1) === 0
)

const dueDateText = computed(() =>
  formatDateSL(activeBorrowing.value?.dueDate ?? activeBorrowing.value?.DueDate)
)

function getAuthHeaders() {
  const token = localStorage.getItem('token')
  return token ? { Authorization: `Bearer ${token}` } : {}
}

function getCurrentUserId() {
  // najprej poskusi iz user objecta
  const u = localStorage.getItem('user')
  if (u) {
    try {
      const parsed = JSON.parse(u)
      if (parsed?.id != null) return String(parsed.id)
      if (parsed?.Id != null) return String(parsed.Id)
    } catch {}
  }
  // fallback (če imaš kje userId posebej)
  const uid = localStorage.getItem('userId')
  return uid ? String(uid) : null
}

async function loadAvailability(bookId) {
  const res = await axios.get(`${INVENTORY_API}/availability`, {
    params: { bookId },
    headers: getAuthHeaders()
  })
  availability.value = res.data
}

async function loadCopies(bookId) {
  const token = localStorage.getItem('token')
  const res = await axios.get(`${INVENTORY_API}/allcopies`, {
    params: { bookId },
    headers: token ? { Authorization: `Bearer ${token}` } : {}
  })

  copies.value = Array.isArray(res.data) ? res.data : []
}

async function addCopy() {
  addCopyLoading.value = true
  try {
    const token = localStorage.getItem('token')
    const bookId = Number(route.params.id)

    await axios.post(`${INVENTORY_API}/copy`, {
      bookId,
      inventoryNumber: newInventoryNumber.value?.trim() || null,
      location: newLocation.value.trim() || null
    }, {
      headers: token ? { Authorization: `Bearer ${token}` } : {}
    })

    newInventoryNumber.value = ''
    newLocation.value = ''

    // refresh inventory + availability
    await refreshAll()
    showToast('Kopija uspešno dodana.')
  } catch (e) {
    console.error(e)
    showToast('Dodajanje kopije ni uspelo.', 'error')
  } finally {
    addCopyLoading.value = false
  }
}

async function loadBookDetails(bookId) {
  // 1) knjiga
  const resBook = await axios.get(`${BOOK_API}/${bookId}`, { headers: getAuthHeaders() })
  book.value = resBook.data


  // 2) related
  relatedBooks.value = []
  if (genres.value.length) {
    const params = new URLSearchParams()
    genres.value.forEach(g => params.append('genre', g))
    params.append('excludeId', String(bookId))
    params.append('take', '3')

    const resRelated = await axios.get(`${BOOK_API}/related?${params.toString()}`, { headers: getAuthHeaders() })
    relatedBooks.value = resRelated.data ?? []
  }
}

async function loadActiveBorrowingIfNeeded(bookId) {
  activeBorrowing.value = null
  if (!availability.value) return
  if (availability.value.total > 0 && availability.value.available === 0) {
    try {
      const resBorrow = await axios.get(`${BORROWING_API}/active-by-book/${bookId}`, { headers: getAuthHeaders() })
      activeBorrowing.value = resBorrow.data
    } catch {
      activeBorrowing.value = null
    }
  }
}

async function refreshAll() {
  loading.value = true
  error.value = ''
  borrowError.value = ''

  try {
    const bookId = Number(route.params.id)
    if (!bookId) throw new Error('Invalid id')

    await loadAvailability(bookId)
    await loadBookDetails(bookId)
    await loadActiveBorrowingIfNeeded(bookId)
    await loadCopies(bookId)
  } catch (e) {
    console.error(e)
    error.value = 'Napaka pri nalaganju knjige.'
  } finally {
    loading.value = false
  }
}

async function borrowBook() {
  borrowError.value = ''
  borrowLoading.value = true

  try {
    const userId = getCurrentUserId()
    if (!userId) {
      borrowError.value = 'Manjka userId (prijava).'
      return
    }

    const bookId = Number(route.params.id)
    if (!bookId) return

    if (availability.value && availability.value.available === 0) {
      borrowError.value = 'Ni prostih kopij.'
      return
    }

    await axios.post(`${BORROWING_API}/borrow`, {
      bookId,
      userId
    }, { headers: getAuthHeaders() })
    await refreshAll()
  } catch (e) {
    console.error(e)
    if (e?.response?.status === 409) borrowError.value = e.response.data || 'Ni prostih kopij.'
    else borrowError.value = 'Izposoja ni uspela.'
  } finally {
    borrowLoading.value = false
  }
}

async function notifyWhenReturned() {
  const bookId = Number(route.params.id)
  if (!bookId) return

  if (availability.value?.available > 0) {
    showToast('Knjiga je že na voljo.', 'info')
    return
  }

  notifyLoading.value = true
  try {
    const res = await axios.post(
      `${BORROWING_API}/notify-when-available`,
      { bookId },
      { headers: getAuthHeaders() }
    )

    const msg = String(res?.data?.message ?? '')
    if (msg.toLowerCase().includes('already')) {
      showToast('Obvestilo je že nastavljeno.', 'info')
    } else {
      showToast('Ko bo knjiga vrnjena, dobiš email obvestilo.', 'success')
    }
  } catch (e) {
    console.error(e)
    const backendMsg =
      typeof e?.response?.data === 'string'
        ? e.response.data
        : e?.response?.data?.message
    showToast(backendMsg || 'Nastavitev obvestila ni uspela.', 'error')
  } finally {
    notifyLoading.value = false
  }
}

function editBook() {
  const id = Number(route.params.id)
  if (!id) return
  router.push(`/editbook/${id}`)
}

async function deleteBook() {
  if (deleteLoading.value) return

  const id = Number(route.params.id)
  if (!id) return

  const confirmed = window.confirm(t('bookdetails.actions.delete_confirm'))
  if (!confirmed) return

  deleteLoading.value = true
  try {
    await axios.delete(`${BOOK_API}/delete/${id}`, { headers: getAuthHeaders() })
    showToast(t('bookdetails.actions.delete_success'))
    setTimeout(() => router.push('/showbooks'), 450)
  } catch (e) {
    console.error(e)
    const backendMsg =
      typeof e?.response?.data === 'string'
        ? e.response.data
        : e?.response?.data?.message
    showToast(backendMsg || t('bookdetails.actions.delete_error'), 'error', 3200)
  } finally {
    deleteLoading.value = false
  }
}

function goBack() {
  router.push('/showbooks')
}

onMounted(refreshAll)

watch(() => route.params.id, () => {
  refreshAll()
})
</script>

<template>
  <div class="bookdetails" :data-state="uiState" :data-role="uiRole">
    <div v-if="loading" class="bd-statebox empty-state">
      {{ $t('bookdetails.loading') }}
    </div>

    <div v-else-if="error" class="bd-statebox bd-statebox--error empty-state">
      {{ $t('bookdetails.error') }}: {{ error }}
      <button class="bd-btn bd-btn--outline" @click="goBack">
        {{ $t('bookdetails.back') }}
      </button>
    </div>

    <template v-else-if="book">
      <header class="bd-top">
        <div class="bd-top-left">
          <router-link to="/showbooks" class="back-btn">
            <i class="pi pi-arrow-left" aria-hidden="true"></i>
            {{ $t('bookdetails.back')}}
          </router-link>
          <div class="bd-status">
            <span v-if="uiState === 'available'" class="bd-badge bd-badge--available">
              {{ $t('bookdetails.status.available') }}
            </span>
            <span v-else-if="uiState === 'borrowed'" class="bd-badge bd-badge--borrowed">
              {{ $t('bookdetails.status.borrowed') }}
            </span>
            <span v-else class="bd-badge bd-badge--unavailable">
              {{ $t('bookdetails.status.unavailable') }}
            </span>
          </div>
        </div>

        <div class="bd-status-text">
          <h1 class="bd-title section-title">
            {{ book.title ?? book.Title ?? '—' }}
          </h1>

          <p class="bd-subtitle">
            {{ $t('bookdetails.details.label_author') }}:
            <span class="bd-strong bd-author">{{ book.author ?? book.Author ?? '—' }}</span>

            <span class="bd-dot">•</span>

            {{ $t('bookdetails.details.label_language') }}:
            {{ upperText(book.language ?? book.Language ?? '—') }}

            <span class="bd-dot">•</span>

            {{ identifierLabel }}:
            <span class="bd-mono">{{ identifierText }}</span>
          </p>

          <p class="bd-availability">
            <span class="bd-availability__label">
              {{ $t('bookdetails.status.label') }}
            </span>

            <span class="bd-availability__value bd-availability__value--available">
              {{ $t('bookdetails.status.available_long') }}
            </span>

            <span class="bd-availability__value bd-availability__value--borrowed">
              {{ $t('bookdetails.status.borrowed_long') }}

              <template v-if="dueDateText">
                {{ $t('bookdetails.status.borrowed_due') }}
                <span class="bd-strong">{{ dueDateText }}</span>
              </template>

              <template v-else>
                {{ $t('bookdetails.status.borrowed_unknown') }}
              </template>
            </span>

            <span class="bd-availability__value bd-availability__value--unavailable">
              {{ $t('bookdetails.status.unavailable_long') }}
            </span>
          </p>
        </div>

        <div class="bd-actions">
          <button
            class="bd-btn bd-btn--primary bd-action--borrow"
            @click="borrowBook"
            :disabled="borrowLoading || (availability && availability.available === 0)"
          >
            {{ borrowLoading ? '...' : $t('bookdetails.actions.borrow') }}
          </button>

          <p v-if="borrowError" style="margin:8px 0 0; color:var(--status-red-text); font-weight:800;">
            {{ borrowError }}
          </p>

          <button
            class="bd-btn bd-btn--ghost bd-action--notify"
            @click="notifyWhenReturned"
            :disabled="notifyLoading"
          >
            {{ notifyLoading ? '...' : $t('bookdetails.actions.notify') }}
          </button>

          <button class="bd-btn bd-btn--outline bd-action--edit" type="button" @click="editBook">
            {{ $t('bookdetails.actions.edit') }}
          </button>

          <button
            v-if="canDeleteUnavailableBook"
            class="bd-btn bd-btn--danger"
            type="button"
            @click="deleteBook"
            :disabled="deleteLoading"
          >
            {{ deleteLoading ? '...' : $t('bookdetails.actions.delete') }}
          </button>
        </div>
      </header>

      <main class="bd-main">
        <section class="bd-left">
          <div class="bd-covercard surface">
            <a class="bd-coverlink" href="#cover-zoom" :aria-label="$t('bookdetails.cover.zoom_label')">
              <img
                v-if="(book.coverUrl ?? book.coverURL)"
                class="bd-cover"
                :src="resolveAssetUrl(book.coverUrl ?? book.coverURL)"
                :alt="`${$t('bookdetails.cover.zoom_alt')} ${(book.title ?? book.Title ?? '')}`"
              />
              <div v-else class="bd-cover bd-cover--fallback"></div>

              <span class="bd-coverhint">{{ $t('bookdetails.cover.zoom_hint') }}</span>
            </a>
          </div>

          <aside class="bd-inventory surface">
            <h2 class="bd-section-title section-title">{{ $t('bookdetails.inventory.title') }}</h2>

            <div v-if="uiRole === 'admin'" style="display:flex; gap:10px; margin-bottom:10px; flex-wrap:wrap;">
              <input
                v-model="newInventoryNumber"
                class="bd-btn"
                style="min-width:220px;"
                type="text"
                placeholder="Inventarna št. (opcijsko)"
              />
              <button
                class="bd-btn bd-btn--primary"
                type="button"
                @click="addCopy"
                :disabled="addCopyLoading"
              >
                {{ addCopyLoading ? '...' : '+ Dodaj kopijo' }}
              </button>
            </div>

            <div class="bd-invgrid">
              <div class="bd-invitem">
                <div class="bd-invlabel">{{ $t('bookdetails.inventory.number') }}</div>
                <div class="bd-invvalue bd-mono tooltip">
                    <i class="pi pi-info-circle info-icon" aria-hidden="true"></i>
                      {{ primaryCopy?.displayInventoryNumber ?? '—' }}
                    <span class="tooltip-text">
                      {{ primaryCopyReason }}
                    </span>
                  </div>
              </div>

              <div class="bd-invitem">
                <div class="bd-invlabel">{{ $t('bookdetails.inventory.location') }}</div>

                <div class="bd-invvalue bd-mono tooltip">
                  <i class="pi pi-info-circle info-icon" aria-hidden="true"></i>
                  {{ categoryName }}
                  <span class="tooltip-text">
                    {{ inventoryCategoryCodeText }}
                  </span>
                </div>
              </div>

              <div class="bd-invitem">
                <div class="bd-invlabel">{{ $t('bookdetails.inventory.copies') }}</div>
                <div class="bd-invvalue">{{ availability ? availability.total : '—' }}</div>
              </div>

              <div class="bd-invitem">
                <div class="bd-invlabel">{{ $t('bookdetails.inventory.free') }}</div>
                <div class="bd-invvalue">{{ availability ? availability.available : '—' }}</div>
              </div>

              <div class="bd-invitem">
                <div class="bd-invlabel">{{ $t('bookdetails.inventory.condition') }}</div>
              <div class="bd-invvalue">
                <span
                  class="bd-pill-message"
                  :class="{
                    'bd-pill--available': availability && availability.available > 0,
                    'bd-pill--borrowed': availability && availability.total > 0 && availability.available === 0,
                    'bd-pill--unavailable': availability && availability.total === 0
                  }"
                >
                  <template v-if="!availability">—</template>
                  <template v-else-if="availability.available > 0">
                    {{ $t('bookdetails.status.available') }} ({{ availability.available }})
                  </template>
                  <template v-else-if="availability.total > 0">
                    {{ $t('bookdetails.status.borrowed')}}
                  </template>
                  <template v-else>
                    {{ $t('bookdetails.status.unavailable') }}
                  </template>
                </span>
              </div>
              </div>
            </div>
          </aside>
        </section>

        <section class="bd-right">
          <div class="bd-card surface">
            <h2 class="bd-section-title section-title">{{ $t('bookdetails.details.title') }}</h2>

            <div class="bd-meta">
              <div class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_title') }}</span>
                <span class="bd-metavalue">{{ book.title ?? book.Title ?? '—' }}</span>
              </div>

              <div class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_author') }}</span>
                <span class="bd-metavalue">{{ book.author ?? book.Author ?? '—' }}</span>
              </div>

              <div class="bd-metaitem">
                <span class="bd-metalabel">{{ identifierLabel }}</span>
                <span class="bd-metavalue bd-mono">{{ identifierText }}</span>
              </div>

              <div v-if="isIssnBook" class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_issn_number') }}</span>
                <span class="bd-metavalue bd-mono">{{ primaryIssnNumber }}</span>
              </div>

              <div v-if="isIssnBook" class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_issn_issue') }}</span>
                <span class="bd-metavalue bd-mono">{{ primaryIssnVolume }}</span>
              </div>

              <div class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_publisher') }}</span>
                <span class="bd-metavalue">{{ book.publisher ?? book.Publisher ?? '—' }}</span>
              </div>

              <div class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_year') }}</span>
                <span class="bd-metavalue">{{ book.yearPublished ?? book.YearPublished ?? '—' }}</span>
              </div>

              <div class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_language') }}</span>
                <span class="bd-metavalue">{{ upperText(book.language ?? book.Language ?? '—') }}</span>
              </div>

              <div class="bd-metaitem">
                <span class="bd-metalabel">{{ $t('bookdetails.details.label_genre') }}</span>
                <span class="bd-metavalue">
                  <span v-if="!genres.length">—</span>
                  <span v-else>
                    <span v-for="(g, i) in genres" :key="i" class="bd-pill">{{ capitalizeGenre(g) }}</span>
                  </span>
                </span>
              </div>
            </div>
          </div>
          <div class="bd-card surface">
            <h2 class="bd-section-title section-title">{{ $t('bookdetails.related.title') }}</h2>

            <div class="bd-related">
              <article
                class="bd-relateditem"
                v-for="rb in relatedBooks"
                :key="rb.id"
                @click="router.push(`/book/${rb.id}`)"
              >
                <div class="bd-relatedcover"></div>
                <div class="bd-relatedmeta">
                  <div class="bd-relatedtitle">{{ rb.title }}</div>
                  <div class="bd-relatedsub">{{ rb.author }}</div>
                </div>
              </article>

              <div v-if="!relatedBooks.length" class="bd-relatedempty empty-state">
                {{ $t('bookdetails.related.empty') }}
              </div>
            </div>
          </div>
        </section>
      </main>

      <div id="cover-zoom" class="bd-zoom">
        <a class="bd-zoom__backdrop" href="#" :aria-label="$t('bookdetails.cover.zoom_close')"></a>

        <div class="bd-zoom__panel" role="dialog" aria-modal="true">
          <a class="bd-zoom__close" href="#" :aria-label="$t('bookdetails.cover.zoom_close')">
            <i class="pi pi-times" aria-hidden="true"></i>
          </a>

          <img
            v-if="(book.coverUrl ?? book.coverURL)"
            class="bd-zoom__img"
            :src="resolveAssetUrl(book.coverUrl ?? book.coverURL)"
            :alt="`${$t('bookdetails.cover.zoom_alt')} ${(book.title ?? book.Title ?? '')}`"
          />

          <div v-else class="bd-zoom__img bd-zoom__img--fallback"></div>
        </div>
      </div>
    </template>
  </div>
  <!-- FLOATING TOAST -->
  <transition name="toast-fade">
    <div
      v-if="toast.show"
      class="toast"
      :class="`toast--${toast.type}`"
    >
      {{ toast.message }}
    </div>
  </transition>
</template>

<style scoped>
.bookdetails {
  --bd-radius: 16px;
  --bd-radius-sm: 12px;
  --bd-border: var(--card-border);
  --bd-surface: var(--card-bg);
  --bd-surface-2: var(--select-bg, var(--card-bg));
  --bd-shadow: 0 14px 34px rgba(0,0,0,0.22);

  padding: 16px;
  background: var(--bg);
  color: var(--text);
}

/* Top bar */
.bd-top {
  max-width: 1200px;
  margin: 0 auto 14px;
  display: grid;
  grid-template-columns: auto 1fr auto;
  align-items: start;
  column-gap: 8px;
  row-gap: 6px;
  padding: 14px;
  border-radius: var(--bd-radius);
  background: var(--bd-surface);
  border: 1px solid var(--bd-border);
  backdrop-filter: blur(10px);
}

.bd-top-left {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 10px;
  min-width: 0;
}

.bd-status {
  display: flex;
  align-items: center;
  gap: 12px;
}

.back-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border-radius: 10px;
  border: 1px solid var(--bd-border);
  background: var(--bd-surface-2);
  color: var(--text);
  text-decoration: none;
  font-weight: 700;
  font-size: 13px;
}

.back-btn .pi {
  font-size: 12px;
}

.back-btn:hover {
  border-color: var(--nav-item-active-border);
  background: var(--nav-item-hover);
}

.bd-title {
  margin: 0;
  font-size: 22px;
  font-weight: 900;
  letter-spacing: 0.2px;
}

.bd-status-text {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  text-align: left;
}

.bd-subtitle {
  margin: 6px 0 0;
  color: var(--muted);
  font-size: 13px;
}

.bd-dot {
  opacity: 0.6;
  margin: 0 6px;
}

.bd-strong { font-weight: 800; }
.bd-mono { font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", monospace; }

/* Badges (only one is shown via data-state rules below) */
.bd-badge {
  display: none;
  padding: 6px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 900;
  border: 1px solid transparent;
  white-space: nowrap;
  margin-top: 2px;
}

.bd-badge--available {
  background: var(--status-green-bg);
  border-color: var(--status-green-border);
  color: var(--status-green-text);
}

.bd-badge--borrowed {
  background: var(--status-yellow-bg);
  border-color: var(--status-yellow-border);
  color: var(--status-yellow-text);
}

.bd-badge--unavailable {
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
  color: var(--status-red-text);
}

/* Availability text */
.bd-availability {
  margin: 10px 0 0;
  font-size: 13px;
  color: var(--muted);
}

.bd-availability__label {
  font-weight: 800;
  color: var(--text);
  opacity: 0.9;
  margin-right: 6px;
}

.bd-availability__value { display: none; }
.bd-availability__value--available { color: var(--status-green-text); }
.bd-availability__value--borrowed { color: var(--status-yellow-text); }
.bd-availability__value--unavailable { color: var(--status-red-text); }

/* Actions (only shown via state/role rules) */
.bd-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  justify-content: flex-end;
  align-items: center;
  justify-self: end;
  align-self: start;
}

.bd-statebox {
  max-width: 1200px;
  margin: 0 auto 14px;
  padding: 14px;
  border-radius: var(--bd-radius);
  background: var(--bd-surface);
  border: 1px solid var(--bd-border);
}

.inline-success{
  margin-top: 10px;
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--status-green-border);
  background: var(--status-green-bg);
  color: var(--status-green-text);
  font-weight: 700;
}

.bd-statebox--error {
  border-color: var(--status-red-border);
}


.bd-btn {
  padding: 10px 14px;
  border-radius: 12px;
  border: 1px solid var(--bd-border);
  background: var(--bd-surface-2);
  color: var(--text);
  font-weight: 800;
  cursor: pointer;
  transition: transform 0.15s ease, background 0.15s ease, border-color 0.15s ease, opacity 0.15s ease;
}

.bd-btn:hover {
  background: var(--nav-item-hover);
  border-color: var(--nav-item-active-border);
  transform: translateY(-1px);
}

.bd-btn--primary {
  background: #7c5cff;
  border-color: #7c5cff;
  color: #fff;
}

.bd-btn--primary:hover {
  background: #6b4ef5;
  border-color: #6b4ef5;
}

.bd-btn--outline {
  background: transparent;
}

.bd-btn--ghost {
  background: transparent;
  border-color: transparent;
}

.bd-btn--ghost:hover {
  border-color: var(--bd-border);
}

.bd-btn--danger {
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
  color: var(--status-red-text);
}

.bd-btn--danger:hover {
  background: var(--status-red-bg);
  border-color: var(--status-red-border);
  filter: brightness(0.95);
}

/* Main layout */
.bd-main {
  max-width: 1200px;
  margin: 0 auto;
  display: grid;
  grid-template-columns: 340px 1fr;
  gap: 14px;
}

/* Left */
.bd-left {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.bd-covercard,
.bd-inventory,
.bd-card {
  padding: 14px;
  border-radius: var(--bd-radius);
  background: var(--bd-surface);
  border: 1px solid var(--bd-border);
  box-shadow: var(--bd-shadow);
}

.bd-coverlink {
  display: block;
  position: relative;
  border-radius: 14px;
  overflow: hidden;
  border: 1px solid var(--bd-border);
  background: rgba(255,255,255,0.03);
}

.bd-cover {
  width: 100%;
  height: 420px;
  object-fit: cover;
  display: block;
}

.bd-cover--fallback {
  height: 420px;
  background: rgba(255,255,255,0.04);
}


.bd-coverhint {
  position: absolute;
  left: 10px;
  bottom: 10px;
  padding: 6px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 800;
  background: rgba(0,0,0,0.45);
  color: #fff;
  border: 1px solid rgba(255,255,255,0.18);
}

/* Chips */
.bd-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 12px;
}

.bd-chip {
  padding: 6px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 800;
  background: rgba(99, 102, 241, 0.14);
  border: 1px solid rgba(99, 102, 241, 0.32);
  color: var(--text);
}

.bd-chip--muted {
  background: rgba(255,255,255,0.06);
  border-color: var(--bd-border);
  color: var(--muted);
}

/* Inventory */
.bd-section-title {
  margin: 0 0 12px;
  font-size: 13px;
  font-weight: 900;
  color: var(--muted);
  letter-spacing: 0.2px;
}

.bd-invgrid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0,1fr));
  gap: 10px 12px;
}

.bd-invitem {
  padding: 10px;
  border-radius: 12px;
  background: rgba(255,255,255,0.04);
  border: 1px solid var(--bd-border);
}

.bd-invlabel {
  font-size: 11px;
  color: var(--muted);
  font-weight: 800;
}

.bd-invvalue {
  margin-top: 6px;
  font-size: 13px;
  font-weight: 900;
}

.bd-pill {
  display: inline-flex;
  align-items: center;
  padding: 5px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 900;
  border: 1px solid var(--bd-border);
  background: rgba(255,255,255,0.06);
}

.bd-pill--warn {
  background: var(--status-yellow-bg);
  border-color: var(--status-yellow-border);
  color: var(--status-yellow-text);
}

.bd-pill-message {
  display: inline-block;
  padding: 4px 10px;
  border-radius: 999px;
  font-size: 12px;
  font-weight: 700;
  line-height: 1;
  white-space: nowrap;
}

/* barve */
.bd-pill--available {
  background: var(--status-green-bg);
  color: var(--status-green-pill-text);
}

.bd-pill--borrowed {
  background: var(--status-yellow-bg);
  color: var(--status-yellow-pill-text);
}

.bd-pill--unavailable {
  background: var(--status-red-bg);
  color: var(--status-red-pill-text);
}


/* Right */
.bd-right {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* Meta grid */
.bd-meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 14px;
  margin-top: 10px;
}

.bd-metaitem {
  padding: 10px;
  border-radius: 12px;
  background: rgba(255,255,255,0.04);
  border: 1px solid var(--bd-border);
}

.bd-metalabel {
  display: block;
  font-size: 11px;
  font-weight: 900;
  color: var(--muted);
}

.bd-metavalue {
  margin-top: 6px;
  display: block;
  font-size: 14px;
  font-weight: 900;
}

.bd-desc {
  margin-top: 12px;
}

.bd-subsection-title {
  margin: 0 0 8px;
  font-size: 12px;
  font-weight: 900;
  color: var(--muted);
}

/* Related */
.bd-related {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 12px;
  margin-top: 10px;
}

.bd-relateditem {
  display: flex;
  gap: 10px;
  padding: 10px;
  border-radius: 14px;
  background: rgba(255,255,255,0.04);
  border: 1px solid var(--bd-border);
  transition: transform 0.15s ease, border-color 0.15s ease, background 0.15s ease;
  cursor: pointer;
}

.bd-relateditem:hover {
  transform: translateY(-2px);
  border-color: var(--nav-item-active-border);
  background: rgba(255,255,255,0.06);
}

.bd-relatedcover {
  width: 54px;
  height: 74px;
  border-radius: 10px;
  border: 1px solid var(--bd-border);
  background: rgba(255,255,255,0.05);
  flex: 0 0 auto;
}

.bd-relatedtitle {
  font-weight: 900;
  font-size: 13px;
}

.bd-relatedsub {
  margin-top: 4px;
  font-size: 12px;
  color: var(--muted);
}

.bd-relatedchips {
  margin-top: 8px;
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

/* Zoom modal (CSS-only with :target) */
.bd-zoom {
  position: fixed;
  inset: 0;
  display: none;
  z-index: 50;
  overflow-y: auto;
  -webkit-overflow-scrolling: touch;
}

.bd-zoom:target {
  display: grid;
  place-items: center;
  padding: 16px;
}

.bd-zoom__backdrop {
  position: absolute;
  inset: 0;
  background: rgba(0,0,0,0.65);
}

.bd-zoom__panel {
  position: relative;
  z-index: 1;
  width: min(860px, 94vw);
  border-radius: 18px;
  border: 1px solid rgba(255,255,255,0.18);
  background: rgba(10,12,18,0.72);
  backdrop-filter: blur(12px);
  padding: 14px;
  max-height: calc(100dvh - 20px);
  overflow: auto;
  overscroll-behavior: contain;
}

.bd-zoom__close {
  position: sticky;
  top: 0;
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  text-decoration: none;
  color: #fff;
  font-weight: 900;
  padding: 6px 10px;
  border-radius: 10px;
  border: 1px solid rgba(255,255,255,0.18);
  background: rgba(255,255,255,0.08);
}

.bd-zoom__img {
  width: 100%;
  max-height: min(84vh, 980px);
  object-fit: contain;
  display: block;
  border-radius: 12px;
  background: rgba(0,0,0,0.2);
}

.bd-zoom__img--fallback {
  height: 70vh;
  background: rgba(255,255,255,0.04);
  border-radius: 12px;
}

.toast {
  position: fixed;
  bottom: 24px;
  right: 24px;
  padding: 12px 16px;
  border-radius: 12px;
  font-weight: 700;
  font-size: 14px;
  z-index: 9999;
  box-shadow: 0 15px 35px rgba(0,0,0,.35);
  backdrop-filter: blur(6px);
}

/* barve */
.toast--success {
  background: var(--status-green-bg);
  color: var(--status-green-text);
  border: 1px solid var(--status-green-border);
}
.toast--error {
  background: var(--status-red-bg);
  color: var(--status-red-text);
  border: 1px solid var(--status-red-border);
}
.toast--info {
  background: var(--status-info-bg);
  color: var(--status-info-text);
  border: 1px solid var(--status-info-border);
}

/* animacija */
.toast-fade-enter-active,
.toast-fade-leave-active {
  transition: opacity .2s ease, transform .2s ease;
}
.toast-fade-enter-from,
.toast-fade-leave-to {
  opacity: 0;
  transform: translateY(10px);
}

/* Show the right badge + text + actions */
.bookdetails[data-state="available"] .bd-badge--available { display: inline-flex; }
.bookdetails[data-state="borrowed"] .bd-badge--borrowed { display: inline-flex; }
.bookdetails[data-state="unavailable"] .bd-badge--unavailable { display: inline-flex; }

.bookdetails[data-state="available"] .bd-availability__value--available { display: inline; }
.bookdetails[data-state="borrowed"] .bd-availability__value--borrowed { display: inline; }
.bookdetails[data-state="unavailable"] .bd-availability__value--unavailable { display: inline; }

/* Actions visibility */
.bd-action--borrow,
.bd-action--notify,
.bd-action--edit { display: none; }

.bookdetails[data-state="available"] .bd-action--borrow { display: inline-flex; }
.bookdetails[data-state="borrowed"] .bd-action--notify { display: inline-flex; }
.bookdetails[data-role="admin"] .bd-action--edit { display: inline-flex; }

/* Inventory only for admin */
.bookdetails[data-role="admin"] .bd-inventory { display: block; }
.bookdetails:not([data-role="admin"]) .bd-inventory { display: none; }

.tooltip {
  position: relative;
  cursor: help;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  max-width: 100%;
}

.tooltip-text {
  visibility: hidden;
  opacity: 0;
  position: absolute;
  bottom: calc(100% + 10px);

  /* privzeto centrirano */
  left: 50%;
  transform: translateX(-50%);

  background: rgba(17, 24, 39, 0.95);
  color: #fff;
  padding: 8px 10px;
  border-radius: 8px;
  font-size: 12px;
  line-height: 1.4;

  /* ključni del za offscreen */
  min-width: 220px;
  max-width: min(90vw, 320px);
  white-space: normal;
  overflow-wrap: break-word;
  word-break: normal;
  hyphens: auto;

  z-index: 9999;
  transition: opacity 0.15s ease;
  box-shadow: 0 10px 25px rgba(0,0,0,0.25);
  pointer-events: none;
}

.tooltip:hover .tooltip-text {
  visibility: visible;
  opacity: 1;
}

/* puščica */
.tooltip-text::after {
  content: "";
  position: absolute;
  top: 100%;
  left: 50%;
  transform: translateX(-50%);
  border-width: 6px;
  border-style: solid;
  border-color: rgba(17, 24, 39, 0.95) transparent transparent transparent;
}

.info-icon {
  margin-right: 6px;
  font-size: 12px;
  opacity: 0.7;
  flex: 0 0 auto;
}

.bd-inventory select.bd-btn,
.bd-meta select.bd-btn {
  background: var(--select-bg);
  color: var(--text);
  border: 1px solid var(--bd-border);
}

.bd-inventory select.bd-btn option,
.bd-meta select.bd-btn option {
  background: var(--option-bg);
  color: var(--text);
}

@media (max-width: 520px) {
  .tooltip-text {
    left: 0;
    transform: translateX(0);
    min-width: min(260px, 90vw);
  }

  .tooltip-text::after {
    left: 16px; /* puščica ostane približno pri ikoni */
    transform: translateX(0);
  }
}

/* Responsive */
@media (max-width: 980px) {
  .bd-top {
    grid-template-columns: 1fr;
    align-items: stretch;
  }
  .bd-status-text {
    align-items: flex-start;
    text-align: left;
  }
  .bd-actions {
    justify-self: start;
    justify-content: flex-start;
  }
  .bd-main {
    grid-template-columns: 1fr;
  }
  .bd-cover {
    height: 360px;
  }
  .bd-related {
    grid-template-columns: 1fr;
  }
  .bd-meta {
    grid-template-columns: 1fr;
  }
}



@media (max-width: 640px) {
  .bd-top {
    padding: 12px;
  }

  .bd-status { 
    align-items: flex-start;
  }

  .bd-status-text{
    width: 100%;
    min-width: 0;
  }

  .bd-subtitle .bd-author{
    display: inline-block;
    max-width: 42vw; /* prilagodi po želji */
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    vertical-align: bottom;
  }
  .bd-title {
    max-width: 100%;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .bd-actions {
    width: 100%;
  }

  .bd-actions .bd-btn {
    width: 100%;
    justify-content: center;
  }

  .bd-cover,
  .bd-cover--fallback {
    height: 300px;
  }

  .bd-invgrid {
    grid-template-columns: 1fr;
  }

  .bd-metaitem {
    padding: 8px;
  }

  .toast {
    left: 16px;
    right: 16px;
    bottom: 16px;
  }
}</style>


