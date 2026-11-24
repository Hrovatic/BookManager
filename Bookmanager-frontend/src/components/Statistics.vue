<script setup>
import { onMounted, ref, computed, watch } from 'vue'
import axios from 'axios'
import { apiUrl } from '@/utils/api'
import { useI18n } from 'vue-i18n'

const { t, locale } = useI18n()

const INVENTORY_API = apiUrl('/api/inventory')
const STATS_API = apiUrl('/api/statistics')
const goal = 3500
const bookCount = ref(0)
const centerCounts = ref([])

const progressRatio = computed(() => {
  if (bookCount.value <= 0) return 0
  return Math.min(bookCount.value / goal, 1)
})

const progressPercent = computed(() => {
  if (bookCount.value >= goal) return 100
  return Math.floor(progressRatio.value * 100)
})

const progressStage = computed(() => {
  const p = progressPercent.value
  if (bookCount.value >= goal) return 'done'
  if (p < 25) return 'opening'
  if (p < 50) return 'filling'
  if (p < 75) return 'space'
  return 'almost'
})

const progressText = computed(() => {
  locale.value
  if (progressStage.value === 'done') return t('statistics.progress.done')
  if (progressStage.value === 'opening') return t('statistics.progress.opening')
  if (progressStage.value === 'filling') return t('statistics.progress.filling')
  if (progressStage.value === 'space') return t('statistics.progress.space')
  return t('statistics.progress.almost')
})

const progressIcon = computed(() => {
  if (progressStage.value === 'done') return '✅'
  if (progressStage.value === 'opening') return '📦'
  if (progressStage.value === 'filling') return '📗'
  if (progressStage.value === 'space') return '🧱'
  return '🚧'
})

// Tok podatkov:
// bookCount (API ali testni input) -> progressPercent -> progressWidth -> prikaz knjig in bara
// progressWidth je enoten vir resnice za širino napredka v vseh vizualnih delih.
const progressWidth = computed(() => {
  const rawPercent = progressRatio.value * 100
  if (rawPercent <= 0) return '0%'
  return `${Math.max(rawPercent, 0.8)}%`
})
// Največje število vizualnih knjig, ki jih pri 100% če smiselno prikažemo na polici.
// Večja vrednost pomeni bolj "gost" videz (več hrbtov na isti širini).
const maxShelfBooks = 72

const shelfBooks = computed(() => {
  if (bookCount.value <= 0) return []

  // število prikazanih knjig je sorazmerno napredku (0-100%).
  // Uporabimo ceil, da se ob majhnem napredku vidi vsaj 1 knjiga.
  const count = Math.max(1, Math.ceil((progressPercent.value / 100) * maxShelfBooks))
  return Array.from({ length: count }, (_, idx) => ({
    // Vsaki "knjigi" dodelimo rahlo drugačno višino/širino/naklon/barvo,
    // da polica ne izgleda preveč uniformno in umetno.
    h: 30 + ((idx * 7) % 18),
    w: 8 + ((idx * 3) % 7),
    rot: ((idx % 5) - 2) * 0.7,
    hue: (idx * 31) % 360,
    delay: `${idx * 60}ms`
  }))
})

// ------------------------------------
//               CHARTS
// ------------------------------------

const centerChartOptions = computed(() => ({
  labels: centerCounts.value.map(x => x.centerName),
  chart: {
    type: 'pie',
    height: 280,
    toolbar: { show: true },
    foreColor: 'var(--text)',
    offsetY: 8,
    parentHeightOffset: 0
  },
  legend: {
    position: 'right',
    horizontalAlign: 'center',
    fontSize: '13px',
    itemMargin: {
      horizontal: 12,
      vertical: 6
    },
    labels: {
      colors: centerCounts.value.map(() => 'var(--text)')
    }
  },
  stroke: {
    width: 2,
    colors: ['var(--text)']
  },
  dataLabels: {
    enabled: true,
    style: {
      fontSize: '13px',
      fontWeight: 700,
    }
  },
  plotOptions: {
    pie: {
      expandOnClick: true,
      customScale: 0.9,
      offsetX: 18
    }
  },
  responsive: [
    {
      breakpoint: 900,
      options: {
        legend: {
          position: 'bottom',
          horizontalAlign: 'center'
        },
        plotOptions: {
          pie: {
            customScale: 0.9,
            offsetX: 0
          }
        }
      }
    },
    {
      breakpoint: 700,
      options: {
        chart: {
          height: 248,
          toolbar: { show: false }
        },
        legend: {
          position: 'bottom',
          horizontalAlign: 'center'
        },
        plotOptions: {
          pie: {
            customScale: 0.82,
            offsetX: 0
          }
        }
      }
    }
  ],
  colors: ['#3b82f6', '#14b8a6', '#22c55e', '#eab308', '#f97316', '#ef4444', '#8b5cf6', '#ec4899']
}))

const centerChartSeries = computed(() =>
  centerCounts.value.map(x => x.copyCount)
)

const totalCopies = computed(() =>
  centerCounts.value.reduce((sum, x) => sum + (x.copyCount || 0), 0)
)

const growthBucket = ref('day')
const growthRange = ref('90d')
const currentUtcYear = new Date().getUTCFullYear()
const growthYear = ref('all')
const growthRows = ref([])
const growthLoading = ref(false)
const growthError = ref('')
const defaultZoomPoints = 30

const growthYearOptions = computed(() =>
  Array.from({ length: 8 }, (_, idx) => currentUtcYear - idx)
)

const isDayBucketDisabled = computed(() =>
  growthYear.value === 'all' && growthRange.value !== '90d'
)

const growthZoomRange = computed(() => {
  const points = growthRows.value
    .map(x => new Date(x.periodStartUtc).getTime())
    .filter(x => Number.isFinite(x))

  if (!points.length) return { min: undefined, max: undefined }

  const max = points[points.length - 1]
  const min = points.length > defaultZoomPoints
    ? points[points.length - defaultZoomPoints]
    : points[0]

  return { min, max }
})

const growthChartSeries = computed(() => ([
  {
    name: t('statistics.growth.series.total'),
    type: 'line',
    data: growthRows.value.map(x => [new Date(x.periodStartUtc).getTime(), x.total])
  },
  {
    name: t('statistics.growth.series.added'),
    type: 'column',
    data: growthRows.value.map(x => [new Date(x.periodStartUtc).getTime(), x.added])
  }
]))

const growthChartOptions = computed(() => ({
  chart: {
    type: 'line',
    height: 280,
    toolbar: { show: true },
    zoom: {
      enabled: true,
      autoScaleYaxis: true
    }
  },
  colors: ['#7c5cff', '#22c55e'],
  stroke: { width: [3, 0] },
  plotOptions: {
    bar: {
      columnWidth: '45%',
      borderRadius: 4
    }
  },
  dataLabels: { enabled: false },
  xaxis: {
    type: 'datetime',
    min: growthZoomRange.value.min,
    max: growthZoomRange.value.max
  },
  yaxis: [
    { title: { text: t('statistics.growth.yaxis.total') } },
    { opposite: true, title: { text: t('statistics.growth.yaxis.added') } }
  ],
  legend: {
    position: 'top',
    horizontalAlign: 'left'
  },
  grid: { borderColor: 'var(--card-border)' },
  tooltip: {
    shared: true,
    x: { format: growthBucket.value === 'day' ? 'dd MMM yyyy' : 'MMM yyyy' }
  },
  responsive: [
    {
      breakpoint: 700,
      options: {
        chart: {
          height: 238,
          toolbar: { show: false },
          zoom: { enabled: false }
        },
        legend: {
          position: 'bottom',
          horizontalAlign: 'center'
        },
        yaxis: [
          { title: { text: undefined } },
          { show: false }
        ]
      }
    }
  ]
}))

const growthTotalNow = computed(() => {
  if (!growthRows.value.length) return 0
  return growthRows.value[growthRows.value.length - 1].total || 0
})

const growthAddedInRange = computed(() =>
  growthRows.value.reduce((sum, x) => sum + (x.added || 0), 0)
)

const topSubcategories = ref([])
const topSubcategoriesLoading = ref(false)
const topSubcategoriesError = ref('')
const topSubcategoryCategoryLabels = computed(() =>
  topSubcategories.value.map((row) => {
    const code = row?.categoryCode || '-'
    return code
  })
)
const topSubcategoryLegendItems = computed(() =>
  topSubcategories.value.map((row, index) => {
    const code = row?.categoryCode || '-'
    const fullName = (row?.categoryName || '').trim() || code
    return {
      key: `${code}-${index}`,
      code,
      fullName
    }
  })
)
const topSubcategoryMax = computed(() =>
  Math.max(1, ...topSubcategories.value.map(x => Number(x.addedCopies) || 0))
)

const topSubcategorySeries = computed(() => ([
  {
    name: t('statistics.topSubcategories.series.addedCopies'),
    data: topSubcategories.value.map(x => Number(x.addedCopies) || 0)
  }
]))

const topSubcategoryOptions = computed(() => ({
  chart: {
    type: 'bar',
    height: 280,
    toolbar: { show: true },
    foreColor: 'var(--text)',
    animations: {
      enabled: true,
      easing: 'easeout',
      speed: 420
    }
  },
  plotOptions: {
    bar: {
      horizontal: true,
      borderRadius: 10,
      borderRadiusApplication: 'end',
      barHeight: '58%',
      distributed: false,
      colors: {
        backgroundBarColors: ['rgba(148, 163, 184, 0.22)'],
        backgroundBarOpacity: 0.32
      }
    }
  },
  colors: ['#4f7cff'],
  dataLabels: {
    enabled: false
  },
  xaxis: {
    min: 0,
    max: Math.ceil(topSubcategoryMax.value * 1.15),
    tickAmount: 4,
    categories: topSubcategoryCategoryLabels.value,
    title: { text: t('statistics.topSubcategories.xAxisTitle') },
    labels: {
      formatter: (value) => `${Math.round(Number(value) || 0)}`,
      style: {
        colors: ['var(--muted)']
      }
    }
  },
  yaxis: {
    labels: {
      maxWidth: 170,
      style: {
        colors: ['var(--text)'],
        fontSize: '12px',
        fontWeight: 700
      }
    }
  },
  tooltip: {
    x: {
      formatter: (_label, ctx) => {
        const row = topSubcategories.value[ctx.dataPointIndex]
        const code = row?.categoryCode || '-'
        if (row?.categoryName) return `${code}: ${row.categoryName}`
        return code
      }
    },
    y: {
      formatter: (value, ctx) => {
        const row = topSubcategories.value[ctx.dataPointIndex]
        if (row?.categoryName) return `${value} ${t('statistics.topSubcategories.tooltipSuffix')}`
        return `${value} ${t('statistics.topSubcategories.tooltipSuffix')}`
      }
    }
  },
  grid: {
    borderColor: 'var(--card-border)',
    strokeDashArray: 3
  },
  states: {
    hover: {
      filter: {
        type: 'none',
        value: 0
      }
    }
  },
  responsive: [
    {
      breakpoint: 700,
      options: {
        chart: {
          height: 238,
          toolbar: { show: false }
        },
        plotOptions: {
          bar: {
            barHeight: '62%'
          }
        },
        yaxis: {
          labels: {
            maxWidth: 120,
            style: {
              fontSize: '11px'
            }
          }
        }
      }
    }
  ]
}))

const topSubcategoriesTotal = computed(() =>
  topSubcategories.value.reduce((sum, x) => sum + (x.addedCopies || 0), 0)
)

const growthPercentSeries = computed(() => {
  if (!growthRows.value.length) {
    return [{ name: t('statistics.growthPercent.series.addedPercent'), data: [] }]
  }

  return [{
    name: t('statistics.growthPercent.series.addedPercent'),
    data: growthRows.value.map(x => {
      const ts = new Date(x.periodStartUtc).getTime()
      // Isti časovni potek kot "Dodano", samo normaliziran v %.
      const pct = x.total > 0 ? (x.added / x.total) * 100 : 0
      return [ts, pct]
    })
  }]
})

const growthPercentNow = computed(() => {
  if (!growthPercentSeries.value[0].data.length) return 0
  return growthPercentSeries.value[0].data[growthPercentSeries.value[0].data.length - 1][1]
})

function formatGrowthPercent(value) {
  const n = Number(value || 0)
  if (Math.abs(n) < 1) return `${n.toFixed(4)}%`
  return `${n.toFixed(2)}%`
}

const growthPercentChartOptions = computed(() => ({
  chart: {
    type: 'line',
    height: 280,
    toolbar: { show: true },
    zoom: {
      enabled: true,
      autoScaleYaxis: true
    }
  },
  colors: ['#7c5cff'],
  stroke: {
    width: 3,
    curve: 'smooth'
  },
  dataLabels: { enabled: false },
  xaxis: {
    type: 'datetime',
    min: growthZoomRange.value.min,
    max: growthZoomRange.value.max
  },
  yaxis: {
    title: { text: t('statistics.growthPercent.yaxis.addedPercent') },
    tooltip: { enabled: false },
    labels: {
      formatter: (value) => formatGrowthPercent(value)
    }
  },
  legend: {
    position: 'top',
    horizontalAlign: 'left'
  },
  grid: { borderColor: 'var(--card-border)' },
  tooltip: {
    x: { format: growthBucket.value === 'day' ? 'dd MMM yyyy' : 'MMM yyyy' },
    y: {
      formatter: (value) => formatGrowthPercent(value)
    }
  },
  responsive: [
    {
      breakpoint: 700,
      options: {
        chart: {
          height: 238,
          toolbar: { show: false },
          zoom: { enabled: false }
        },
        legend: {
          position: 'bottom',
          horizontalAlign: 'center'
        },
        yaxis: {
          title: { text: undefined },
          tooltip: { enabled: false },
          labels: {
            formatter: (value) => formatGrowthPercent(value)
          }
        }
      }
    }
  ]
}))

function getGrowthQueryRange() {
  if (growthYear.value !== 'all') {
    const y = Number(growthYear.value)
    if (!Number.isNaN(y)) {
      const from = new Date(Date.UTC(y, 0, 1, 0, 0, 0, 0))
      const to = new Date(Date.UTC(y, 11, 31, 23, 59, 59, 999))
      return { fromUtc: from.toISOString(), toUtc: to.toISOString() }
    }
  }

  const now = new Date()

  if (growthRange.value === 'all') {
    return { fromUtc: null, toUtc: null }
  }

  if (growthRange.value === '90d') {
    const from = new Date(now)
    from.setUTCDate(from.getUTCDate() - 89)
    from.setUTCHours(0, 0, 0, 0)
    return { fromUtc: from.toISOString(), toUtc: null }
  }

  if (growthRange.value === '12m') {
    const from = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 11, 1, 0, 0, 0))
    return { fromUtc: from.toISOString(), toUtc: null }
  }

  const from = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 23, 1, 0, 0, 0))
  return { fromUtc: from.toISOString(), toUtc: null }
}

// ------------------------------------
//            END OF CHARTS
// ------------------------------------

async function loadCenterCounts() {
  const res = await axios.get(`${INVENTORY_API}/counts-by-center`)
  centerCounts.value = Array.isArray(res.data) ? res.data : []
  bookCount.value = centerCounts.value.reduce((sum, x) => sum + (Number(x.copyCount) || 0), 0)
}

async function loadBookGrowth() {
  growthLoading.value = true
  growthError.value = ''

  try {
    const { fromUtc, toUtc } = getGrowthQueryRange()
    const params = { bucket: growthBucket.value }
    if (fromUtc) params.fromUtc = fromUtc
    if (toUtc) params.toUtc = toUtc

    const res = await axios.get(`${STATS_API}/book-growth`, {
      params
    })
    growthRows.value = Array.isArray(res.data) ? res.data : []
  } catch (err) {
    growthRows.value = []
    growthError.value = t('statistics.errors.growthLoad')
    console.error(err)
  } finally {
    growthLoading.value = false
  }
}

async function loadTopSubcategories() {
  topSubcategoriesLoading.value = true
  topSubcategoriesError.value = ''

  try {
    const { fromUtc, toUtc } = getGrowthQueryRange()
    const params = { top: 10 }
    if (fromUtc) params.fromUtc = fromUtc
    if (toUtc) params.toUtc = toUtc

    const res = await axios.get(`${STATS_API}/top-subcategories`, { params })
    topSubcategories.value = Array.isArray(res.data) ? res.data : []
  } catch (err) {
    topSubcategories.value = []
    topSubcategoriesError.value = t('statistics.errors.subcategoriesLoad')
    console.error(err)
  } finally {
    topSubcategoriesLoading.value = false
  }
}

async function loadGrowthCharts() {
  await Promise.all([
    loadBookGrowth(),
    loadTopSubcategories()
  ])
}

watch(growthBucket, () => {
  loadGrowthCharts()
})

watch(growthRange, (nextRange) => {
  if (growthYear.value === 'all' && nextRange !== '90d' && growthBucket.value === 'day') {
    growthBucket.value = 'month'
    return
  }

  loadGrowthCharts()
})

watch(growthYear, (nextYear) => {
  if (nextYear === 'all' && growthRange.value !== '90d' && growthBucket.value === 'day') {
    growthBucket.value = 'month'
    return
  }

  loadGrowthCharts()
})

onMounted(async () => {
  await Promise.all([
    loadCenterCounts(),
    loadGrowthCharts()
  ])
})
</script>

<template>
  <section class="stats-page">
    <div class="stats-card surface">
      <header class="stats-head">
        <h1 class="stats-title section-title">{{ $t('statistics.title') }}</h1>
        <p class="stats-subtitle">
          {{ $t('statistics.current') }}: <b>{{ bookCount }}</b> / <b>{{ goal }}</b> {{ $t('statistics.booksLabel') }}
        </p>
      </header>
        <div class="stats-shelf-stage">
        <div
          class="stats-shelf-progress"
          role="progressbar"
          :aria-valuenow="bookCount"
          aria-valuemin="0"
          :aria-valuemax="goal"
        >
          <div class="stats-shelf-progress-fill" :style="{ width: progressWidth }" aria-hidden="true"></div>

          <!-- Maska reže knjige na trenutno širino napredka -->
          <div class="stats-shelf-books" :style="{ width: progressWidth }" aria-hidden="true">
            <div class="stats-books-row">
              <span
                v-for="(book, idx) in shelfBooks"
                :key="idx"
                class="shelf-book"
                :style="{
                  '--h': `${book.h}px`,
                  '--w': `${book.w}px`,
                  '--rot': `${book.rot}deg`,
                  '--hue': book.hue,
                  '--delay': book.delay
                }"
              ></span>
            </div>
          </div>

          <div class="stats-shelf-plank" aria-hidden="true"></div>
        </div>
      </div>

      <div class="stats-meta">
        <span class="stats-percent">{{ progressPercent }}%</span>
        <span class="stats-goal">
          {{ goal - bookCount > 0 ? $t('statistics.progress.toGoal', { count: goal - bookCount }) : $t('statistics.progress.goalReached') }}
        </span>
      </div>

      <p class="stats-message">
        <span class="stats-message-icon" aria-hidden="true">
          <span>{{ progressIcon }}</span>
        </span>
        <span>{{ progressText }}</span>
      </p>
    </div>

    <section class="stats-graphs">
      <!--     GRAF 1 ZGORAJ LEVO      -->
      <div class="stats-graph-card stats-graph-card--centers surface GraphCardFadeIn" style="--d: 0ms">
        <h2 class="stats-graph-title section-title">{{ $t('statistics.centers.title') }}</h2>
        <div class="stats-center-chart-wrap stats-chart-scroll">
          <apexchart class="stats-center-chart stats-chart" :options="centerChartOptions" :series="centerChartSeries"/>
        </div>
        <div class="stats-summary-card stats-summary-card--centers">
            <h3 class="stats-summary-title">{{ $t('statistics.centers.summaryTitle') }}</h3>
            <p class="stats-summary-value">{{ totalCopies }}</p>
            <p class="stats-summary-note">{{ $t('statistics.centers.summaryNote') }}</p>
        </div>
      </div>

      <!--  GRAF 2 ZGORAJ DESNO  -->
      <div class="stats-graph-card stats-graph-card--subcats surface GraphCardFadeIn" style="--d: 240ms">
        <h2 class="stats-graph-title section-title">{{ $t('statistics.topSubcategories.title') }}</h2>
        <p class="stats-graph-note">{{ $t('statistics.topSubcategories.note') }}</p>
        <p v-if="topSubcategoriesError" class="stats-error">{{ topSubcategoriesError }}</p>
        <div v-else class="stats-chart-scroll stats-chart-scroll--wide">
          <apexchart
            class="stats-chart"
            :options="topSubcategoryOptions"
            :series="topSubcategorySeries"
          />
        </div>
        <div v-if="topSubcategoryLegendItems.length" class="stats-subcat-legend">
          <div
            v-for="item in topSubcategoryLegendItems"
            :key="item.key"
            class="stats-subcat-legend-item"
          >
            <span class="stats-subcat-legend-swatch" aria-hidden="true"></span>
            <div class="stats-subcat-legend-text">
              <span class="stats-subcat-legend-code">{{ item.code }}</span>
              <span class="stats-subcat-legend-name">{{ item.fullName }}</span>
            </div>
          </div>
        </div>
        <div class="stats-growth-meta">
          <span>{{ $t('statistics.topSubcategories.metaTotalTop10') }}: <b>{{ topSubcategoriesTotal }}</b></span>
          <span>{{ $t('statistics.topSubcategories.metaCount') }}: <b>{{ topSubcategories.length }}</b></span>
        </div>
      </div>

      <!--   GRAF 3 SPODAJ LEVO  -->
      <div class="stats-graph-card surface GraphCardFadeIn" style="--d: 360ms">
        <div class="stats-graph-head">
          <h2 class="stats-graph-title section-title">{{ $t('statistics.growth.title') }}</h2>
          <div class="stats-graph-controls">
            <select v-model="growthYear" class="stats-select">
              <option value="all">{{ $t('statistics.growth.yearAll') }}</option>
              <option
                v-for="year in growthYearOptions"
                :key="year"
                :value="String(year)"
              >
                {{ year }}
              </option>
            </select>
            <select v-model="growthRange" class="stats-select" :disabled="growthYear !== 'all'">
              <option value="90d">{{ $t('statistics.growth.range90d') }}</option>
              <option value="12m">{{ $t('statistics.growth.range12m') }}</option>
              <option value="24m">{{ $t('statistics.growth.range24m') }}</option>
              <option value="all">{{ $t('statistics.growth.rangeAll') }}</option>
            </select>
            <select v-model="growthBucket" class="stats-select">
              <option value="month">{{ $t('statistics.growth.bucketMonth') }}</option>
              <option value="day" :disabled="isDayBucketDisabled">{{ $t('statistics.growth.bucketDay') }}</option>
            </select>
            <button type="button" class="stats-refresh-btn" @click="loadGrowthCharts" :disabled="growthLoading || topSubcategoriesLoading">
              {{ (growthLoading || topSubcategoriesLoading) ? $t('statistics.growth.loading') : $t('statistics.growth.refresh') }}
            </button>
          </div>
        </div>

        <p v-if="growthError" class="stats-error">{{ growthError }}</p>
        <div v-else class="stats-chart-scroll stats-chart-scroll--wide">
          <apexchart
            class="stats-chart"
            :options="growthChartOptions"
            :series="growthChartSeries"
          />
        </div>

        <div class="stats-growth-meta">
          <span>{{ $t('statistics.growth.metaTotalNow') }}: <b>{{ growthTotalNow }}</b></span>
          <span>{{ $t('statistics.growth.metaAddedRange') }}: <b>{{ growthAddedInRange }}</b></span>
        </div>
      </div>

      <!--  GRAF 4 SPODAJ DESNO  -->
      <div class="stats-graph-card surface GraphCardFadeIn" style="--d: 480ms">
        <h2 class="stats-graph-title section-title">{{ $t('statistics.growthPercent.title') }}</h2>
        <p class="stats-graph-note">{{ $t('statistics.growthPercent.note') }}</p>
        <div class="stats-chart-scroll stats-chart-scroll--wide">
          <apexchart
            class="stats-chart"
            :options="growthPercentChartOptions"
            :series="growthPercentSeries"
          />
        </div>
        <div class="stats-growth-meta">
          <span>{{ $t('statistics.growthPercent.metaLastPeriod') }}: <b>{{ formatGrowthPercent(growthPercentNow) }}</b></span>
          <span>{{ $t('statistics.growthPercent.metaTotalNow') }}: <b>{{ growthTotalNow }}</b></span>
        </div>
      </div>
    </section>
  </section>
</template>

<style scoped>
/*-------------------------------------
  Animations
-------------------------------------*/
.GraphCardFadeIn{
  animation: fadeDown 0.5s ease forwards;
  animation-delay: var(--d, 0ms);
  opacity: 0;
}

@keyframes fadeDown {
  0% {
    opacity: 0;
    transform: translateY(10px);
  }
  100% {
    opacity: 1;
    transform: translateY(0);
  }
}

/*-------------------------------------
  Normal styles
-------------------------------------*/
.stats-page {
  max-width: 980px;
  margin: 0 auto;
  padding: 18px;
}

.stats-card {
  border-radius: 16px;
  padding: 18px;
  border: 1px solid var(--card-border);
  background: linear-gradient(180deg, var(--card-bg-gradient1), var(--card-bg-gradient2));
  box-shadow: var(--shadow-elev-1);
}

.stats-head {
  margin-bottom: 14px;
}

.stats-title {
  margin: 0;
  font-size: 24px;
}

.stats-subtitle {
  margin: 6px 0 0;
  color: var(--muted);
  font-size: 14px;
}

.stats-shelf-stage {
  position: relative;
  margin-top: 8px;
}

.stats-test {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 10px;
}

.stats-test label {
  font-size: 13px;
  color: var(--muted);
  font-weight: 700;
}

.stats-test input {
  width: 120px;
  padding: 8px 10px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
  outline: none;
}

.stats-test input:focus {
  border-color: var(--nav-item-active-border);
  box-shadow: 0 0 0 4px rgba(124, 92, 255, 0.18);
}

.stats-shelf-progress {
  position: relative;
  width: 100%;
  height: 66px;
  overflow: hidden;
}

.stats-shelf-books {
  position: absolute;
  left: 0;
  bottom: 14px;
  height: 48px;
  overflow: hidden;
  transition: width 0.35s ease;
  z-index: 3;
}

.stats-shelf-progress-fill {
  position: absolute;
  left: 0;
  bottom: 8px;
  height: 8px;
  min-width: 2px;
  border-radius: 999px;
  background:
    linear-gradient(90deg, rgba(124, 92, 255, 0.22), rgba(91, 62, 228, 0.2)),
    linear-gradient(90deg, var(--btn-gradient1), var(--btn-gradient2));
  box-shadow: 0 2px 10px rgba(124, 92, 255, 0.28);
  transition: width 0.35s ease;
  z-index: 2;
}

.stats-books-row {
  display: flex;
  align-items: flex-end;
  gap: 2px;
  height: 48px;
  padding-left: 0;
  padding-right: 0;
}

.stats-shelf-plank {
  position: absolute;
  left: 0;
  right: 0;
  bottom: 8px;
  height: 8px;
  border-radius: 999px;
  border: 1px solid var(--card-border);
  background: rgba(255, 255, 255, 0.03);
  z-index: 3;
}

/* En vizualni hrbet knjige na polici.
   Dimenzije, naklon in barva prihajajo iz inline CSS spremenljivk iz template-a. */
.shelf-book {
  width: var(--w);
  height: var(--h);
  border-radius: 2px 2px 0 0;
  border: 1px solid rgba(0, 0, 0, 0.2);
  border-bottom: none;
  transform: rotate(var(--rot));
  transform-origin: bottom center;
  background:
    linear-gradient(180deg, rgba(255, 255, 255, 0.28), rgba(0, 0, 0, 0.18)),
    linear-gradient(90deg, hsl(var(--hue) 75% 58%), hsl(calc(var(--hue) + 22) 75% 48%));
  box-shadow: 0 2px 7px rgba(0, 0, 0, 0.22);
  position: relative;
  animation: shelfBob 2.8s ease-in-out infinite;
  animation-delay: var(--delay);
}

.shelf-book::before {
  content: '';
  position: absolute;
  left: 35%;
  top: 5px;
  width: 2px;
  height: calc(100% - 12px);
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.38);
}

.shelf-book::after {
  content: '';
  position: absolute;
  right: 2px;
  top: 6px;
  width: 3px;
  height: 8px;
  border-radius: 999px;
  background: rgba(255, 255, 255, 0.28);
}

.stats-meta {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 12px;
  gap: 10px;
}

.stats-percent {
  font-size: 15px;
  font-weight: 800;
  color: var(--text);
}

.stats-goal {
  color: var(--muted);
  font-size: 13px;
}

.stats-message {
  margin-top: 12px;
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
  font-weight: 600;
  display: flex;
  align-items: center;
  gap: 8px;
}

.stats-message-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  font-size: 18px;
  line-height: 1;
}

.stats-graphs {
  margin-top: 14px;
  width: min(75vw, 1400px);
  margin-left: 50%;
  transform: translateX(-50%);
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.stats-graph-card {
  min-height: 300px;
  min-width: 0;
  border: 1px dashed var(--card-border);
  background: var(--card-bg);
  border-radius: 14px;
  padding: 14px;
}

.stats-chart-scroll {
  width: 100%;
}

.stats-chart {
  width: 100%;
}

.stats-graph-card :deep(.apexcharts-canvas),
.stats-graph-card :deep(.apexcharts-svg) {
  overflow: hidden !important;
}

.stats-graph-title {
  margin: 0 0 10px;
  font-size: 16px;
  font-weight: 800;
}

.stats-graph-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  margin-bottom: 8px;
}

.stats-graph-head .stats-graph-title {
  margin-bottom: 0;
}

.stats-graph-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}

.stats-select {
  border: 1px solid var(--card-border);
  border-radius: 10px;
  background: var(--select-bg);
  color: var(--select-text);
  padding: 7px 10px;
  font-size: 13px;
}

.stats-refresh-btn {
  border: 1px solid var(--card-border);
  border-radius: 10px;
  background: var(--select-bg);
  color: var(--select-text);
  padding: 7px 10px;
  font-size: 13px;
  font-weight: 700;
  cursor: pointer;
}

.stats-select:hover,
.stats-refresh-btn:hover {
  background: var(--nav-item-hover);
}

.stats-select option {
  background: var(--option-bg);
  color: var(--option-text);
}

.stats-refresh-btn:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}

/* Laptop layout: keep graph title on first row, controls on second row */
@media (min-width: 701px) and (max-width: 1366px) {
  .stats-graph-head {
    flex-direction: column;
    align-items: flex-start;
  }

  .stats-graph-head .stats-graph-title {
    width: 100%;
  }

  .stats-graph-controls {
    width: 100%;
    flex-wrap: wrap;
  }
}

.stats-growth-meta {
  margin-top: 10px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  color: var(--muted);
  font-size: 12px;
}

.stats-error {
  margin: 8px 0;
  color: #ef4444;
  font-size: 13px;
  font-weight: 700;
}

.stats-summary-card {
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
}

.stats-summary-title {
  margin: 0;
  font-size: 14px;
  color: var(--muted);
  font-weight: 700;
}

.stats-summary-value {
  margin: 8px 0 0;
  font-size: 42px;
  line-height: 1;
  font-weight: 900;
  color: var(--text);
}

.stats-summary-note {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--muted);
}

.stats-graph-card--centers {
  display: flex;
  flex-direction: column;
  overflow-x: hidden;
}

.stats-center-chart-wrap {
  flex: 1;
  min-height: 280px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.stats-center-chart {
  width: 100%;
}

.stats-summary-card--centers {
  margin-top: auto;
  align-self: flex-start;
}

.stats-graph-card--centers :deep(.apexcharts-canvas) {
  margin-inline: auto !important;
  max-width: 100% !important;
  overflow: hidden !important;
}

.stats-graph-card--centers :deep(.apexcharts-svg) {
  max-width: 100% !important;
  overflow: hidden !important;
}

.stats-graph-card--centers :deep(.apexcharts-legend) {
  overflow: hidden !important;
}

.stats-graph-note {
  margin: -2px 0 8px;
  color: var(--muted);
  font-size: 12px;
}

.stats-graph-card--subcats {
  border: 1px dashed var(--card-border);
  background: var(--card-bg);
}

.stats-graph-card--subcats .stats-graph-title {
  margin-bottom: 6px;
}

.stats-graph-card--subcats :deep(.apexcharts-bar-area) {
  transition: filter 0.2s ease, opacity 0.2s ease;
}

.stats-graph-card--subcats :deep(.apexcharts-yaxis-label) {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.1px;
}

.stats-subcat-legend {
  margin-top: 8px;
  border: 1px solid var(--card-border);
  border-radius: 12px;
  background: linear-gradient(180deg, var(--card-bg), var(--card-bg-gradient2));
  padding: 10px;
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 10px;
}

.stats-subcat-legend-item {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  min-width: 0;
  padding: 6px 8px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
}

.stats-subcat-legend-swatch {
  width: 9px;
  height: 9px;
  border-radius: 999px;
  margin-top: 4px;
  background: #4f7cff;
  box-shadow: 0 0 0 2px rgba(79, 124, 255, 0.2);
  flex: 0 0 auto;
}

.stats-subcat-legend-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.stats-subcat-legend-code {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
  font-size: 11px;
  font-weight: 700;
  color: var(--muted);
  white-space: nowrap;
}

.stats-subcat-legend-name {
  font-size: 12px;
  color: var(--text);
  line-height: 1.3;
  overflow-wrap: anywhere;
}

.stats-graph-card--subcats :deep(.apexcharts-gridline) {
  opacity: 0.65;
}

.stats-graph-card--subcats :deep(.apexcharts-tooltip),
.stats-graph-card--subcats :deep(.apexcharts-xaxistooltip),
.stats-graph-card--subcats :deep(.apexcharts-yaxistooltip),
.stats-graph-card--subcats :deep(.apexcharts-tooltip-series-group) {
  opacity: 1 !important;
  background: var(--modal-bg) !important;
}

:deep(.apexcharts-legend-text),
:deep(.apexcharts-data-labels text) {
  fill: var(--text) !important;
  color: var(--text) !important;
}

:deep(.apexcharts-xaxis-label),
:deep(.apexcharts-yaxis-label),
:deep(.apexcharts-yaxis-title text),
:deep(.apexcharts-xaxis-title text) {
  fill: var(--text) !important;
}

:deep(.apexcharts-gridline) {
  stroke: var(--card-border) !important;
}

:deep(.apexcharts-tooltip),
:deep(.apexcharts-xaxistooltip),
:deep(.apexcharts-yaxistooltip) {
  background: var(--select-bg) !important;
  color: var(--select-text) !important;
  border: 1px solid var(--card-border) !important;
  box-shadow: var(--shadow-elev-1) !important;
}

:deep(.apexcharts-tooltip-title) {
  background: var(--nav-item-hover) !important;
  color: var(--select-text) !important;
  border-bottom: 1px solid var(--card-border) !important;
}

/* ApexCharts toolbar/menu: light theme stays default, dark gets themed surface colors. */
:global(:root:not(.light) .apexcharts-toolbar) {
  background: var(--modal-bg);
  border: 1px solid var(--card-border);
  border-radius: 8px;
  padding: 2px;
}

:global(:root:not(.light) .apexcharts-menu) {
  background: var(--modal-bg) !important;
  border: 1px solid var(--card-border) !important;
  box-shadow: var(--shadow-elev-1) !important;
}

:global(:root:not(.light) .apexcharts-menu.apexcharts-menu-open) {
  opacity: 1 !important;
  pointer-events: auto !important;
}

:global(:root:not(.light) .apexcharts-menu-item) {
  color: var(--text) !important;
}

:global(:root:not(.light) .apexcharts-menu-item:hover) {
  background: var(--nav-item-hover) !important;
}

@keyframes shelfBob {
  0% {
    transform: rotate(var(--rot)) translateY(0);
  }
  50% {
    transform: rotate(var(--rot)) translateY(-1px);
  }
  100% {
    transform: rotate(var(--rot)) translateY(0);
  }
}

@media (prefers-reduced-motion: reduce) {
  .shelf-book {
    animation: none;
  }

  .GraphCardFadeIn {
    animation: none;
    opacity: 1;
    transform: none;
  }
}

@media (max-width: 700px) {
  .stats-page {
    padding: 12px;
  }

  .stats-card {
    padding: 14px;
  }

  .stats-title {
    font-size: 20px;
  }
  .stats-shelf-progress {
    height: 62px;
  }

  .stats-books-row {
    gap: 1px;
  }

  .stats-graphs {
    width: 100%;
    margin-left: 0;
    transform: none;
    grid-template-columns: 1fr;
  }

  .stats-graph-head {
    flex-direction: column;
    align-items: flex-start;
  }

  .stats-graph-card {
    padding: 12px 10px;
    border-radius: 12px;
  }

  .stats-center-chart-wrap {
    min-height: 248px;
  }

  .stats-graph-controls {
    width: 100%;
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 8px;
  }

  .stats-graph-controls > * {
    width: 100%;
    min-width: 0;
  }

  .stats-refresh-btn {
    grid-column: 1 / -1;
  }

  .stats-graph-card :deep(.apexcharts-canvas),
  .stats-graph-card :deep(.apexcharts-svg) {
    max-width: 100% !important;
  }

  .stats-chart-scroll {
    overflow-x: hidden;
    overflow-y: visible;
    -webkit-overflow-scrolling: touch;
    padding-bottom: 0;
    touch-action: pan-y;
    pointer-events: none;
  }

  .stats-chart-scroll--wide :deep(.apexcharts-canvas),
  .stats-chart-scroll--wide :deep(.apexcharts-svg) {
    min-width: 100% !important;
  }

  .stats-center-chart-wrap :deep(.apexcharts-canvas),
  .stats-center-chart-wrap :deep(.apexcharts-svg) {
    min-width: 100% !important;
  }

  .stats-graph-card :deep(.apexcharts-canvas),
  .stats-graph-card :deep(.apexcharts-svg),
  .stats-graph-card :deep(.apexcharts-inner) {
    touch-action: pan-y !important;
  }

  /* iOS/Safari fallback: don't let chart gestures block page scroll */
  .stats-graph-card :deep(.apexcharts-canvas),
  .stats-graph-card :deep(.apexcharts-svg),
  .stats-graph-card :deep(.apexcharts-inner),
  .stats-graph-card :deep(.apexcharts-legend),
  .stats-graph-card :deep(.apexcharts-toolbar) {
    pointer-events: none !important;
  }

  .stats-growth-meta {
    flex-direction: column;
    align-items: flex-start;
  }

  .stats-subcat-legend {
    grid-template-columns: 1fr;
  }
}
</style>
