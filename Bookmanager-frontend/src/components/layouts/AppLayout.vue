<script setup>
import { useRouter, useRoute } from 'vue-router'
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import axios from 'axios'
import { toggleThemeWithTransition } from '@/themeTransition'
import { apiUrl } from '@/utils/api'

const router = useRouter()
const route = useRoute()

const menuOpen = ref(false)
const profileMenuOpen = ref(false)

const noPadRoutes = computed(() => ['home', 'login', 'register'].includes(route.name))
const hideHomeButton = computed(() => {
  return noPadRoutes.value
})

const profileMenuRef = ref(null)
const profileButtonRef = ref(null)

const isAdmin = computed(() => localStorage.getItem('role') === 'admin')
const user = computed(() => {
  const raw = localStorage.getItem('user')
  if (!raw) return null
  try {
    return JSON.parse(raw)
  } catch {
    return null
  }
})

// Added by Codex: Keep the primary navigation together so the shared drawer and active states stay easy to maintain.
const userMenuLinks = [
  { to: '/showbooks', icon: 'pi pi-book', labelKey: 'nav.books' },
  { to: '/borrowings', icon: 'pi pi-bookmark', labelKey: 'nav.borrowed' },
  { to: '/wishlist', icon: 'pi pi-shopping-cart', labelKey: 'nav.wishlist' },
  { to: '/statistics', icon: 'pi pi-chart-line', labelKey: 'nav.statistics' }
]

// Added by Codex: Keep the admin section visually separated while using the same drawer component and item styling.
const adminMenuLinks = [
  { to: '/adduser', icon: 'pi pi-user-plus', labelKey: 'nav.addUser' },
  { to: '/admin-users', icon: 'pi pi-users', labelKey: 'nav.manageUsers' },
  { to: '/audit-logs', icon: 'pi pi-history', labelKey: 'nav.auditLogs' },
  { to: '/addbook', icon: 'pi pi-plus-circle', labelKey: 'nav.addBook' },
  { to: '/inventory', icon: 'pi pi-warehouse', labelKey: 'nav.inventory' },
  { to: '/price-review', icon: 'pi pi-euro', labelKey: 'nav.priceReview' },
  { to: '/admin-centers', icon: 'pi pi-building', labelKey: 'nav.manageCenters' }

]

// THEME
const isLight = ref(false)

function applyTheme(light) {
  isLight.value = light
  localStorage.setItem('theme', light ? 'light' : 'dark')
  document.documentElement.classList.toggle('light', light)
}

onMounted(() => {
  const savedTheme = localStorage.getItem('theme')
  applyTheme(savedTheme !== 'dark')
})

watch(route, () => {
  menuOpen.value = false
  profileMenuOpen.value = false
  const savedTheme = localStorage.getItem('theme')
  const light = savedTheme !== 'dark'
  if (isLight.value !== light) {
    applyTheme(light)
  }
}) // ko menjaš stran, zapri meni

// LANGUAGE
const { locale } = useI18n()

onMounted(() => {
  const savedLang = localStorage.getItem('lang')
  if (savedLang === 'sl' || savedLang === 'en') {
    locale.value = savedLang
  }
})

watch(locale, (newLang) => {
  localStorage.setItem('lang', newLang)
})

function toggleTheme(event) {
  toggleThemeWithTransition(!isLight.value, applyTheme, event)
}

const themeIconClass = computed(() =>
  isLight.value ? 'pi pi-moon' : 'pi pi-sun'
)

import slFlag from '@/assets/Images/slFlag.webp'
import enFlag from '@/assets/Images/enFlag.webp'

const currentFlag = computed(() => {
  return locale.value === 'sl' ? slFlag : enFlag
})

async function persistPreferredLanguage(lang) {
  const token = localStorage.getItem('token')
  if (!token) return

  const authHeader = token.startsWith('Bearer ') ? token : `Bearer ${token}`

  try {
    await axios.post(
      apiUrl('/Users/preferred-language'),
      { language: lang },
      { headers: { Authorization: authHeader } }
    )
  } catch {
    // keep UI responsive even if sync fails
  }
}

async function toggleLanguage() {
  const next = locale.value === 'sl' ? 'en' : 'sl'
  locale.value = next
  await persistPreferredLanguage(next)
}

// Centralize sign-out 
function signOut() {
  localStorage.removeItem('token')
  localStorage.removeItem('role')
  localStorage.removeItem('user')
  localStorage.removeItem('userId')
  profileMenuOpen.value = false
  menuOpen.value = false
  router.push('/login')
}
// profile button logic
function handleOutsideClick(e) {
  if (!profileMenuOpen.value) return

  const menu = profileMenuRef.value
  const button = profileButtonRef.value

  if (!menu || !button) return

  const clickedInsideMenu = menu.contains(e.target)
  const clickedButton = button.contains(e.target)

  if (!clickedInsideMenu && !clickedButton) {
    profileMenuOpen.value = false
  }
}

onMounted(() => {
  document.addEventListener('click', handleOutsideClick)
})
onBeforeUnmount(() => {
  document.removeEventListener('click', handleOutsideClick)
})
</script>

<template>
  <div class="layout-wrapper">
    <header class="app-header" v-if="!hideHomeButton">
      <button class="menu-trigger-btn" @click="menuOpen = true" :aria-label="$t('nav.openMenu')"
        :title="$t('nav.openMenu')">
        <i class="pi pi-bars" aria-hidden="true"></i>{{ $t('nav.menu') }}
      </button>

      <div class="controls">
        <div v-if="user" class="profile-menu" ref="profileMenuRef">
          <button ref="profileButtonRef" class="user-chip user-chip--button" type="button"
            @click="profileMenuOpen = !profileMenuOpen" :aria-expanded="profileMenuOpen ? 'true' : 'false'"
            :aria-label="user.username">
            <span class="user-chip__name">{{ user.username }}</span>
          </button>

          <div v-if="profileMenuOpen" class="profile-popover">
            <div class="profile-popover__card">
              <p class="profile-popover__label">{{ $t('nav.profile') }}</p>
              <p class="profile-popover__name">{{ user.username }}</p>
              <button class="profile-popover__action" type="button" @click="signOut">
                <i class="pi pi-sign-out" aria-hidden="true"></i>{{ $t('nav.logout') }}
              </button>
            </div>
          </div>
        </div>
        <button class="lang-toggle" @click="toggleLanguage" :title="$t('nav.language')">
          <img :src="currentFlag" class="flag-icon" />
        </button>

        <button class="theme-toggle" @click="toggleTheme" :title="$t('nav.theme')">
          <i :class="themeIconClass" aria-hidden="true"></i>
        </button>
      </div>

      <div v-if="menuOpen" class="menu-backdrop" @click="menuOpen = false"></div>
      <transition name="SideMenuSlide">
        <aside v-if="menuOpen" class="menu-drawer">
          <div class="menu-head">
            <div class="menu-title">
              {{ $t('nav.menu') }}
            </div>
            <button class="menu-close" @click="menuOpen = false" :aria-label="$t('nav.closeMenu')">
              <i class="pi pi-times" aria-hidden="true"></i>
            </button>
          </div>
          <nav class="menu-nav">
            <div class="menu-section">
              <p v-if="isAdmin" class="menu-section-label">{{ $t('nav.userActions') }}</p>
              <router-link v-for="item in userMenuLinks" :key="`user-${item.to}`" class="menu-link" :to="item.to"
                @click="menuOpen = false">
                <i :class="item.icon" aria-hidden="true"></i>{{ item.labelKey ? $t(item.labelKey) : item.label }}
              </router-link>
            </div>

            <div v-if="isAdmin" class="menu-section menu-section--admin">
              <p class="menu-section-label">{{ $t('nav.adminActions') }}</p>
              <router-link v-for="item in adminMenuLinks" :key="`admin-${item.to}`" class="menu-link" :to="item.to"
                @click="menuOpen = false">
                <i :class="item.icon" aria-hidden="true"></i>{{ item.labelKey ? $t(item.labelKey) : item.label }}
              </router-link>
            </div>
          </nav>
        </aside>
      </transition>
    </header>

    <main class="app-main" :class="{ 'app-main--no-pad': noPadRoutes }">
      <router-view />
    </main>
  </div>
</template>



<style>
.layout-wrapper {
  min-height: 100vh;
  min-height: 100dvh;
  display: flex;
  flex-direction: column;
  background: var(--bg) !important;
  color: var(--text);
}

.app-header {
  position: sticky;
  top: 0;
  z-index: 50;

  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;

  padding: 12px;
  background: var(--bg);
  border-bottom: 1px solid var(--card-border);
}

.app-main {
  flex: 1;
  overflow: auto;
  overflow-x: hidden;
  -webkit-overflow-scrolling: touch;
  padding: 0 16px 0;
  background: var(--bg) !important;
}

.app-main--no-pad {
  padding: 0;
}

.menu-trigger-btn,
.lang-toggle,
.theme-toggle,
.menu-close {
  height: 36px;
  min-width: 36px;
  padding: 0 10px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
  cursor: pointer;

  display: inline-flex;
  align-items: center;
  justify-content: center;

  font-size: 14px;
  font-weight: 700;
  box-shadow: none;
  gap: 6px;
}

.menu-trigger-btn .pi,
.theme-toggle .pi,
.menu-close .pi,
.menu-title-user .pi,
.menu-link .pi,
.user-chip .pi {
  font-size: 14px;
}


.menu-trigger-btn:hover,
.lang-toggle:hover,
.theme-toggle:hover,
.menu-close:hover {
  background: var(--nav-item-hover);
}

.flag-icon {
  width: 26px;
  height: 26px;
  border-radius: 4px;
}

.controls {
  margin-left: auto;
  display: inline-flex;
  align-items: center;
  gap: 12px;
  position: relative;
}

.user-chip {
  min-height: 36px;
  padding: 0 12px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
  font-size: 14px;
  font-weight: 700;
  letter-spacing: 0.2px;
}

.profile-menu {
  position: relative;
}

.user-chip--button {
  min-width: 36px;
  min-height: 36px;
  cursor: pointer;
  justify-content: center;
  padding: 0 12px;
}

.user-chip--button:hover {
  background: var(--nav-item-hover);
}

.user-chip__name {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  text-align: center;
}

/* Added by Codex: Keep the first profile menu intentionally small and plain so later iterations can expand it safely. */
.profile-popover {
  position: absolute;
  top: calc(100% + 10px);
  right: 0;
  z-index: 80;
}

.profile-popover__card {
  min-width: 220px;
  padding: 12px;
  border-radius: 14px;
  border: 1px solid var(--card-border);
  background: var(--modal-bg);
  box-shadow: 0 14px 28px rgba(0, 0, 0, 0.28);
}

.profile-popover__label {
  margin: 0;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.4px;
  text-transform: uppercase;
  color: var(--muted);
}

.profile-popover__name {
  margin: 6px 0 12px;
  font-size: 14px;
  font-weight: 700;
  color: var(--text);
  word-break: break-word;
}

.profile-popover__action {
  width: 100%;
  height: 38px;
  padding: 0 12px;
  border-radius: 10px;
  border: 1px solid var(--card-border);
  background: var(--nav-item-bg);
  color: var(--text);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 700;
}

.profile-popover__action:hover {
  background: var(--nav-item-hover);
}

@media (max-width: 640px) {
  .app-header {
    padding: 10px calc(12px + env(safe-area-inset-right)) 10px calc(12px + env(safe-area-inset-left));
    gap: 8px;
  }

  .menu-trigger-btn {
    height: 34px;
    font-size: 13px;
    padding: 0 8px;
  }

  .lang-toggle,
  .theme-toggle {
    height: 34px;
    min-width: 34px;
  }

  .flag-icon {
    width: 22px;
    height: 22px;
  }

  .controls {
    gap: 8px;
  }

  .user-chip--button {
    min-width: 34px;
    min-height: 34px;
    padding: 0 10px;
  }

  .user-chip__name {
    max-width: 96px;
  }

  .app-main {
    overflow-y: auto;
    overflow-x: hidden;
  }
}

.menu-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  z-index: 60;
}

/* Added by Codex: The drawer keeps the existing visual language while switching to a sliding side menu. */
.menu-drawer {
  position: fixed;
  left: 0;
  top: 0;
  height: 100dvh;
  width: min(320px, 86vw);
  padding: 14px;
  padding-left: calc(14px + env(safe-area-inset-left));
  padding-top: calc(14px + env(safe-area-inset-top));
  background: var(--bg);
  border-right: 1px solid rgba(255, 255, 255, 0.12);
  border-radius: 0;
  display: grid;
  grid-template-rows: auto 1fr auto;
  gap: 12px;
  box-shadow: 8px 0 24px rgba(0, 0, 0, 0.35);
  z-index: 70;
  opacity: 1;
}

/* Added by Codex: Animate the shared drawer from the left edge so desktop and mobile behavior matches. */
.SideMenuSlide-enter-from,
.SideMenuSlide-leave-to {
  transform: translateX(-100%);
  opacity: 0;
}

.SideMenuSlide-enter-active,
.SideMenuSlide-leave-active {
  transition: transform 0.25s ease, opacity 0.25s ease;
}

.menu-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

.menu-title {
  font-weight: 800;
  font-size: 16px;
}

.menu-close {
  height: 32px;
  min-width: 32px;
  border-radius: 8px;
  border: 1px solid var(--card-border);
  background: var(--card-bg);
  color: var(--text);
  cursor: pointer;
  font-size: 18px;
  line-height: 1;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.menu-nav {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-height: 0;
  overflow-y: auto;
  padding-right: 4px;
}

.menu-section {
  display: flex;
  flex-direction: column;
  gap: 5px;
}

.menu-section--admin {
  margin-top: 10px;
  padding-top: 12px;
  border-top: 1px solid var(--card-border);
}

.menu-section-label {
  margin: 0 0 4px;
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.4px;
  text-transform: uppercase;
  color: var(--muted);
}

.menu-link {
  height: 45px;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 10px;
  border-radius: 10px;
  color: var(--text);
  text-decoration: none;
  border: 1px solid rgba(255, 255, 255, 0);
  background: var(--nav-item-bg);
}

.menu-link:hover {
  border-color: rgba(255, 255, 255, 0.12);
  background: var(--nav-item-active-hover);
}

.menu-link.router-link-exact-active {
  color: var(--text);
  border-color: var(--nav-item-active-border);
  background: var(--nav-item-active);
}

@media (max-width: 360px) {
  .app-header {
    padding: 8px calc(10px + env(safe-area-inset-right)) 8px calc(10px + env(safe-area-inset-left));
  }
}
</style>
