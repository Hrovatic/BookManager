<script setup>
import { ref, onMounted, computed, watch, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import axios from 'axios'
import { apiUrl, resolveAssetUrl } from '@/utils/api'

const route = useRoute()
const router = useRouter()
const CATEGORIES_API = apiUrl('/api/categories')
const INVENTORY_API = apiUrl('/api/Inventory')
const { t } = useI18n()

const status = ref('loading') // loading | ready | error | saving
const errorMsg = ref('')
const successMsg = ref('')
const copyId = ref(null)
const copyStatus = ref(true)

// Category const
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

const token = localStorage.getItem('token')
const authHeaders = computed(() => (token ? { Authorization: `Bearer ${token}` } : {}))

const id = Number(route.params.id)

// form state (matches UpdateBookDto)
const form = ref({
  title: '',
  author: '',
  publisher: '',
  yearPublished: null,
  price: '',
  coverUrl: '',
  language: '',
  genre: '' // comma separated string
})
const coverFile = ref(null);
const coverPreviewUrl = ref('');
const coverInput = ref(null);
const coverPreviewSrc = computed(() => coverPreviewUrl.value || resolveAssetUrl(form.value.coverUrl) || '');

const revokeCoverPreview = () => {
  if (coverPreviewUrl.value) {
    URL.revokeObjectURL(coverPreviewUrl.value);
    coverPreviewUrl.value = '';
  }
};

const clearCoverSelection = () => {
  coverFile.value = null;
  revokeCoverPreview();
  if (coverInput.value) coverInput.value.value = '';
};

const onCoverSelected = (event) => {
  const file = event?.target?.files?.[0] ?? null;
  coverFile.value = file;
  revokeCoverPreview();
  if (file) {
    coverPreviewUrl.value = URL.createObjectURL(file);
  }
};

onBeforeUnmount(() => {
  revokeCoverPreview();
});

function mainFromCode(code) {
  if (!code) return '9.00.00'
  const parts = String(code).split('.')
  return parts.length === 3 ? `${parts[0]}.00.00` : '9.00.00'
}
function categoryNameKey(code) {
  return `books.categoryNames.${String(code || '').replaceAll('.', '_')}`
}

async function loadSubCategories(parentCode) {
  try {
    const res = await axios.get(
      `${CATEGORIES_API}?parentCode=${encodeURIComponent(parentCode)}`,
      { headers: authHeaders.value }
    )

    const arr = Array.isArray(res.data) ? res.data : []
    const mapped = arr.map(x => ({
      code: x.code ?? x.Code,
      name: x.name ?? x.Name,
      level: Number(x.level ?? x.Level),
      parentCode: x.parentCode ?? x.ParentCode
    })).filter(x => x.code)

    subOption.value = mapped

  } catch (e) {
    console.error('loadSubCategories failed', e?.response?.status, e?.response?.data, e?.message)
    subOption.value = []
  }
}


watch(categoryMain, async (v) => {
  if (!v) return

  if (v === '9.00.00') {
    subOption.value = []
    categoryCode.value = '9.00.00'
    return
  }

  await loadSubCategories(v)

  // če trenutni categoryCode ne spada več pod novo glavno, reset
  const ok = subOption.value.some(x => x.code === categoryCode.value)
  if (!ok) categoryCode.value = ''
})

// load existing book
const loadBook = async () => {
  status.value = 'loading'
  errorMsg.value = ''

  try {
    const res = await axios.get(apiUrl(`/api/Book/${id}`))
    const b = res.data
    const invRes = await axios.get(`${INVENTORY_API}/by-book/${id}`, { headers: authHeaders.value })

    const data = invRes.data
    const firstCopy = Array.isArray(data) ? data[0] : data

    copyId.value = firstCopy?.id ?? firstCopy?.Id ?? null
    copyStatus.value = firstCopy?.status ?? firstCopy?.Status ?? firstCopy?.isAvailable ?? true

    form.value.title = b.title ?? ''
    form.value.author = b.author ?? ''
    form.value.publisher = b.publisher ?? ''
    form.value.yearPublished = b.yearPublished ?? null
    form.value.price = b.price ?? ''
    form.value.coverUrl = b.coverUrl ?? b.coverURL ?? ''
    clearCoverSelection();
    form.value.language = b.language ?? ''
    form.value.genreText = Array.isArray(b.genre) ? b.genre.join(', ') : ''

    const code = b.categoryCode ?? b.CategoryCode ?? '9.00.00'
    categoryMain.value = mainFromCode(code)
    categoryCode.value = code

    // naloži sub kategorije, če ni "Drugo"
    if (categoryMain.value !== '9.00.00') {
      await loadSubCategories(categoryMain.value)
    }

    status.value = 'ready'
  } catch (e) {
    status.value = 'error'
    errorMsg.value = t('editBook.errors.loadFailed')
  }
}

onMounted(async () => {
  await loadBook()
})

// submit (PUT edit)
const uploadCover = async () => {
  if (!coverFile.value) return null;

  const formData = new FormData();
  formData.append('file', coverFile.value);

  const token = localStorage.getItem('token');
  const headers = token ? { Authorization: `Bearer ${token}` } : {};

  const res = await axios.post(
    apiUrl(`/api/Book/${id}/cover`),
    formData,
    { headers }
  );

  return res.data?.coverUrl ?? null;
};

const save = async () => {
  status.value = 'saving'
  errorMsg.value = ''
  successMsg.value = ''

  try {
    const token = localStorage.getItem('token')
    if (!token) {
      status.value = 'ready'
      errorMsg.value = t('editBook.errors.missingToken')
      return
    }

    const genreArray = String(form.value.genreText ?? '')
      .split(',')
      .map(x => x.trim())
      .filter(Boolean)

    const rawPrice = String(form.value.price ?? '').trim()
    let parsedPrice = null
    if (rawPrice) {
      const num = Number(rawPrice.replace(',', '.'))
      if (!Number.isFinite(num) || num < 0) {
        status.value = 'ready'
        errorMsg.value = t('editBook.errors.invalidPrice')
        return
      }
      parsedPrice = num
    }

    const payload = {
      title: form.value.title,
      author: form.value.author,
      publisher: form.value.publisher,
      yearPublished: form.value.yearPublished ? Number(form.value.yearPublished) : null,
      price: parsedPrice,
      coverUrl: form.value.coverUrl?.trim() || null,
      language: form.value.language,
      genre: genreArray,
    }

    const finalCategoryCode =
      categoryMain.value === '9.00.00'
        ? '9.00.00'
        : (categoryCode.value || categoryMain.value)

    // 1) najprej shrani book
    await axios.put(
      apiUrl(`/api/Book/edit/${id}`),
      payload,
      { headers: { Authorization: `Bearer ${token}` } }
    )

    if (copyId.value) {
      await axios.patch(
        apiUrl(`/api/Inventory/${copyId.value}`),
        {
          locationSet: true,
          location: mainFromCode(finalCategoryCode), // shrani X.00.00
          status: !!copyStatus.value
        },
        { headers: authHeaders.value }
      )
    } else {
      const createCopyRes = await axios.post(
        `${INVENTORY_API}/copy`,
        {
          bookId: id,
          location: mainFromCode(finalCategoryCode)
        },
        { headers: authHeaders.value }
      )
      copyId.value = createCopyRes.data?.id ?? createCopyRes.data?.Id ?? null
    }

        // 2) nato shrani kategorijo (ločeni endpoint)
    await axios.put(
      apiUrl(`/api/Book/${id}/category`),
      { categoryCode: finalCategoryCode },
      { headers: { Authorization: `Bearer ${token}` } }
    )

    let coverUploadFailed = false;
    if (coverFile.value) {
      try {
        const newUrl = await uploadCover();
        if (newUrl) form.value.coverUrl = newUrl;
        clearCoverSelection();
      } catch {
        coverUploadFailed = true;
      }
    }

    successMsg.value = coverUploadFailed
      ? t('editBook.success.updatedCoverFailed')
      : t('editBook.success.updated')
    status.value = 'ready'

    if (!coverUploadFailed) {
      setTimeout(() => router.push(`/book/${id}`), 800)
    }
  } catch (e) {
    console.error('SAVE failed:', {
      status: e?.response?.status,
      data: e?.response?.data,
      message: e?.message
    })

    status.value = 'ready'

    const serverMsg =
      (typeof e?.response?.data === 'string' && e.response.data) ||
      e?.response?.data?.message ||
      e?.response?.data?.title ||
      ''

    errorMsg.value = serverMsg
      ? t('editBook.errors.saveFailedWithReason', { reason: serverMsg })
      : t('editBook.errors.saveFailed')
  }
}


const cancel = () => router.back()
</script>

<template>
  <div class="editbook-wrapper">
    <div class="editbook-card surface">
      <h2 class="editbook-title section-title">{{ $t('editBook.title') }}</h2>
      <p class="editbook-subtitle">{{ $t('editBook.subtitle') }}</p>

      <div v-if="status === 'loading'" class="editbook-statebox empty-state">{{ $t('editBook.loading') }}</div>
      <div v-else-if="status === 'error'" class="editbook-statebox editbook-statebox--error empty-state">{{ errorMsg }}</div>

      <form v-else class="editbook-form" @submit.prevent="save">
        <div class="field">
          <label>{{ $t('editBook.fields.title') }}</label>
          <input v-model="form.title" required />
        </div>

        <div class="field">
          <label>{{ $t('editBook.fields.author') }}</label>
          <input v-model="form.author" required />
        </div>

        <div class="field">
          <label>{{ $t('editBook.fields.publisher') }}</label>
          <input v-model="form.publisher" />
        </div>

        <div class="field">
          <label>{{ $t('editBook.fields.year') }}</label>
          <input type="number" v-model.number="form.yearPublished" />
        </div>

        <div class="field">
          <label>{{ $t('editBook.fields.price') }}</label>
          <input type="number" min="0" step="0.01" v-model="form.price" />
        </div>

        <div class="field field--full">
          <label>{{ $t('editBook.fields.coverUpload') }}</label>
          <input
            ref="coverInput"
            type="file"
            accept="image/*"
            @change="onCoverSelected"
          />
          <div class="field-hint">{{ $t('editBook.fields.coverHint') }}</div>
          <div v-if="coverPreviewSrc" class="cover-preview">
            <img :src="coverPreviewSrc" :alt="$t('editBook.fields.coverPreviewAlt')" />
          </div>
        </div>

        <div class="field">
          <label>{{ $t('editBook.fields.language') }}</label>
          <input v-model="form.language" />
        </div>

        <div class="field">
          <label>{{ $t('editBook.fields.categoryMain') }}</label>
          <select v-model="categoryMain" :data-category="categoryMain.split('.')[0]">
            <option v-for="m in mainOption" :key="m.code" :value="m.code">
              {{ m.code }} — {{ $t(categoryNameKey(m.code)) }}
            </option>
          </select>
        </div>

        <div class="field" v-if="categoryMain !== '9.00.00'">
          <label>{{ $t('editBook.fields.subcategory') }}</label>
          <select v-model="categoryCode">
            <option value="">{{ $t('editBook.fields.subcategoryPlaceholder') }}</option>
            <option v-for="s in subOption" :key="s.code" :value="s.code">
              {{ s.code }} — {{ s.name }}
            </option>
          </select>

          <!-- DEBUG -->
          <div class="tiny muted">{{ $t('editBook.fields.subOptionsCount', { count: subOption.length }) }}</div>
        </div>

        <div class="field field--full">
          <label>{{ $t('editBook.fields.genres') }}</label>
          <input v-model="form.genreText" :placeholder="$t('editBook.fields.genresPlaceholder')" />
        </div>

        <div class="actions">
          <button class="btn btn--primary" type="submit" :disabled="status === 'saving'">{{ $t('editBook.actions.save') }}</button>
          <button class="btn btn--outline" type="button" @click="cancel">{{ $t('editBook.actions.cancel') }}</button>
        </div>

        <p v-if="successMsg" class="notice notice--success">{{ successMsg }}</p>
        <p v-if="errorMsg" class="notice notice--error">{{ errorMsg }}</p>
      </form>
    </div>
  </div>
</template>


<style scoped>
.editbook-wrapper {
  min-height: 100vh;
  padding: 16px;
  background: var(--bg);
  color: var(--text);
}

.editbook-card {
  max-width: 720px;
  margin: 0 auto;
  padding: 18px;
  border-radius: 18px;
  border: 1px solid var(--card-border);
  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: 0 18px 40px rgba(0, 0, 0, 0.35), inset 0 1px 0 rgba(255, 255, 255, 0.05);
}

.editbook-title {
  margin: 0;
  font-size: 22px;
  font-weight: 800;
}

.editbook-subtitle {
  margin: 6px 0 0;
  color: var(--page-subtitle);
  font-size: 13px;
}

.editbook-statebox {
  margin-top: 14px;
  padding: 14px;
  border-radius: 16px;
  border: 1px dashed var(--card-border);
  background: rgba(255, 255, 255, 0.05);
  color: var(--muted);
}

.editbook-statebox--error {
  border-style: solid;
  border-color: rgba(239, 68, 68, 0.35);
  background: rgba(239, 68, 68, 0.08);
  color: var(--text);
}

.editbook-form {
  margin-top: 14px;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.field--full {
  grid-column: 1 / -1;
}

.cover-preview {
  margin-top: 8px;
  max-width: 180px;
  border-radius: 12px;
  overflow: hidden;
  border: 1px solid var(--card-border);
  background: rgba(255,255,255,0.05);
}

.cover-preview img {
  width: 100%;
  height: 240px;
  object-fit: cover;
  display: block;
}

label {
  font-size: 12px;
  color: var(--muted);
  font-weight: 700;
}

input {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--select-text, var(--text));
  font-size: 14px;
  outline: none;
}

input::placeholder {
  color: var(--muted);
}

input:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.field-hint {
  font-size: 12px;
  color: var(--muted);
  margin-top: 2px;
}

/* actions */
.actions {
  grid-column: 1 / -1;
  display: flex;
  gap: 10px;
  margin-top: 6px;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;

  padding: 10px 14px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  cursor: pointer;
  font-weight: 800;
  font-size: 14px;
  transition: transform 0.12s ease, box-shadow 0.12s ease, background 0.12s ease, border-color 0.12s ease, opacity 0.12s ease;
}

.btn:active {
  transform: translateY(1px);
}

.btn--primary {
  color: #ffffff;
  border-color: rgba(124, 92, 255, 0.45);
  background: linear-gradient(135deg, var(--btn-gradient1), var(--btn-gradient2));
  box-shadow: 0 14px 28px rgba(124, 92, 255, 0.18);
}

.btn--primary:hover {
  box-shadow: 0 18px 38px rgba(124, 92, 255, 0.25);
}

.btn--outline {
  background: var(--nav-item-bg);
  color: var(--text);
}

.btn--outline:hover {
  background: var(--nav-item-hover);
  border-color: rgba(124, 92, 255, 0.35);
}

.btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.notice {
  grid-column: 1 / -1;
  margin-top: 6px;
  font-size: 13px;
  color: var(--muted);
}

.notice--success {
  color: var(--status-info-text);
}

.notice--error {
  color: var(--status-red-text);
}

select {
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--select-bg, var(--card-bg));
  color: var(--select-text, var(--text));
  font-size: 14px;
  outline: none;
}

select:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

select option{
  background-color: var(--option-bg);
}

@media (max-width: 760px) {
  .editbook-wrapper {
    padding: 12px;
  }

  .editbook-card {
    width: 100%;
    min-width: 0;
    padding: 14px;
    border-radius: 14px;
  }

  .editbook-form {
    grid-template-columns: 1fr;
    min-width: 0;
  }

  .field {
    min-width: 0;
  }

  input,
  select {
    width: 100%;
    max-width: 100%;
    box-sizing: border-box;
  }

  .actions {
    width: 100%;
    flex-direction: column;
  }

  .btn {
    min-width: 0;
    width: 100%;
    box-sizing: border-box;
  }
}
</style>





