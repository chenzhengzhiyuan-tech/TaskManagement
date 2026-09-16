import { useAppStore } from './store'
import { useRequirementFilter } from './useRequirementFilter'
import { useEffect } from 'react'

export function useIterationFilter(userId: string) {
  useAppStore()
  const [selection, setSelection] = useRequirementFilter(userId, 'iteration', '')
  // @current was the old implicit default; clear it so the first view after
  // this change shows all requirements. Explicit user selections remain saved.
  const normalized = selection === '@current' ? '' : selection
  useEffect(() => { if (selection === '@current') setSelection('') }, [selection, setSelection])
  return [normalized, setSelection] as const
}
