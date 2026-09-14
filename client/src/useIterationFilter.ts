import { useAppStore } from './store'
import { useRequirementFilter } from './useRequirementFilter'

// Persist intent, not the current week's ID: only the default follows a rollover.
const CURRENT = '@current'
export function useIterationFilter(userId: string) {
  const { iterations } = useAppStore()
  const [selection, setSelection] = useRequirementFilter(userId, 'iteration', CURRENT)
  const currentId = iterations.find(item => item.state === 'active')?.id ?? ''
  return [selection === CURRENT ? currentId : selection, setSelection] as const
}
