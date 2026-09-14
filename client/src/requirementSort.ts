import type { Requirement } from './types'
import { priorityRank } from './utils'

const statusOrder = ['todo', 'in_progress', 'review', 'paused', 'backlog', 'completed', 'closed']
export function compareRequirementNumber(a: string, b: string) {
  return a.localeCompare(b, 'en', { numeric: true })
}
export function comparePriorityAndNumber(a: Requirement, b: Requirement) {
  return priorityRank[b.priority] - priorityRank[a.priority] || compareRequirementNumber(a.id, b.id)
}
export function compareTaskStatus(a: Requirement, b: Requirement) {
  const rank = (id: string) => { const index = statusOrder.indexOf(id); return index < 0 ? statusOrder.length : index }
  return rank(a.statusId) - rank(b.statusId) || comparePriorityAndNumber(a, b)
}
