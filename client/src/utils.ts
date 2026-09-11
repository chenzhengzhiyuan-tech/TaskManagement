import type { Priority, Requirement, StatusDefinition, User } from './types'

export const modules = ['通用', '文档', '测试', '性能', '交互', '数据', '工具', 'UI', '服务器', '体验', '其他']

export const priorityLabel: Record<Priority, string> = {
  urgent: '极高',
  high: '高',
  medium: '中',
  low: '低',
}

export const priorityRank: Record<Priority, number> = {
  urgent: 4,
  high: 3,
  medium: 2,
  low: 1,
}

export const CHINA_TIME_ZONE = 'Asia/Shanghai'

export function chinaDateKey(now = new Date()) {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: CHINA_TIME_ZONE, year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(now)
  const part = (type: string) => parts.find((item) => item.type === type)?.value
  return `${part('year')}-${part('month')}-${part('day')}`
}

export function dashboardDate(now = new Date()) {
  const weekday = new Intl.DateTimeFormat('en-US', { timeZone: CHINA_TIME_ZONE, weekday: 'long' }).format(now).toUpperCase()
  return `${weekday} · ${chinaDateKey(now).replaceAll('-', '.')}`
}

export function greeting(now = new Date()) {
  const hour = Number(new Intl.DateTimeFormat('en-GB', { timeZone: CHINA_TIME_ZONE, hour: '2-digit', hourCycle: 'h23' }).format(now))
  return hour < 12 ? '上午好' : hour < 18 ? '下午好' : '晚上好'
}

export function formatDate(value: string | null, withTime = false) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return new Intl.DateTimeFormat('zh-CN', {
    timeZone: CHINA_TIME_ZONE,
    month: '2-digit',
    day: '2-digit',
    ...(withTime ? { hour: '2-digit', minute: '2-digit', hour12: false } : {}),
  }).format(date)
}

export function isOverdue(requirement: Requirement, statuses: StatusDefinition[], now = new Date()) {
  if (!requirement.dueDate) return false
  const status = statuses.find((item) => item.id === requirement.statusId)
  return !status?.terminal && requirement.dueDate < chinaDateKey(now)
}

export function getUser(users: User[], id: string | null) {
  return users.find((user) => user.id === id)
}

export function requirementMatches(requirement: Requirement, query: string) {
  const normalized = query.trim().toLowerCase()
  if (!normalized) return true
  return `${requirement.id} ${requirement.title} ${requirement.module}`.toLowerCase().includes(normalized)
}

export function getCompletion(requirements: Requirement[], statuses: StatusDefinition[]) {
  if (!requirements.length) return 0
  const completed = requirements.filter((requirement) => statuses.find((status) => status.id === requirement.statusId)?.terminal)
  return Math.round((completed.length / requirements.length) * 100)
}

export function parentFinalStatusBlockReason(requirement: Requirement, targetStatusId: string, requirements: Requirement[]) {
  if (!['completed', 'closed'].includes(targetStatusId) || requirement.statusId === targetStatusId) return null
  const incompleteChildren = requirements.filter((item) => item.parentId === requirement.id && !['completed', 'closed'].includes(item.statusId))
  return incompleteChildren.length
    ? `父任务仍有 ${incompleteChildren.length} 个未完成的子任务，请先将所有子任务设置为“已完成”或“已关闭”`
    : null
}
