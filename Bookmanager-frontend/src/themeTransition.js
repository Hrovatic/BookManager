function getTransitionOrigin(event) {
  if (event && typeof event.clientX === 'number' && typeof event.clientY === 'number') {
    return { x: event.clientX, y: event.clientY }
  }

  return {
    x: window.innerWidth / 2,
    y: window.innerHeight / 2
  }
}

function prefersReducedMotion() {
  return window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches ?? false
}

export function toggleThemeWithTransition(nextIsLight, applyTheme, event) {
  if (typeof document === 'undefined' || typeof window === 'undefined') {
    applyTheme(nextIsLight)
    return
  }

  if (!document.startViewTransition || prefersReducedMotion()) {
    applyTheme(nextIsLight)
    return
  }

  const { x, y } = getTransitionOrigin(event)
  const endRadius = Math.hypot(
    Math.max(x, window.innerWidth - x),
    Math.max(y, window.innerHeight - y)
  )

  const transition = document.startViewTransition(() => {
    applyTheme(nextIsLight)
  })

  transition.ready
    .then(() => {
      document.documentElement.animate(
        {
          clipPath: [
            `circle(0px at ${x}px ${y}px)`,
            `circle(${endRadius}px at ${x}px ${y}px)`
          ]
        },
        {
          duration: 450,
          easing: 'ease-in-out',
          pseudoElement: '::view-transition-new(root)'
        }
      )
    })
    .catch(() => {
      // No-op fallback if transition preparation fails.
    })
}

