# Rudolfovo Library Frontend

Vue 3 + Vite frontend for the BookManager app.

## Stack

- Vue 3
- Vue Router
- vue-i18n
- Axios
- ApexCharts (`vue3-apexcharts`)
- PrimeIcons
- Vite PWA (`vite-plugin-pwa`)

## Requirements

- Node.js `^20.19.0` or `>=22.12.0`

## Setup

```sh
npm install
```

## Environment

Create/update `Bookmanager-frontend/.env`:

```env
VITE_API_BASE_URL=https://localhost:7026
```

If this is not set, frontend falls back to:

- `https://localhost:7026` on HTTPS pages
- `http://localhost:5127` on HTTP pages

## Run

```sh
npm run dev
```

## Build

```sh
npm run build
```

## Preview Production Build

```sh
npm run preview
```

## PWA

PWA is enabled in `Bookmanager-frontend/vite.config.js` with:

- app name: `Rudolfovo Library`
- dark theme manifest colors
- Workbox runtime cache for `/api/*` (`NetworkFirst`)

## Accessibility Note

The app respects `prefers-reduced-motion`:

- transitions/animations are minimized globally
- smooth scrolling is disabled
- view-transition animations are disabled for reduced-motion users
