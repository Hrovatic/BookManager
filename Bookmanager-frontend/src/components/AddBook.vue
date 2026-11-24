<script setup>
import { ref, computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { apiUrl } from '@/utils/api'

const isbn = ref("");
const title = ref("");
const author = ref("");
const publisher = ref("");
const yearPublished = ref("");
const price = ref("");
const language = ref("");
const genre = ref("");
const location = ref("9.00.00");
const locationSub = ref("");
const locationSubOptions = ref([]);
let successTimer = null;
const CATEGORIES_API = apiUrl('/api/categories')

const uploadSuccess = ref(false);
const uploadError = ref(null);

const route = useRoute()
const { t } = useI18n()
const prefilledCode = computed(() => route.query.isbn ?? route.query.code ?? '')
const hasPrefilledIsbn = computed(() => !!prefilledCode.value)
const isAdmin = computed(() => localStorage.getItem('role') === 'admin')
const authHeaders = computed(() => {
  const token = localStorage.getItem('token')
  return token ? { Authorization: `Bearer ${token}` } : {}
})

const locationMainOptions = computed(() => ([
  { code: "1.00.00", name: t('books.categoryNames.1_00_00') },
  { code: "2.00.00", name: t('books.categoryNames.2_00_00') },
  { code: "4.00.00", name: t('books.categoryNames.4_00_00') },
  { code: "5.00.00", name: t('books.categoryNames.5_00_00') },
  { code: "6.00.00", name: t('books.categoryNames.6_00_00') },
  { code: "7.00.00", name: t('books.categoryNames.7_00_00') },
  { code: "9.00.00", name: t('books.categoryNames.9_00_00') },
  { code: "10.00.00", name: t('books.categoryNames.10_00_00') },
]))

const locationMainLabel = computed(() => {
  const found = locationMainOptions.value.find(x => x.code === location.value)
  if (!found) return location.value
  return `${found.code} — ${found.name}`
})

const locationSubLabel = computed(() => {
  if (!locationSub.value) return ''
  const found = locationSubOptions.value.find(x => x.code === locationSub.value)
  return found ? `${found.code} — ${found.name}` : ''
})

async function loadSubCategories(parentCode) {
  if (!parentCode || parentCode === '9.00.00') {
    locationSubOptions.value = []
    locationSub.value = ''
    return
  }

  try {
    const res = await fetch(`${CATEGORIES_API}?parentCode=${encodeURIComponent(parentCode)}`, {
      headers: authHeaders.value
    })

    if (!res.ok) {
      locationSubOptions.value = []
      locationSub.value = ''
      return
    }

    const data = await res.json()
    const arr = Array.isArray(data) ? data : []
    locationSubOptions.value = arr
      .map(x => ({
        code: x.code ?? x.Code,
        name: x.name ?? x.Name
      }))
      .filter(x => x.code && x.name)

    if (!locationSubOptions.value.some(x => x.code === locationSub.value)) {
      locationSub.value = ''
    }
  } catch {
    locationSubOptions.value = []
    locationSub.value = ''
  }
}

watch(
  () => prefilledCode.value,
  (newCode) => {
    isbn.value = newCode ? String(newCode) : ''
  },
  { immediate: true }
)

watch(
  () => location.value,
  async (newMain) => {
    await loadSubCategories(newMain)
  },
  { immediate: true }
)

const normalizeCode = (value) =>
  String(value ?? '').trim().replace(/[-\s]/g, '').toUpperCase()

const normalizedIdentifier = computed(() => normalizeCode(isbn.value))
const isIssn = computed(() => normalizedIdentifier.value.length === 8)
const isIsbn = computed(() => {
  const len = normalizedIdentifier.value.length
  return len === 10 || len === 13
})

const issnVolume = ref('')
const issnNumber = ref('')
const showIssnModal = ref(false)

const openIssnModal = () => {
  if (isIssn.value) showIssnModal.value = true
}

const closeIssnModal = () => {
  showIssnModal.value = false
}

const onIdentifierCommit = () => {
  if (isIssn.value) {
    openIssnModal()
  }
}

watch(
  () => normalizedIdentifier.value,
  (val) => {
    if (val.length !== 8) {
      closeIssnModal()
      issnVolume.value = ''
      issnNumber.value = ''
    }
  }
)

const clearForm = () => {
  // ISBN brišemo samo v manual mode
  if (!hasPrefilledIsbn.value) isbn.value = "";

  title.value = "";
  author.value = "";
  publisher.value = "";
  yearPublished.value = "";
  price.value = "";
  language.value = "";
  genre.value = "";
  location.value = "9.00.00";
  locationSub.value = "";
  locationSubOptions.value = [];
  closeIssnModal();
  issnVolume.value = "";
  issnNumber.value = "";
};

const resetForm = () => {
  clearForm()
  uploadError.value = null
  uploadSuccess.value = false
  if (successTimer) {
    clearTimeout(successTimer)
    successTimer = null
  }
}

const handleSubmit = async (e) => {
  e.preventDefault();
  uploadSuccess.value = false;
  uploadError.value = null;

  if (!title.value.trim() || !author.value.trim()) {
    uploadError.value = "Naslov in avtor sta obvezna.";
    return;
  }

  const normalized = normalizedIdentifier.value
  const isbnValue = isIsbn.value ? normalized : null
  const issnValue = isIssn.value ? normalized : null
  const rawPrice = String(price.value ?? '').trim()
  let parsedPrice = null
  if (rawPrice) {
    const num = Number(rawPrice.replace(',', '.'))
    if (!Number.isFinite(num) || num < 0) {
      uploadError.value = "Cena mora biti veljavna in nenegativna."
      return
    }
    parsedPrice = num
  }

  const finalLocationLabel = locationSubLabel.value || locationMainLabel.value
  const finalCategoryCode =
    location.value === '9.00.00'
      ? '9.00.00'
      : (locationSub.value || location.value)

  const dto = {
    isbn: isbnValue,
    issn: issnValue,
    title: title.value.trim(),
    author: author.value.trim(),
    publisher: publisher.value?.trim() || null,
    yearPublished: yearPublished.value
      ? Number(yearPublished.value)
      : null,
    price: parsedPrice,
    language: language.value?.trim() || null,
    location: finalLocationLabel || null,
    categoryCode: finalCategoryCode,
    genre: genre.value
      ? genre.value.split(",").map(g => g.trim()).filter(Boolean)
      : [],
    issnVolume: isIssn.value ? (issnVolume.value?.trim() || null) : null,
    issnNumber: isIssn.value ? (issnNumber.value?.trim() || null) : null
  };

  try {
    const token = localStorage.getItem('token');
    const headers = token
      ? { "Content-Type": "application/json", Authorization: `Bearer ${token}` }
      : { "Content-Type": "application/json" };

    const response = await fetch(apiUrl('/api/Book/vnos'), {
      method: "POST",
      headers,
      body: JSON.stringify(dto)
    });

    if (!response.ok) {
      const err = await response.text();
      uploadError.value = err || "Shranjevanje ni uspelo.";
      return;
    }
    let created = null;
    try {
      created = await response.json();
    } catch {
      created = null;
    }

    uploadSuccess.value = true;
    clearForm();

    if (successTimer) clearTimeout(successTimer);
    successTimer = setTimeout(() => {
      uploadSuccess.value = false;
    }, 3000);

  } catch {
    uploadError.value = "Network error";
  }
};
</script>

<template>
  <div class="addbook-wrapper">
    <div class="addbook-container">

      <h2 class="addbook-title section-title">{{ $t('addbook.title') }}</h2>

      <div v-if="isAdmin" class="addbook-toolbar">
        <router-link class="btn addbook-toolbar__link" to="/isbn-search">
          <i class="pi pi-barcode" aria-hidden="true"></i>
          {{ $t('addbook.scanLookup') }}
        </router-link>
      </div>

      <!-- INFO / CONTEXT -->
      <div class="addbook-info" v-if="hasPrefilledIsbn">
        <p>{{ $t('addbook.isbnNotFound') }}</p>
      </div>

      <div v-if="uploadSuccess" class="form-message success">
        <i class="pi pi-check-circle" aria-hidden="true"></i>
        {{ $t('addbook.success') }}
      </div>

      <div v-if="uploadError" class="form-message error">
        <i class="pi pi-exclamation-triangle" aria-hidden="true"></i>
        {{ uploadError }}
      </div>

      <!-- FORM -->
      <form class="addbook-form surface" @submit.prevent="handleSubmit">

        <!-- ISBN -->
        <div class="form-group">
          <label>{{ $t('addbook.isbn') }}</label>
          <input type="text" :placeholder="$t('addbook.isbnPlaceholder')" v-model="isbn" :readonly="hasPrefilledIsbn"
            @blur="onIdentifierCommit" @keydown.enter.prevent="onIdentifierCommit" />
          <div v-if="isIssn" class="issn-meta">
            <div class="issn-meta__text">
              <span class="issn-meta__label">{{ $t('addbook.issnMeta') }}</span>
              <span class="issn-meta__value">
                {{ issnVolume || '—' }} / {{ issnNumber || '—' }}
              </span>
            </div>
            <button type="button" class="issn-meta__btn" @click="openIssnModal">
              {{ $t('addbook.issnEdit') }}
            </button>
          </div>
        </div>

        <!-- NASLOV -->
        <div class="form-group">
          <label>{{ $t('addbook.titleLabel') }}</label>
          <input type="text" :placeholder="$t('addbook.titlePlaceholder')" v-model="title" />
        </div>

        <!-- AVTOR -->
        <div class="form-group">
          <label>{{ $t('addbook.author') }}</label>
          <input type="text" :placeholder="$t('addbook.authorPlaceholder')" v-model="author" />
        </div>

        <!-- META GRID -->
        <div class="form-grid">

          <div class="form-group">
            <label>{{ $t('addbook.publisher') }}</label>
            <input type="text" v-model="publisher" />
          </div>

          <div class="form-group">
            <label>{{ $t('addbook.year') }}</label>
            <input type="number" v-model="yearPublished" />
          </div>

          <div class="form-group">
            <label>{{ $t('addbook.price') }}</label>
            <input type="number" min="0" step="0.01" :placeholder="$t('addbook.pricePlaceholder')" v-model="price" />
          </div>

          <div class="form-group">
            <label>{{ $t('addbook.language') }}</label>
            <input type="text" :placeholder="$t('addbook.languagePlaceholder')" v-model="language" />
          </div>

          <div class="form-group">
            <label>{{ $t('addbook.location') }}</label>
            <select v-model="location">
              <option v-for="opt in locationMainOptions" :key="opt.code" :value="opt.code">
                {{ opt.code }} — {{ opt.name }}
              </option>
            </select>
          </div>

          <div class="form-group" v-if="location !== '9.00.00'">
            <label>{{ $t('addbook.subcategory') }}</label>
            <select v-model="locationSub">
              <option value="">{{ $t('addbook.subcategoryPlaceholder') }}</option>
              <option v-for="sub in locationSubOptions" :key="sub.code" :value="sub.code">
                {{ sub.code }} — {{ sub.name }}
              </option>
            </select>
          </div>

          <div class="form-group">
            <label>{{ $t('addbook.genres') }}</label>
            <input type="text" :placeholder="$t('addbook.genresPlaceholder')" v-model="genre" />
          </div>
        </div>

        <!-- ACTIONS -->
        <div class="form-actions">
          <button type="submit" class="btn primary">{{ $t('addbook.save') }}<i class="pi pi-save"></i></button>
          <button type="button" class="btn" @click="resetForm">{{ $t('addbook.cancel') }}</button>
        </div>

      </form>
    </div>

    <!-- ISSN MODAL -->
    <div v-if="showIssnModal" class="modal-backdrop" @click.self="closeIssnModal">
      <div class="modal">
        <h3 class="modal-title">{{ $t('addbook.issnModalTitle') }}</h3>
        <p class="modal-sub">{{ $t('addbook.issnModalSubtitle') }}</p>

        <label class="modal-label">{{ $t('addbook.issnVolumeLabel') }}</label>
        <input v-model="issnVolume" class="modal-input" type="text"
          :placeholder="$t('addbook.issnVolumePlaceholder')" />

        <label class="modal-label">{{ $t('addbook.issnNumberLabel') }}</label>
        <input v-model="issnNumber" class="modal-input" type="text"
          :placeholder="$t('addbook.issnNumberPlaceholder')" />

        <div class="modal-actions">
          <button class="btn modal-btn-ghost" type="button" @click="closeIssnModal">
            {{ $t('addbook.issnModalCancel') }}
          </button>

          <button class="btn modal-btn-primary" type="button" @click="closeIssnModal">
            {{ $t('addbook.issnModalSave') }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
/* -------------------------------------------------------
   Layout
------------------------------------------------------- */
.addbook-wrapper {
  background: var(--bg);
  color: var(--text);
  padding: 16px 0 12px;
  overflow-x: clip;
}

.addbook-container {
  max-width: 920px;
  margin: 0 auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.addbook-toolbar {
  display: flex;
  justify-content: flex-start;
}

.addbook-toolbar__link {
  width: auto;
  text-decoration: none;
}

/* -------------------------------------------------------
   Title + Info
------------------------------------------------------- */
.addbook-title {
  font-size: 24px;
  font-weight: 900;
  letter-spacing: 0.2px;
}

.addbook-info {
  padding: 12px 14px;
  border-radius: 14px;
  background: var(--status-info-bg);
  border: 1px solid var(--status-info-border);
  font-size: 13px;
  font-weight: 600;
  color: var(--status-info-text);
}

/* -------------------------------------------------------
   Form surface
------------------------------------------------------- */
.addbook-form {
  padding: 16px;
  border-radius: 18px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  box-shadow: var(--shadow-elev-1);
  display: flex;
  flex-direction: column;
  gap: 14px;
}

/* -------------------------------------------------------
   Groups + Controls
------------------------------------------------------- */
.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group--full {
  grid-column: 1 / -1;
}

.form-group label {
  font-size: 12px;
  font-weight: 800;
  color: var(--muted);
}

.form-group input,
.form-group select {
  padding: 11px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, rgba(255, 255, 255, 0.08));
  /* theme-safe */
  color: var(--text);
  font-size: 14px;
  outline: none;
}

.form-group input::placeholder {
  color: var(--muted);
}

.form-group input:focus,
.form-group select:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px var(--focus-ring);
}

.form-group select option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.field-hint {
  font-size: 12px;
  color: var(--muted);
  margin-top: 2px;
}

/* ISSN meta row */
.issn-meta {
  margin-top: 6px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  padding: 8px 10px;
  border-radius: 12px;
  background: var(--status-info-bg);
  border: 1px solid var(--status-info-border);
}

.issn-meta__text {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 6px;
  font-size: 12px;
}

.issn-meta__label {
  color: var(--muted);
  font-weight: 700;
}

.issn-meta__value {
  color: var(--status-info-text);
  font-weight: 800;
}

.issn-meta__btn {
  border-radius: 10px;
  padding: 6px 10px;
  border: 1px solid var(--status-info-border);
  background: rgba(59, 130, 246, 0.10);
  color: var(--status-info-text);
  cursor: pointer;
  font-weight: 700;
  font-size: 12px;
}

.issn-meta__btn:hover {
  background: rgba(59, 130, 246, 0.20);
}

/* Readonly */
.form-group input[readonly] {
  opacity: 0.7;
  cursor: not-allowed;
  background: rgba(255, 255, 255, 0.05);
}

/* -------------------------------------------------------
   Grid (meta)
------------------------------------------------------- */
.form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px 16px;
}

/* -------------------------------------------------------
   Actions + Buttons
------------------------------------------------------- */
.form-actions {
  margin-top: 10px;
  display: flex;
  gap: 10px;
  justify-content: flex-end;
}

/* Base button style (works for all .btn in this component) */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  border-radius: 12px;
  padding: 10px 14px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--text);
  cursor: pointer;
  font-weight: 700;
  transition: filter 0.18s ease, opacity 0.18s ease, background 0.18s ease, border-color 0.18s ease;
}

.btn:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.btn:hover {
  background: var(--nav-item-hover);
}

/* Primary variant */
.btn.primary {
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  border-color: rgba(124, 92, 255, 0.7);
  color: var(--on-accent);
}

.btn.primary:hover {
  filter: brightness(1.08);
  background: linear-gradient(180deg, var(--btn-gradient2), var(--btn-gradient1));
}

/* -------------------------------------------------------
   Messages
------------------------------------------------------- */
.form-message {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 12px 14px;
  border-radius: 14px;
  font-size: 13px;
  font-weight: 700;
  margin-bottom: 10px;
}

.form-message .pi {
  font-size: 14px;
}

.form-message.success {
  background: var(--status-green-bg);
  border: 1px solid var(--status-green-border);
  color: var(--status-green-text);
}

.form-message.error {
  background: var(--status-red-bg);
  border: 1px solid var(--status-red-border);
  color: var(--status-red-text);
}

/* -------------------------------------------------------
   ISSN modal
------------------------------------------------------- */
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
  box-shadow: var(--shadow-elev-2);
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

.modal-input {
  width: 100%;
  padding: 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg);
  color: var(--select-text, var(--text));
  outline: none;
  margin-bottom: 10px;
}

.modal-input option {
  background: var(--option-bg, var(--bg));
  color: var(--option-text, var(--text));
}

.modal-actions {
  margin-top: 10px;
  display: flex;
  gap: 10px;
  justify-content: flex-end;
  flex-wrap: wrap;
}

.modal-btn-primary {
  background: linear-gradient(180deg, var(--btn-gradient1), var(--btn-gradient2));
  border-color: rgba(124, 92, 255, 0.7);
  color: var(--on-accent);
}

.modal-btn-ghost {
  background: transparent;
}

/* -------------------------------------------------------
   Responsive
------------------------------------------------------- */
@media (max-width: 600px) {
  .form-grid {
    grid-template-columns: 1fr;
  }

  .form-actions {
    flex-direction: column;
  }
}

/* Laptop: compact layout to avoid page scrolling */
@media (min-width: 1024px) and (max-height: 920px) {
  .addbook-wrapper {
    padding-top: 8px;
  }

  .addbook-container {
    max-width: 920px;
    gap: 10px;
  }

  .addbook-title {
    font-size: 18px;
  }

  .addbook-info {
    padding: 8px 10px;
    font-size: 12px;
  }

  .form-message {
    padding: 8px 10px;
    margin-bottom: 0;
    font-size: 12px;
  }

  .addbook-form {
    padding: 12px;
    gap: 10px;
    border-radius: 14px;
  }

  .form-group {
    gap: 2px;
  }

  .form-group label {
    font-size: 11px;
  }

  .form-group input,
  .form-group select {
    padding: 8px 10px;
    font-size: 13px;
  }

  .form-grid {
    gap: 8px 12px;
  }

  .issn-meta {
    margin-top: 4px;
    padding: 6px 8px;
  }

  .form-actions {
    margin-top: 4px;
  }

  .btn {
    padding: 8px 12px;
    font-size: 13px;
  }
}
</style>