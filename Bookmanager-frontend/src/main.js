import { createApp } from 'vue'
import { createI18n } from 'vue-i18n'
import App from './App.vue'
import router from './router'
import VueApexCharts from 'vue3-apexcharts'
import sl from './locales/sl.json'
import en from './locales/en.json'
import { registerSW } from 'virtual:pwa-register'

import '@/assets/main.css'
import 'primeicons/primeicons.css'

const APP_SUSPEND_EVENT = 'libraryapp:suspend'
const APP_RESUME_EVENT = 'libraryapp:resume'
const APP_IDLE_TIMEOUT_MS = Number(import.meta.env.VITE_IDLE_UNLOAD_MS)
let appIdleTimerId = null
let appIsSuspended = false

function clearAppIdleTimer() {
  if (appIdleTimerId !== null) {
    window.clearTimeout(appIdleTimerId)
    appIdleTimerId = null
  }
}

function isAwayFromApp() {
  return document.visibilityState !== 'visible' || !document.hasFocus()
}

function setAppSuspended(nextValue) {
  if (appIsSuspended === nextValue) return
  appIsSuspended = nextValue
  window.dispatchEvent(new Event(nextValue ? APP_SUSPEND_EVENT : APP_RESUME_EVENT))
}

function syncAppLifecycle() {
  if (isAwayFromApp()) {
    if (appIdleTimerId === null) {
      appIdleTimerId = window.setTimeout(() => {
        appIdleTimerId = null
        if (isAwayFromApp()) {
          setAppSuspended(true)
        }
      }, APP_IDLE_TIMEOUT_MS)
    }
    return
  }

  clearAppIdleTimer()
  setAppSuspended(false)
}

const savedTheme = localStorage.getItem('theme')
const initialLight = savedTheme !== 'dark'
if (!savedTheme) {
  localStorage.setItem('theme', 'light')
}
document.documentElement.classList.toggle('light', initialLight)

const i18n = createI18n({
  locale: 'sl',
  fallbackLocale: 'en',
  messages: {sl, en}
})

createApp(App)
  .use(i18n)
  .use(router)
  .use(VueApexCharts)
  .mount('#app')

registerSW({ immediate: true })

if (typeof window !== 'undefined') {
  const handleLifecycleChange = () => syncAppLifecycle()

  window.addEventListener('focus', handleLifecycleChange)
  window.addEventListener('blur', handleLifecycleChange)
  window.addEventListener('pageshow', handleLifecycleChange)
  window.addEventListener('pagehide', clearAppIdleTimer)
  document.addEventListener('visibilitychange', handleLifecycleChange)

  syncAppLifecycle()
}
