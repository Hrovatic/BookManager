const explicitBase = (import.meta.env.VITE_API_BASE_URL || '').trim()

const fallbackBase =
  window.location.protocol === 'https:'
    ? 'https://localhost:7026'
    : 'http://localhost:5127'

export const API_BASE_URL = explicitBase || fallbackBase

export function apiUrl(path = '') {
  const normalized = path.startsWith('/') ? path : `/${path}`
  return `${API_BASE_URL}${normalized}`
}

export function resolveAssetUrl(value = '') {
  const raw = String(value ?? '').trim()
  if (!raw) return ''

  if (/^(https?:|data:|blob:|\/\/)/i.test(raw)) {
    return raw
  }

  const normalized = raw.startsWith('/') ? raw : `/${raw}`
  return `${API_BASE_URL}${normalized}`
}
