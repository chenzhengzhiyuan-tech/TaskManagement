export type Theme = 'dark' | 'light'

const KEY = 'g43-theme'
const SWITCHING_CLASS = 'theme-switching'
let cleanupTimer: number | undefined

export function getTheme(): Theme {
  return localStorage.getItem(KEY) === 'light' ? 'light' : 'dark'
}

export function applyTheme(theme: Theme) {
  const root = document.documentElement
  root.classList.add(SWITCHING_CLASS)
  root.dataset.theme = theme
  localStorage.setItem(KEY, theme)

  // Switch foreground and background atomically. Independent color transitions
  // otherwise create unreadable intermediate frames while toggling themes.
  void root.offsetHeight
  window.clearTimeout(cleanupTimer)
  cleanupTimer = window.setTimeout(() => root.classList.remove(SWITCHING_CLASS), 0)
}