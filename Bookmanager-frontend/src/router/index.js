import { createRouter, createWebHistory } from 'vue-router'

import AppLayout from '@/components/layouts/AppLayout.vue'

import Register from '@/components/Register.vue'
import Login from '@/components/Login.vue'
import Signup from '@/components/Signup.vue'
//import ChangePassword from '@/components/ChangePassword.vue'
//import Home from '@/components/Home.vue'
import ScanIsbn from '@/components/SearchByISBN.vue'
import AddBook from '@/components/AddBook.vue'
import ShowBook from '@/components/AllBooks.vue'
import Wishlist from '@/components/Wishlist.vue'
import MyBorrowings from '@/components/MyBorrowings.vue'
import Details from '@/components/BookDetails.vue'
import EditBook from '@/components/EditBook.vue'
import AdminUsers from '@/components/AdminUsers.vue'
import Inventory from '../components/Inventory.vue'
import PriceReview from '../components/PriceReview.vue'
import Statistics from '../components/Statistics.vue'
import AdminCenters from '../components/AdminCenters.vue'
import AdminAuditLogs from '@/components/AdminAuditLogs.vue'

// Added by Codex: Redirect the app entry point to the books overview so existing deep links keep working while /showbooks acts as home.
const routes = [
  // AUTH pages (brez layouta)
  {
    path: '/login',
    name: 'login',
    component: Login
  },
  {
    path: '/signup',
    name: 'signup',
    component: Signup
  },

  // APP layout (vse ostale strani)
  {
    path: '/',
    component: AppLayout,
    children: [
      
      { path: '', redirect: '/showbooks' },
  //    { path: 'home', name: 'home', component: Home },
      { path: 'isbn-search', name: 'isbn-search', component: ScanIsbn },
      { path: 'adduser', name: 'adduser', component: Register},
      { path: 'admin-users', name: 'admin-users', component: AdminUsers},
      { path: 'addbook', name: 'addbook', component: AddBook },
      { path: 'inventory', name: 'inventory', component: Inventory},
      { path: 'showbooks', name: 'showbooks', component: ShowBook },
      { path: 'wishlist', name: 'wishlist', component: Wishlist },
      { path: 'borrowings', name: 'borrowings', component: MyBorrowings },
   //   { path: 'change-password', name: 'change-password', component: ChangePassword },
      { path: 'book/:id', name: 'details', component: Details },
      { path: 'editbook/:id', name: "edit", component: EditBook},
      { path: 'price-review', name: 'price-review', component: PriceReview},
      { path: 'statistics', name: 'statistics', component: Statistics},
      { path: 'admin-centers', name: 'admin-centers', component: AdminCenters},
      { path: 'audit-logs', name: 'audit-logs', component: AdminAuditLogs },
    ]
  }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

const PUBLIC_ROUTE_NAMES = new Set(['login', 'signup'])
const ADMIN_ONLY_ROUTE_NAMES = new Set([
  'adduser',
  'admin-users',
  'addbook',
  'inventory',
  'price-review',
  'isbn-search',
  'admin-centers',
  'AdminAuditLogs'
])
const AUTH_HEARTBEAT_MS = 3000
let authHeartbeatId = null

function clearAuthStorage() {
  localStorage.removeItem('token')
  localStorage.removeItem('role')
  localStorage.removeItem('user')
  localStorage.removeItem('userId')
}

function parseJwtPayload(rawToken) {
  if (!rawToken || typeof rawToken !== 'string') return null

  const token = rawToken.startsWith('Bearer ') ? rawToken.slice(7) : rawToken
  const parts = token.split('.')
  if (parts.length !== 3) return null

  try {
    const payload = parts[1]
      .replace(/-/g, '+')
      .replace(/_/g, '/')
      .padEnd(Math.ceil(parts[1].length / 4) * 4, '=')
    return JSON.parse(atob(payload))
  } catch {
    return null
  }
}

function hasValidToken(rawToken) {
  const payload = parseJwtPayload(rawToken)
  if (!payload) return false
  if (typeof payload.exp !== 'number') return true
  return payload.exp * 1000 > Date.now()
}

function enforceAuthForCurrentRoute() {
  const currentRoute = router.currentRoute.value
  const currentName = currentRoute?.name
  if (PUBLIC_ROUTE_NAMES.has(currentName)) return

  const token = localStorage.getItem('token')
  if (hasValidToken(token)) return

  clearAuthStorage()
  router.replace({ name: 'login' })
}

function shouldRunAuthHeartbeat() {
  const currentName = router.currentRoute.value?.name
  if (!currentName) return false
  if (PUBLIC_ROUTE_NAMES.has(currentName)) return false
  return typeof document === 'undefined' || document.visibilityState === 'visible'
}

function syncAuthHeartbeat() {
  if (typeof window === 'undefined') return

  if (shouldRunAuthHeartbeat()) {
    if (authHeartbeatId === null) {
      authHeartbeatId = window.setInterval(() => {
        enforceAuthForCurrentRoute()
      }, AUTH_HEARTBEAT_MS)
    }
    return
  }

  if (authHeartbeatId !== null) {
    window.clearInterval(authHeartbeatId)
    authHeartbeatId = null
  }
}

router.beforeEach((to, from, next) => {
  const token = localStorage.getItem('token')
  const role = localStorage.getItem('role')
  const isPublicRoute = PUBLIC_ROUTE_NAMES.has(to.name)
  const isAuthenticated = hasValidToken(token)

  if (!isAuthenticated) {
    clearAuthStorage()
  }

  if (!isAuthenticated && !isPublicRoute) return next({ name: 'login' })
  if (isAuthenticated && to.name === 'login') return next({ name: 'showbooks' })
  if (isAuthenticated && ADMIN_ONLY_ROUTE_NAMES.has(to.name) && role !== 'admin') return next({ name: 'showbooks' })
  next()
})

router.afterEach(() => {
  syncAuthHeartbeat()
})

if (typeof window !== 'undefined') {
  window.addEventListener('storage', (event) => {
    if (event.key === 'token' || event.key === null) {
      enforceAuthForCurrentRoute()
      syncAuthHeartbeat()
    }
  })

  window.addEventListener('focus', () => {
    enforceAuthForCurrentRoute()
    syncAuthHeartbeat()
  })

  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') {
      enforceAuthForCurrentRoute()
    }
    syncAuthHeartbeat()
  })

  syncAuthHeartbeat()
}

export default router
