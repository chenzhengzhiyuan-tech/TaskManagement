export type Role = 'admin' | 'developer'
export type Priority = 'urgent' | 'high' | 'medium' | 'low'
export type PageKey = 'dashboard' | 'requirements' | 'iterations' | 'board' | 'reports' | 'settings'

export interface User {
  id: string
  name: string
  account: string
  role: Role
  initials: string
  color: string
  wecomBound: boolean
  active?: boolean
}

export interface StatusDefinition {
  id: string
  name: string
  color: string
  terminal: boolean
  protected?: boolean
}

export interface RequirementType { id: string; name: string; color: string; sortOrder: number; enabled: boolean }
export interface RequirementDefaults { module: string | null; priority: Priority; statusId: string | null; assigneeId: string | null; reviewerId: string | null; requirementTypeId: string | null; iterationMode: 'current' | 'none' | 'specific'; iterationId: string | null; dueDateOffsetDays: number | null; descriptionTemplate: string }
export interface SystemBranding { projectName: string; loginBackgroundUrl: string; updatedAt: string }
export interface WorkCalendarDay { date: string; isWorkday: boolean; note: string | null }

export interface Iteration {
  id: string
  name: string
  startDate: string
  endDate: string
  state: 'upcoming' | 'active' | 'completed'
  goal: string
}

export interface Comment {
  id: string
  authorId: string
  content: string
  createdAt: string
}

export interface HistoryEntry {
  id: string
  actorId: string
  action: string
  detail: string
  createdAt: string
}

export interface Attachment {
  id: string
  name: string
  size: number
  type: string
  uploadedBy: string
  createdAt: string
  url?: string
  downloadUrl?: string
}

export type CustomFieldType = 'text' | 'number' | 'date' | 'single' | 'multi' | 'person'
export type CustomFieldValue = string | number | string[] | null

export interface CustomField {
  id: string
  name: string
  type: CustomFieldType
  required: boolean
  enabled: boolean
  options?: string[]
}

export interface Requirement {
  assigneeIds?: string[]
  id: string
  title: string
  module: string
  priority: Priority
  statusId: string
  assigneeId: string | null
  creatorId: string
  iterationId: string | null
  parentId: string | null
  reviewerId?: string | null
  requirementTypeId?: string | null
  dueDate: string | null
  description: string
  createdAt: string
  updatedAt: string
  comments: Comment[]
  history: HistoryEntry[]
  attachments: Attachment[]
  customValues: Record<string, CustomFieldValue>
  version?: number
}

export interface CreateRequirementInput {
  assigneeIds?: string[]
  title: string
  module: string
  priority: Priority
  statusId: string
  assigneeId: string | null
  iterationId: string | null
  parentId: string | null
  reviewerId?: string | null
  requirementTypeId?: string | null
  dueDate: string | null
  description: string
  customValues?: Record<string, CustomFieldValue>
}

export interface AppData {
  users: User[]
  statuses: StatusDefinition[]
  iterations: Iteration[]
  requirements: Requirement[]
  customFields: CustomField[]
  modules: string[]
  requirementTypes: RequirementType[]
  requirementDefaults: RequirementDefaults
  branding: SystemBranding
  currentUserId: string
}


