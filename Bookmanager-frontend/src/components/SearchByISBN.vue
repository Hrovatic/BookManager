<script setup>
import { ref, onMounted, onBeforeUnmount, computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import { apiUrl, resolveAssetUrl } from '@/utils/api'

const inputRef = ref(null)
const code = ref('')
const status = ref('idle')
const book = ref(null)
const searchErrorText = ref('')

const router = useRouter()
const token = localStorage.getItem('token')

const INVENTORY_API = apiUrl('/api/inventory')
const LOOKUP_API = apiUrl('/api/Book/lookup')
const CATEGORIES_API = apiUrl('/api/categories')
const LOOKUP_TIMEOUT_MS = 12000

const isAdmin = computed(() => localStorage.getItem('role') === 'admin')
const authHeaders = computed(() => (token ? { Authorization: `Bearer ${token}` } : {}))

onMounted(() => inputRef.value?.focus())

// --- helpers ---
const normalizeCode = (v) => String(v ?? '').trim().replace(/-/g, '')
const formatGenre = (g) => {
  const text = String(g ?? '').trim()
  if (!text) return ''
  const lower = text.toLocaleLowerCase('sl-SI')
  return lower.replace(/(^|[\s/.,-])(\p{L})/gu, (match, sep, ch) =>
    `${sep}${ch.toLocaleUpperCase('sl-SI')}`
  )
}

const bookTitle = computed(() => book.value?.title ?? book.value?.Title ?? '—')
const bookAuthorText = computed(() => {
  // podpiramo vse variante: authors[], author[], author string, Author string
  const authors =
    book.value?.authors ??
    book.value?.Authors ??
    book.value?.author ??
    book.value?.Author

  if (Array.isArray(authors)) return authors.filter(Boolean).join(', ')
  if (typeof authors === 'string') return authors
  return ''
})

const displayGenres = computed(() => {
  const g = book.value?.genre ?? book.value?.Genre
  return Array.isArray(g) ? g.map(formatGenre).filter(Boolean) : []
})

// --- Add copy modal state ---
const showAddCopyModal = ref(false)
const copyInvNumber = ref('')
const copyPrice = ref('')
const issnVolume = ref('')
const issnNumber = ref('')
const addCopyLoading = ref(false)
const addCopyError = ref('')
const addCopySuccess = ref('')
const nextInvLoading = ref(false)
const coverFile = ref(null)
const coverPreviewUrl = ref('')
const coverInput = ref(null)
const coverPreviewSrc = computed(() => coverPreviewUrl.value || '')

// Location (same logic as EditBook: category -> mainFromCode)
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
const subOption = ref([])
const categoryMain = ref('9.00.00')
const categoryCode = ref('9.00.00')

function mainFromCode(code) {
  if (!code) return '9.00.00'
  const parts = String(code).split('.')
  return parts.length === 3 ? `${parts[0]}.00.00` : '9.00.00'
}

async function loadSubCategories(parentCode) {
  try {
    const res = await fetch(
      `${CATEGORIES_API}?parentCode=${encodeURIComponent(parentCode)}`,
      { headers: authHeaders.value }
    )
    if (!res.ok) {
      subOption.value = []
      return
    }

    const data = await res.json()
    const arr = Array.isArray(data) ? data : []
    subOption.value = arr
      .map(x => ({
        code: x.code ?? x.Code,
        name: x.name ?? x.Name,
        level: Number(x.level ?? x.Level),
        parentCode: x.parentCode ?? x.ParentCode
      }))
      .filter(x => x.code)
  } catch {
    subOption.value = []
  }
}

const showIssnFields = computed(() => !!book.value?.issn)

const revokeCoverPreview = () => {
  if (coverPreviewUrl.value) {
    URL.revokeObjectURL(coverPreviewUrl.value)
    coverPreviewUrl.value = ''
  }
}

const clearCoverSelection = () => {
  coverFile.value = null
  revokeCoverPreview()
  if (coverInput.value) coverInput.value.value = ''
}

const onCoverSelected = (event) => {
  const file = event?.target?.files?.[0] ?? null
  coverFile.value = file
  revokeCoverPreview()
  if (file) {
    coverPreviewUrl.value = URL.createObjectURL(file)
  }
}

onBeforeUnmount(() => {
  revokeCoverPreview()
})

const uploadCover = async (bookId) => {
  if (!coverFile.value || !bookId) return null

  const formData = new FormData()
  formData.append('file', coverFile.value)

  const headers = token ? { Authorization: `Bearer ${token}` } : {}
  const res = await fetch(apiUrl(`/api/Book/${bookId}/cover`), {
    method: 'POST',
    headers,
    body: formData
  })

  if (!res.ok) {
    const txt = await res.text().catch(() => '')
    throw new Error(txt || 'Cover upload failed.')
  }

  const data = await res.json().catch(() => null)
  return data?.coverUrl ?? null
}

const loadNextInventoryNumber = async () => {
  if (!token) return
  if (nextInvLoading.value) return
  nextInvLoading.value = true

  try {
    const res = await fetch(`${INVENTORY_API}/next-inventory`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    if (!res.ok) return
    const data = await res.json().catch(() => null)
    const next = data?.inventoryNumber ?? data?.number ?? data
    if (typeof next === 'string' && !copyInvNumber.value) {
      copyInvNumber.value = next
    }
  } catch {
    // ignore suggestion errors
  } finally {
    nextInvLoading.value = false
  }
}

const openAddCopyModal = () => {
  addCopyError.value = ''
  addCopySuccess.value = ''
  copyInvNumber.value = ''
  copyPrice.value = ''
  issnVolume.value = ''
  issnNumber.value = ''
  clearCoverSelection()
  const initialCode = book.value?.categoryCode ?? book.value?.CategoryCode ?? '9.00.00'
  categoryMain.value = mainFromCode(initialCode)
  categoryCode.value = initialCode
  subOption.value = []
  if (categoryMain.value !== '9.00.00') {
    loadSubCategories(categoryMain.value)
  }
  loadNextInventoryNumber()
  showAddCopyModal.value = true
}

const closeAddCopyModal = () => {
  showAddCopyModal.value = false
  clearCoverSelection()
}

// availability (da se odločimo ali pokažemo modal)
const availability = ref(null) // { bookId, total, available, isAvailable }

async function loadAvailabilityByBookId(bookId) {
  try {
    const res = await fetch(`${INVENTORY_API}/availability?bookId=${encodeURIComponent(bookId)}`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    if (!res.ok) return null
    return await res.json()
  } catch {
    return null
  }
}

const addCopyFromIsbn = async () => {
  if (addCopyLoading.value) return
  addCopyError.value = ''
  addCopySuccess.value = ''
  addCopyLoading.value = true

  try {
    const isbnOrIssn = normalizeCode(book.value?.issn || book.value?.isbn || code.value)
    if (!isbnOrIssn) {
      addCopyError.value = 'Manjka ISBN.'
      return
    }

    const rawPrice = String(copyPrice.value ?? '').trim()
    let parsedPrice = null
    if (rawPrice) {
      const num = Number(rawPrice.replace(',', '.'))
      if (!Number.isFinite(num) || num < 0) {
        addCopyError.value = 'Cena mora biti veljavna in nenegativna.'
        return
      }
      parsedPrice = num
    }

    const finalCategoryCode =
      categoryMain.value === '9.00.00'
        ? '9.00.00'
        : (categoryCode.value || categoryMain.value)

    const res = await fetch(`${INVENTORY_API}/from-isbn`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`
      },
      body: JSON.stringify({
        isbn: isbnOrIssn,
        inventoryNumber: (copyInvNumber.value || '').trim() || null,
        price: parsedPrice,
        location: mainFromCode(
          finalCategoryCode
        ),
        categoryCode: finalCategoryCode,
        issnVolume: showIssnFields.value ? (issnVolume.value || '').trim() || null : null,
        issnNumber: showIssnFields.value ? (issnNumber.value || '').trim() || null : null
      })
    })

    if (res.status === 409) {
      addCopyError.value = 'Inventarna številka že obstaja.'
      return
    }

    if (!res.ok) {
      const txt = await res.text().catch(() => '')
      addCopyError.value = txt || 'Dodajanje kopije ni uspelo.'
      return
    }

    const created = await res.json().catch(() => null)

    let coverUploadFailed = false
    if (coverFile.value) {
      try {
        const bookId = book.value?.id ?? book.value?.Id ?? created?.bookId ?? created?.BookId
        if (!bookId) {
          coverUploadFailed = true
        } else {
          const newUrl = await uploadCover(bookId)
          if (newUrl && book.value) {
            book.value.coverUrl = newUrl
            book.value.coverURL = newUrl
          }
        }
      } catch {
        coverUploadFailed = true
      } finally {
        clearCoverSelection()
      }
    }

    addCopySuccess.value = coverUploadFailed
      ? 'Kopija uspešno dodana, nalaganje slike ni uspelo.'
      : 'Kopija uspešno dodana.'

    if (!coverUploadFailed) {
      setTimeout(() => {
        showAddCopyModal.value = false
      }, 700)
    }
  } catch {
    addCopyError.value = 'Dodajanje kopije ni uspelo.'
  } finally {
    addCopyLoading.value = false
  }
}

// --- SEARCH ---
let debounceTimer = null
const search = async () => {
  const v = normalizeCode(code.value)
  if (!v) return

  status.value = 'searching'
  searchErrorText.value = ''
  book.value = null
  availability.value = null
  showAddCopyModal.value = false

  try {
    const controller = new AbortController()
    const timeoutId = setTimeout(() => controller.abort(), LOOKUP_TIMEOUT_MS)

    const res = await fetch(`${LOOKUP_API}/${encodeURIComponent(v)}`, {
      signal: controller.signal,
      headers: { Authorization: `Bearer ${token}` }
    })
    clearTimeout(timeoutId)

    // ni v DB in ni na spletu -> preusmeri na addbook
    if (res.status === 404) {
      status.value = 'notfound'
      setTimeout(() => {
        router.push({ path: '/addbook', query: { code: v } })
      }, 800)
      return
    }

    if (!res.ok) {
      status.value = 'error'
      searchErrorText.value = `Napaka strežnika (${res.status}).`
      return
    }

    const isNew = res.status === 201
    const data = await res.json()

    // normalizacija ključev
    data.title = data.title ?? data.Title ?? ''
    data.isbn = data.isbn ?? data.ISBN ?? ''
    data.issn = data.issn ?? data.ISSN ?? null

    const a = data.authors ?? data.Authors ?? data.author ?? data.Author
    if (Array.isArray(a)) {
      data.authors = a
      data.author = a
    } else if (typeof a === 'string' && a.trim()) {
      data.authors = [a]
      data.author = [a]
    } else {
      data.authors = []
      data.author = []
    }

    data.genre = Array.isArray(data.genre ?? data.Genre) ? (data.genre ?? data.Genre) : []
    data.publisher = data.publisher ?? data.Publisher ?? ''
    data.language = data.language ?? data.Language ?? ''
    data.coverUrl = data.coverUrl ?? data.coverURL ?? ''

    book.value = data
    status.value = 'found'

    // availability naložiš za prikaz (ni več pogoj za modal)
    const bookId = data.id ?? data.Id
    if (bookId) {
      const av = await loadAvailabilityByBookId(bookId)
      availability.value = av
    }

    // ✅ modal za admina pri najdeni knjigi (tudi če je nova)
    if (isAdmin.value) {
      openAddCopyModal()
    }
  } catch (e) {
    console.error(e)
    status.value = 'error'
    searchErrorText.value = e?.name === 'AbortError'
      ? 'Poizvedba je potekla (timeout). Poskusi znova.'
      : 'Prišlo je do napake pri iskanju.'
  }
}

// --- AUTO SEARCH on scan/typing ---
// Auto-search je samo za ISBN-13, da počasno ročno tipkanje ISBN-13
// na telefonu ne sproži prezgodnjega lookupa pri 10 znakih.
watch(code, (val) => {
  const v = normalizeCode(val)

  // debounce
  if (debounceTimer) clearTimeout(debounceTimer)

  // sproži samo za ISBN-13
  if (v.length === 13) {
    debounceTimer = setTimeout(() => {
      search()
    }, 250)
  }
})

watch(categoryMain, async (v) => {
  if (!v) return

  if (v === '9.00.00') {
    subOption.value = []
    categoryCode.value = '9.00.00'
    return
  }

  await loadSubCategories(v)

  const ok = subOption.value.some(x => x.code === categoryCode.value)
  if (!ok) categoryCode.value = ''
})
</script>

<template>
  <div class="isbn-wrapper">
    <div class="isbn-container">
      <h2 class="isbn-card-title section-title">{{ $t('isbn.title') }}</h2>

      <!-- SEARCH -->
      <div class="card">
        <form class="isbn-search" @submit.prevent="search">
          <input
            ref="inputRef"
            v-model="code"
            class="isbn-search__input"
            :placeholder="$t('isbn.placeholder')"
          />
          <button class="btn" type="submit">{{ $t('isbn.submit') }}</button>
        </form>

        <div class="isbn-status">
          <p v-if="status === 'idle'">{{ $t('isbn.status.idle') }}</p>
          <p v-else-if="status === 'searching'">{{ $t('isbn.status.searching') }}</p>
          <p v-else-if="status === 'found'" class="ok">{{ $t('isbn.status.found') }}</p>
          <p v-else-if="status === 'notfound'">{{ $t('isbn.status.notfound') }}</p>
          <p v-else-if="status === 'error'" class="err">{{ $t('isbn.status.error') }}</p>
          <p v-if="status === 'error' && searchErrorText" class="err">{{ searchErrorText }}</p>
        </div>
      </div>

      <h2 class="isbn-card-title section-title">{{ $t('isbn.results') }}</h2>

      <!-- RESULT -->
      <div v-if="book" class="card result">
        <!-- LEFT -->
        <div class="left">
          <div class="cover">
            <img v-if="book.coverUrl" :src="resolveAssetUrl(book.coverUrl)" alt="Cover" />
            <span v-else class="cover-fallback">
              <i class="pi pi-book" aria-hidden="true"></i>
            </span>
          </div>

          <div class="lang-badge">
            <span class="lang-label">{{ $t('isbn.language') }}</span>
            <span class="lang-value">{{ (book.language || '').toUpperCase() }}</span>
          </div>
        </div>

        <!-- RIGHT -->
        <div class="right">
          <!-- ✅ naslov -->
          <div class="meta-card">
            <span class="meta-label">{{ $t('isbn.titleLabel') ?? 'Naslov' }}</span>
            <strong class="meta-value">{{ bookTitle }}</strong>
          </div>

          <!-- ✅ avtor -->
          <div class="meta-card">
            <span class="meta-label">{{ $t('isbn.author') }}</span>
            <strong class="meta-value clamp-1">{{ bookAuthorText || '—' }}</strong>
          </div>

          <div class="topmeta">
            <div class="meta-card">
              <span class="meta-label">{{ book.issn ? 'ISSN' : 'ISBN' }}</span>
              <strong class="meta-value">{{ book.issn || book.isbn }}</strong>
            </div>
          </div>

          <div class="meta">
            <div class="meta-card">
              <span class="meta-label">{{ $t('isbn.publisher') }}</span>
              <strong class="meta-value">{{ book.publisher || '—' }}</strong>
            </div>

            <div class="meta-card year">
              <span class="meta-label">{{ $t('isbn.year') }}</span>
              <strong class="meta-value">{{ book.yearPublished ?? '—' }}</strong>
            </div>
          </div>

          <div class="genre">
            <span class="genre-label">Žanri: </span>

            <template v-if="displayGenres.length">
              <span v-for="(g, i) in displayGenres" :key="i" class="tag">
                {{ g }}
              </span>
            </template>

            <span v-else class="genre-empty">—</span>
          </div>

          <!-- Admin: ročni gumb (če želi admin dodati kopijo tudi pri novih) -->
          <div v-if="isAdmin" style="margin-top: 6px;">
            <button class="btn" type="button" @click="openAddCopyModal">
              + Dodaj kopijo
            </button>
          </div>
        </div>
      </div>

      <div v-else class="card empty empty-state">
        {{ $t('isbn.noResults') }}
      </div>
    </div>

    <!-- ADD COPY MODAL -->
    <div v-if="showAddCopyModal" class="modal-backdrop" @click.self="closeAddCopyModal">
      <div class="modal">
        <h3 class="modal-title">Dodaj novo kopijo?</h3>
        <p class="modal-sub">
          Knjiga: <strong>{{ bookTitle }}</strong><br />
          Avtor: <strong>{{ bookAuthorText || '—' }}</strong>
        </p>

        <label class="modal-label">Inventarna številka (opcijsko)</label>
        <input
          v-model="copyInvNumber"
          class="modal-input"
          type="text"
          placeholder="npr. 000123"
        />

        <label class="modal-label modal-label-spaced">Cena (EUR, opcijsko)</label>
        <input
          v-model="copyPrice"
          class="modal-input"
          type="number"
          min="0"
          step="0.01"
          placeholder="npr. 24.99"
        />

        <label class="modal-label modal-label-spaced">{{ $t('addbook.coverUpload') }}</label>
        <input
          ref="coverInput"
          class="modal-input"
          type="file"
          accept="image/*"
          @change="onCoverSelected"
        />
        <p class="modal-hint">{{ $t('addbook.coverUploadHint') }}</p>
        <div v-if="coverPreviewSrc" class="modal-cover-preview">
          <img :src="coverPreviewSrc" alt="Cover preview" />
        </div>

        <div class="modal-divider"></div>

        <div v-if="!showIssnFields" class="modal-issn">
          <label class="modal-label">Kategorija (glavna)</label>
          <select v-model="categoryMain" class="modal-input" :data-category="categoryMain.split('.')[0]">
            <option v-for="m in mainOption" :key="m.code" :value="m.code">
              {{ m.code }} — {{ m.name }}
            </option>
          </select>

          <div v-if="categoryMain !== '9.00.00'">
            <label class="modal-label modal-label-spaced">Podkategorija</label>
            <select v-model="categoryCode" class="modal-input">
              <option value="">— izberi —</option>
              <option v-for="s in subOption" :key="s.code" :value="s.code">
                {{ s.code }} — {{ s.name }}
              </option>
            </select>
          </div>
        </div>

        <div v-if="showIssnFields" class="modal-issn">
          <label class="modal-label">{{ $t('addbook.issnVolumeLabel') }}</label>
          <input
            v-model="issnVolume"
            class="modal-input"
            type="text"
            :placeholder="$t('addbook.issnVolumePlaceholder')"
          />

          <label class="modal-label modal-label-spaced">{{ $t('addbook.issnNumberLabel') }}</label>
          <input
            v-model="issnNumber"
            class="modal-input"
            type="text"
            :placeholder="$t('addbook.issnNumberPlaceholder')"
          />

          <label class="modal-label modal-label-spaced">Kategorija (glavna)</label>
          <select v-model="categoryMain" class="modal-input" :data-category="categoryMain.split('.')[0]">
            <option v-for="m in mainOption" :key="m.code" :value="m.code">
              {{ m.code }} — {{ m.name }}
            </option>
          </select>

          <div v-if="categoryMain !== '9.00.00'">
            <label class="modal-label modal-label-spaced">Podkategorija</label>
            <select v-model="categoryCode" class="modal-input">
              <option value="">— izberi —</option>
              <option v-for="s in subOption" :key="s.code" :value="s.code">
                {{ s.code }} — {{ s.name }}
              </option>
            </select>
          </div>
        </div>

        <p v-if="addCopyError" class="modal-err">{{ addCopyError }}</p>
        <p v-if="addCopySuccess" class="modal-ok">{{ addCopySuccess }}</p>

        <div class="modal-actions">
          <button class="btn modal-btn-ghost" type="button" @click="closeAddCopyModal" :disabled="addCopyLoading">
            Prekliči
          </button>

          <button class="btn modal-btn-primary" type="button" @click="addCopyFromIsbn" :disabled="addCopyLoading">
            {{ addCopyLoading ? '...' : 'Dodaj kopijo' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.isbn-wrapper {
  --space-1: 10px;
  --space-2: 14px;
  --space-3: 18px;

  min-height: 100vh;
  padding: var(--space-2);
  background: var(--bg);
  color: var(--text);
}

.isbn-container {
  max-width: 860px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
}

.isbn-card-title {
  margin: 0;
  font-size: 18px;
  font-weight: 800;
}

/* cards */
.card {
  border-radius: 16px;
  border: 1px solid var(--card-border);
  padding: var(--space-3);
  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: var(--shadow-elev-1);
}

/* search */
.isbn-search {
  display: grid;
  grid-template-columns: 1fr 140px;
  gap: 10px;
}

.isbn-search__input {
  width: 100%;
  padding: 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--select-text, var(--text));
  outline: none;
}
.isbn-search__input:focus-visible {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.isbn-status {
  margin-top: 10px;
  font-size: 13px;
  color: var(--muted);
}
.ok { color: var(--status-green-text); }
.err { color: var(--status-red-text); }

.btn {
  width: 100%;
  padding: 10px 12px;
  border-radius: 12px;
  font-size: 16px;
  font-weight: 700;
  background: var(--card-bg);
  color: var(--btn);
  border: 1px solid var(--card-border);
  cursor: pointer;
}
.btn:focus-visible {
  outline: none;
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.btn:hover{
  background: var(--nav-item-hover);
}

/* stable 2-column result */
.result {
  display: grid;
  grid-template-columns: 140px minmax(0, 1fr);
  gap: 16px;
  align-items: start;
}

/* left */
.left {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.cover {
  width: 140px;
  height: 190px;
  border-radius: 14px;
  overflow: hidden;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  display: grid;
  place-items: center;
}

.cover img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.cover-fallback {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 30px;
  opacity: 0.6;
}

.lang-badge {
  border-radius: 999px;
  padding: 8px 10px;
  text-align: center;
  background: rgba(99,102,241,0.18);
  border: 1px solid rgba(99,102,241,0.35);
  color: var(--text);
  display: inline-flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  min-height: 30px;
}

.lang-label {
  display: block;
  font-size: 11px;
  opacity: 0.85;
  margin-bottom: 2px;
  color: var(--text);
}

.lang-value {
  display: block;
  font-weight: 900;
  letter-spacing: 1px;
  color: var(--text);
}

/* right */
.right {
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.topmeta {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
}

.meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.meta-card {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  min-width: 0;
}
.meta-card:hover {
  border-color: var(--nav-item-active-border);
  box-shadow: var(--shadow-elev-2);
}

.meta-label {
  display: block;
  font-size: 12px;
  color: var(--muted);
  margin-bottom: 2px;
}

.meta-value {
  display: block;
  font-size: 13px;
  font-weight: 650;
  line-height: 1.2;
  overflow-wrap: anywhere;
}

.meta-card.year .meta-value {
  white-space: nowrap;
}

.genre {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 10px;
}

.genre-label {
  font-size: 13px;
  color: var(--muted);
  font-weight: 650;
  margin-right: 2px;
}

.genre-empty {
  color: var(--muted);
  font-size: 13px;
}

.tag {
  padding: 6px 12px;
  border-radius: 999px;
  font-size: 12px;
  background: rgba(99,102,241,0.16);
  border: 1px solid rgba(99,102,241,0.35);
  color: var(--text);
  text-transform: capitalize;
  letter-spacing: 0.8px;
  max-width: 260px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: inline-flex;
  align-items: center;
  min-height: 26px;
}
.tag:hover {
  border-color: rgba(99,102,241,0.6);
}

.clamp-1 {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.empty {
  border-style: dashed;
  background: var(--card-bg);
  color: var(--muted);
}

/* modal */
.modal-backdrop {
  position: fixed;
  inset: 0;
  z-index: 60;
  background: var(--modal-overlay);
  display: grid;
  place-items: center;
  padding: 16px;
}

.modal {
  width: min(520px, 92vw);
  border-radius: 16px;
  border: 1px solid var(--card-border);
  background: var(--modal-bg);
  box-shadow: var(--shadow-elev-3);
  padding: 16px;
}

.modal-title {
  margin: 0;
  font-size: 18px;
  font-weight: 900;
}

.modal-sub {
  margin: 6px 0 12px;
  color: var(--muted);
  font-size: 13px;
}

.modal-label {
  display: block;
  font-size: 12px;
  color: var(--muted);
  font-weight: 800;
  margin: 0 0 6px;
}

.modal-label-spaced {
  margin-top: 10px;
}

.modal-input {
  width: 100%;
  padding: 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--select-text, var(--text));
  outline: none;
}
.modal-input:focus-visible {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.modal-input option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.modal-issn {
  margin-top: 8px;
}
.modal-divider {
  height: 1px;
  background: var(--card-border);
  margin: 12px 0 6px;
}

.modal-hint {
  margin: 6px 0 0;
  font-size: 12px;
  color: var(--muted);
}

.modal-cover-preview {
  margin-top: 10px;
  width: 120px;
  height: 170px;
  border-radius: 10px;
  overflow: hidden;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
}

.modal-cover-preview img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.modal-actions {
  margin-top: 14px;
  display: flex;
  gap: 10px;
  justify-content: flex-end;
  flex-wrap: wrap;
}

.modal-btn-primary {
  background: var(--btn-gradient1);
  border-color: var(--btn-gradient1);
  color: var(--on-accent);
}

.modal-btn-ghost {
  background: transparent;
}
.modal-btn-ghost:hover {
  background: var(--nav-item-hover);
}
.modal-btn-ghost:focus-visible,
.modal-btn-primary:focus-visible {
  outline: none;
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}


.modal-err {
  margin: 10px 0 0;
  color: var(--status-red-text);
  font-weight: 800;
  font-size: 13px;
}

.modal-ok {
  margin: 10px 0 0;
  color: var(--status-green-text);
  font-weight: 800;
  font-size: 13px;
}

@media (max-width: 900px) {
  .isbn-search {
    grid-template-columns: 1fr;
  }
  .result {
    grid-template-columns: 1fr;
  }
  .cover {
    width: 100%;
    height: 220px;
  }
  .left {
    align-items: stretch;
  }
}

@media (max-width: 600px) {
  .topmeta {
    grid-template-columns: 1fr;
  }

  .meta {
    grid-template-columns: 1fr;
  }

  .genre {
    align-items: flex-start;
  }

  .tag {
    max-width: 100%;
  }
}</style>


