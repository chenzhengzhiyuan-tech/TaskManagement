import { useCallback, useState } from 'react'

// Both views use the same per-account filters, including after a refresh.
export function useRequirementFilter(userId: string, field: string, fallback = '') {
  const key = `g43-filters:${userId}:${field}`
  const [value, setValue] = useState(() => {
    try { return sessionStorage.getItem(key) ?? fallback } catch { return fallback }
  })
  const update = useCallback((next: string) => {
    try { sessionStorage.setItem(key, next) } catch { /* Storage can be disabled. */ }
    setValue(next)
  }, [key])
  return [value, update] as const
}
