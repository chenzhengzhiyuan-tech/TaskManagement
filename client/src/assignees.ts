import type { User } from './types'
export const assigneeIds = (item: { assigneeIds?: string[]; assigneeId: string | null }) => item.assigneeIds ?? (item.assigneeId ? [item.assigneeId] : [])
export const assigneeNames = (item: { assigneeIds?: string[]; assigneeId: string | null }, users: User[]) => assigneeIds(item).map(id => users.find(user => user.id === id)?.name ?? '已删除成员').join('、')
export const assigneeMatches = (filter: string, item: { assigneeIds?: string[]; assigneeId: string | null }) => !filter || filter.split(',').some(id => assigneeIds(item).includes(id))
