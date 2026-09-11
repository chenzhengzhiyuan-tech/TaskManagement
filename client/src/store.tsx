import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react'
import type { PropsWithChildren } from 'react'
import { ApiError, apiBlob, apiRequest, getApiToken, setApiToken } from './api'
import { initialData } from './data'
import { clearAttachmentBlobs, deleteAttachmentBlobs, getAttachmentBlob, saveAttachmentBlob } from './attachmentStorage'
import type { AppData, Attachment, CreateRequirementInput, CustomField, Requirement, RequirementDefaults, RequirementType, StatusDefinition, SystemBranding, User, WorkCalendarDay } from './types'
import { parentFinalStatusBlockReason } from './utils'

const STORAGE_KEY = 'g43-client-demo-v1'
const MOCK_SESSION_KEY = 'g43-client-session-v1'
const DEMO_PASSWORD = 'demo123'
const API_MODE = import.meta.env.VITE_DATA_MODE === 'api'

interface OperationResult { ok: boolean; reason?: string }
interface ApiModule { id: string; name: string; sortOrder: number }
export interface WeComUserProfile { userId: string; name: string; departments: number[]; status: number; active: boolean }
export interface RequirementImportRow { rowNumber: number; title: string; requirementType: string; status: string; assignee: string; priority: string; module: string; iteration: string; dueDate: string; valid: boolean; errors: string[] }
export interface RequirementImportResult { preview: boolean; totalRows: number; validRows: number; invalidRows: number; importedRows: number; rows: RequirementImportRow[] }
export interface RequirementTreeGroup { root: Requirement; children: Requirement[]; rootMatches: boolean }
export interface RequirementTreePage { page: number; pageSize: number; rootCount: number; requirementCount: number; groups: RequirementTreeGroup[] }
export interface RequirementTreeQuery { mine?: boolean; query?: string; statusId?: string; assigneeId?: string; iterationId?: string; priority?: string; requirementTypeId?: string; page?: number; pageSize?: number; sort?: string }
interface ApiBootstrap {
  currentUser: AppData['users'][number]
  users: AppData['users']
  statuses: Array<StatusDefinition & { sortOrder: number }>
  iterations: AppData['iterations']
  requirements: AppData['requirements']
  customFields: Array<CustomField & { sortOrder: number }>
  modules: ApiModule[]
  requirementTypes: RequirementType[]
  requirementDefaults: RequirementDefaults
  branding: SystemBranding
}
interface UploadSession { uploadId: string; chunkSize: number; totalChunks: number; expiresAt: string }
interface CompleteUpload { attachment: Attachment }

interface AppStoreValue extends AppData {
  currentUser: AppData['users'][number]
  mode: 'mock' | 'api'
  authenticated: boolean
  initializing: boolean
  lastOperationError: string
  login: (account: string, password: string) => Promise<OperationResult>
  logout: () => Promise<void>
  refresh: () => Promise<void>
  createRequirement: (input: CreateRequirementInput) => Promise<string>
  updateRequirement: (id: string, patch: Partial<Requirement>, summary?: string) => Promise<boolean>
  moveRequirementStatus: (id: string, statusId: string) => Promise<OperationResult>
  deleteRequirement: (id: string) => Promise<boolean>
  addComment: (id: string, content: string) => Promise<void>
  uploadAttachment: (id: string, file: File, onProgress?: (progress: number) => void) => Promise<Attachment>
  deleteAttachment: (requirementId: string, attachmentId: string) => Promise<OperationResult>
  loadAttachmentBlob: (attachment: Attachment) => Promise<Blob | null>
  setCurrentUser: (id: string) => void
  updateUserRole: (id: string, role: AppData['users'][number]['role']) => Promise<void>
  createUser: (input: { account: string; name: string; role: User['role']; password: string; color?: string; weComEmail?: string | null }) => Promise<OperationResult>
  validateWeComEmail: (email: string) => Promise<{ ok: boolean; reason?: string; profile?: WeComUserProfile }>
  bindWeComUser: (id: string, email: string) => Promise<OperationResult>
  unbindWeComUser: (id: string) => Promise<OperationResult>
  updateUser: (id: string, input: { password?: string; role?: User['role'] }) => Promise<OperationResult>
  deleteUser: (id: string) => Promise<OperationResult>
  addModule: (name: string) => Promise<OperationResult>
  renameModule: (oldName: string, name: string) => Promise<OperationResult>
  setStatusProtection: (id: string, value: boolean) => Promise<OperationResult>
  reorderStatuses: (ids: string[]) => Promise<OperationResult>
  removeModule: (name: string) => Promise<OperationResult>
  addStatus: (name: string, color: string) => Promise<OperationResult>
  renameStatus: (id: string, name: string) => Promise<OperationResult>
  removeStatus: (id: string) => Promise<OperationResult>
  addCustomField: (field: Omit<CustomField, 'id' | 'enabled'>) => Promise<OperationResult>
  toggleCustomField: (id: string) => Promise<void>
  addRequirementType: (name: string) => Promise<OperationResult>
  updateRequirementType: (id: string, input: Partial<RequirementType>) => Promise<OperationResult>
  updateRequirementDefaults: (input: Partial<RequirementDefaults>) => Promise<OperationResult>
  updateBranding: (projectName: string) => Promise<OperationResult>
  uploadBrandingBackground: (file: File) => Promise<OperationResult>
  getWorkCalendar: (year: number) => Promise<WorkCalendarDay[]>
  updateWorkCalendar: (date: string, isWorkday: boolean, note?: string) => Promise<OperationResult>
  queryRequirementTree: (query: RequirementTreeQuery) => Promise<RequirementTreePage>
  importRequirements: (file: File, commit: boolean) => Promise<RequirementImportResult>
  resetDemo: () => Promise<void>
}

const AppStoreContext = createContext<AppStoreValue | null>(null)
const cloneInitialData = (): AppData => JSON.parse(JSON.stringify(initialData)) as AppData

function loadMockData(): AppData {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (!stored) return cloneInitialData()
    const parsed = JSON.parse(stored) as Partial<AppData>
    return { ...cloneInitialData(), ...parsed, modules: parsed.modules?.length ? parsed.modules : cloneInitialData().modules } as AppData
  } catch { return cloneInitialData() }
}

function mapBootstrap(payload: ApiBootstrap): { data: AppData; moduleIds: Record<string, string> } {
  return {
    data: {
      currentUserId: payload.currentUser.id,
      users: payload.users,
      statuses: payload.statuses,
      iterations: payload.iterations,
      requirements: payload.requirements,
      customFields: payload.customFields,
      modules: payload.modules.map((module) => module.name),
      requirementTypes: payload.requirementTypes,
      requirementDefaults: payload.requirementDefaults,
      branding: payload.branding,
    },
    moduleIds: Object.fromEntries(payload.modules.map((module) => [module.name, module.id])),
  }
}

function nowIso() { return new Date().toISOString() }
const TYPE_COLOR_PALETTE = ['#0A84FF','#BF5AF2','#FF9F0A','#30D158','#FF453A','#64D2FF','#5E5CE6','#FF375F','#AC8E68','#FFD60A']
function hslToHex(hue: number, saturation = .72, lightness = .52) {
  const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation
  const x = chroma * (1 - Math.abs((hue / 60) % 2 - 1)); const m = lightness - chroma / 2
  const [red, green, blue] = hue < 60 ? [chroma,x,0] : hue < 120 ? [x,chroma,0] : hue < 180 ? [0,chroma,x] : hue < 240 ? [0,x,chroma] : hue < 300 ? [x,0,chroma] : [chroma,0,x]
  return `#${[red,green,blue].map((value) => Math.round((value + m) * 255).toString(16).padStart(2,'0')).join('').toUpperCase()}`
}
function nextRequirementTypeColor(types: RequirementType[]) {
  const used = new Set(types.map((type) => type.color.toUpperCase()))
  const paletteColor = TYPE_COLOR_PALETTE.find((color) => !used.has(color)); if (paletteColor) return paletteColor
  for (let index = 0; index < 720; index++) { const candidate = hslToHex((types.length * 137.508 + index * 53) % 360); if (!used.has(candidate)) return candidate }
  return '#0A84FF'
}function wait(ms: number) { return new Promise((resolve) => window.setTimeout(resolve, ms)) }
function hasOwn(object: object, key: PropertyKey) { return Object.prototype.hasOwnProperty.call(object, key) }

export function AppStoreProvider({ children }: PropsWithChildren) {
  const [data, setData] = useState<AppData>(loadMockData)
  const [moduleIds, setModuleIds] = useState<Record<string, string>>({})
  const [authenticated, setAuthenticated] = useState(() => API_MODE ? Boolean(getApiToken()) : sessionStorage.getItem(MOCK_SESSION_KEY) === 'authenticated')
  const [initializing, setInitializing] = useState(() => API_MODE && Boolean(getApiToken()))
  const [lastOperationError, setLastOperationError] = useState('')
  const iterationSignature = JSON.stringify(data.iterations)
  const iterationSignatureRef = useRef(iterationSignature)
  useEffect(() => { iterationSignatureRef.current = iterationSignature }, [iterationSignature])
  const requirementsRef = useRef(data.requirements)
  useEffect(() => { requirementsRef.current = data.requirements }, [data.requirements])

  const currentUser = useMemo(() => data.users.find((user) => user.id === data.currentUserId) ?? data.users[0], [data.currentUserId, data.users])

  const handleApiError = useCallback((error: unknown) => {
    if (error instanceof ApiError && error.status === 401) { setApiToken(null); setAuthenticated(false) }
    return error instanceof Error ? error.message : '请求失败'
  }, [])

  const refresh = useCallback(async () => {
    if (!API_MODE) return
    try {
      const mapped = mapBootstrap(await apiRequest<ApiBootstrap>('/bootstrap'))
      setData(mapped.data); setModuleIds(mapped.moduleIds); setAuthenticated(true)
    } catch (error) { handleApiError(error); throw error }
  }, [handleApiError])

  useEffect(() => {
    if (!API_MODE || !getApiToken()) { setInitializing(false); return }
    void refresh().finally(() => setInitializing(false))
  }, [refresh])

  useEffect(() => {
    if (!API_MODE || !authenticated || initializing) return
    let disposed = false
    let pending = false
    const controller = new AbortController()
    async function syncIterations() {
      if (disposed || pending || document.visibilityState === 'hidden') return
      pending = true
      try {
        const iterations = await apiRequest<AppData['iterations']>('/iterations', { signal: controller.signal })
        if (JSON.stringify(iterations) !== iterationSignatureRef.current) {
          const idsBeforeRequest = new Set(requirementsRef.current.map((item) => item.id))
          const { data: incoming } = mapBootstrap(await apiRequest<ApiBootstrap>('/bootstrap', { signal: controller.signal }))
          if (!disposed) setData((previous) => {
            const currentById = new Map(previous.requirements.map((item) => [item.id, item]))
            const incomingIds = new Set(incoming.requirements.map((item) => item.id))
            const requirements = incoming.requirements
              // Preserve deletions made while the snapshot was in flight.
              .filter((item) => !idsBeforeRequest.has(item.id) || currentById.has(item.id))
              .map((item) => {
                const current = currentById.get(item.id)
                return current && (current.version ?? 0) > (item.version ?? 0) ? current : item
              })
            // Also preserve newly created tasks absent from the older server snapshot.
            requirements.push(...previous.requirements.filter((item) => !idsBeforeRequest.has(item.id) && !incomingIds.has(item.id)))
            return { ...previous, iterations: incoming.iterations, requirements }
          })
        }
      } catch (error) { if (!disposed) handleApiError(error) }
      finally { pending = false }
    }
    const timer = window.setInterval(() => void syncIterations(), 30_000)
    window.addEventListener('focus', syncIterations)
    document.addEventListener('visibilitychange', syncIterations)
    return () => {
      disposed = true
      controller.abort()
      window.clearInterval(timer)
      window.removeEventListener('focus', syncIterations)
      document.removeEventListener('visibilitychange', syncIterations)
    }
  }, [authenticated, initializing, handleApiError])

  useEffect(() => { if (!API_MODE) localStorage.setItem(STORAGE_KEY, JSON.stringify(data)) }, [data])
  useEffect(() => {
    if (!API_MODE || authenticated) return
    void apiRequest<SystemBranding>('/public-settings').then((branding) => setData((previous) => ({ ...previous, branding }))).catch(() => undefined)
  }, [authenticated])


  const login = useCallback(async (account: string, password: string): Promise<OperationResult> => {
    if (!API_MODE) {
      const member = account.trim().toLowerCase()
      const user = data.users.find((item) => item.name.toLowerCase() === member || item.account.toLowerCase() === member)
      if (!user || password !== DEMO_PASSWORD) return { ok: false, reason: '成员或密码错误' }
      setData((previous) => ({ ...previous, currentUserId: user.id })); sessionStorage.setItem(MOCK_SESSION_KEY, 'authenticated'); setAuthenticated(true); return { ok: true }
    }
    try {
      const result = await apiRequest<{ token: string }>('/auth/login', { method: 'POST', body: JSON.stringify({ account, password }) })
      setApiToken(result.token)
      const mapped = mapBootstrap(await apiRequest<ApiBootstrap>('/bootstrap'))
      setData(mapped.data); setModuleIds(mapped.moduleIds); setAuthenticated(true); return { ok: true }
    } catch (error) { setApiToken(null); setAuthenticated(false); return { ok: false, reason: handleApiError(error) } }
  }, [data.users, handleApiError])

  const logout = useCallback(async () => {
    if (API_MODE && getApiToken()) { try { await apiRequest<void>('/auth/logout', { method: 'POST' }) } catch { /* local logout still proceeds */ } setApiToken(null) }
    else sessionStorage.removeItem(MOCK_SESSION_KEY)
    setAuthenticated(false); setData(cloneInitialData()); setModuleIds({})
  }, [])

  const createRequirement = useCallback(async (input: CreateRequirementInput) => {
    if (API_MODE) {
      const created = await apiRequest<Requirement>('/requirements', { method: 'POST', body: JSON.stringify(input) })
      setData((previous) => ({ ...previous, requirements: [created, ...previous.requirements] })); return created.id
    }
    const maxId = data.requirements.reduce((max, item) => Math.max(max, Number(item.id.replace('REQ-', '')) || 0), 0)
    const id = `REQ-${String(maxId + 1).padStart(4, '0')}`; const timestamp = nowIso()
    const requirement: Requirement = { ...input, reviewerId: input.reviewerId ?? data.requirementDefaults.reviewerId, requirementTypeId: input.requirementTypeId ?? data.requirementDefaults.requirementTypeId, id, creatorId: currentUser.id, createdAt: timestamp, updatedAt: timestamp, comments: [], attachments: [], customValues: input.customValues ?? {}, version: 1, history: [{ id: crypto.randomUUID(), actorId: currentUser.id, action: '创建需求', detail: `创建了 ${id}`, createdAt: timestamp }] }
    setData((previous) => ({ ...previous, requirements: [requirement, ...previous.requirements] })); return id
  }, [currentUser.id, data.requirements, data.requirementDefaults.reviewerId, data.requirementDefaults.requirementTypeId])

  const updateRequirement = useCallback(async (id: string, patch: Partial<Requirement>, summary = '更新了需求信息') => {
    if (hasOwn(patch, 'assigneeIds')) patch = { ...patch, assigneeId: patch.assigneeIds?.[0] ?? null }
    else if (hasOwn(patch, 'assigneeId')) patch = { ...patch, assigneeIds: patch.assigneeId ? [patch.assigneeId] : [] }
    setLastOperationError('')
    const current = data.requirements.find((requirement) => requirement.id === id)
    if (!current) return false
    const targetStatus = patch.statusId ? data.statuses.find((status) => status.id === patch.statusId) : null
    const parentStatusBlock = patch.statusId ? parentFinalStatusBlockReason(current, patch.statusId, data.requirements) : null
    if (parentStatusBlock) { setLastOperationError(parentStatusBlock); return false }
    if (currentUser.role === 'developer' && targetStatus?.protected) { setLastOperationError('普通成员不能设置系统保护状态'); return false }
    if (API_MODE) {
      const body: Record<string, unknown> = { summary, version: current.version }
      for (const key of ['title', 'module', 'priority', 'statusId', 'description', 'reviewerId', 'requirementTypeId'] as const) if (hasOwn(patch, key)) body[key] = patch[key]
      if (hasOwn(patch, 'assigneeId')) { body.assigneeId = patch.assigneeId; body.clearAssignee = patch.assigneeId === null }
      if (hasOwn(patch, 'assigneeIds')) body.assigneeIds = patch.assigneeIds
      if (hasOwn(patch, 'iterationId')) { body.iterationId = patch.iterationId; body.clearIteration = patch.iterationId === null }
      if (hasOwn(patch, 'parentId')) { body.parentId = patch.parentId; body.clearParent = patch.parentId === null }
      if (hasOwn(patch, 'reviewerId')) { body.reviewerId = patch.reviewerId; body.clearReviewer = patch.reviewerId === null }
      if (hasOwn(patch, 'requirementTypeId')) { body.requirementTypeId = patch.requirementTypeId; body.clearRequirementType = patch.requirementTypeId === null }
      if (hasOwn(patch, 'dueDate')) { body.dueDate = patch.dueDate; body.clearDueDate = patch.dueDate === null }
      try {
        const updated = await apiRequest<Requirement>(`/requirements/${id}`, { method: 'PATCH', body: JSON.stringify(body) })
        setData((previous) => ({ ...previous, requirements: previous.requirements.map((item) => item.id === id ? updated : item) })); return true
      } catch (error) { const reason=handleApiError(error); setLastOperationError(reason); if (error instanceof ApiError && error.status === 409) await refresh(); return false }
    }
    const timestamp = nowIso()
    setData((previous) => ({ ...previous, requirements: previous.requirements.map((requirement) => requirement.id === id ? { ...requirement, ...patch, version: (requirement.version ?? 1) + 1, updatedAt: timestamp, history: [{ id: crypto.randomUUID(), actorId: currentUser.id, action: '需求更新', detail: summary, createdAt: timestamp }, ...requirement.history] } : requirement) })); return true
  }, [currentUser.id, currentUser.role, data.requirements, data.statuses, handleApiError, refresh])

  const moveRequirementStatus = useCallback(async (id: string, statusId: string): Promise<OperationResult> => {
    const targetStatus = data.statuses.find((status) => status.id === statusId)
    const current = data.requirements.find((item) => item.id === id)
    if (current) {
      const parentStatusBlock = parentFinalStatusBlockReason(current, statusId, data.requirements)
      if (parentStatusBlock) { setLastOperationError(parentStatusBlock); return { ok: false, reason: parentStatusBlock } }
    }
    if (currentUser.role === 'developer' && targetStatus?.protected) return { ok: false, reason: '普通成员不能移动到系统保护状态' }
    if (!API_MODE) return { ok: await updateRequirement(id, { statusId }, `通过看板将状态变更为“${targetStatus?.name ?? statusId}”`) }
    const send = async (version?: number) => apiRequest<Requirement>(`/requirements/${id}`, { method: 'PATCH', body: JSON.stringify({ statusId, version, summary: `通过看板将状态变更为“${targetStatus?.name ?? statusId}”` }) })
    try {
      let updated: Requirement
      try { updated = await send(current?.version) }
      catch (error) {
        if (!(error instanceof ApiError) || error.status !== 409) throw error
        const latest = await apiRequest<Requirement>(`/requirements/${id}`)
        updated = await send(latest.version)
      }
      setData((previous) => ({ ...previous, requirements: previous.requirements.map((item) => item.id === id ? updated : item) }))
      return { ok: true }
    } catch (error) { const reason = handleApiError(error); setLastOperationError(reason); return { ok: false, reason } }
  }, [currentUser.role, data.requirements, data.statuses, handleApiError, updateRequirement])

  const deleteRequirement = useCallback(async (id: string) => {
    if (currentUser.role !== 'admin') return false
    const target = data.requirements.find((requirement) => requirement.id === id)
    if (API_MODE) { try { await apiRequest<void>(`/requirements/${id}`, { method: 'DELETE' }) } catch (error) { handleApiError(error); return false } }
    else if (target) void deleteAttachmentBlobs(target.attachments.map((attachment) => attachment.id))
    setData((previous) => ({ ...previous, requirements: previous.requirements.filter((item) => item.id !== id).map((item) => item.parentId === id ? { ...item, parentId: null, version: (item.version ?? 1) + 1, updatedAt: nowIso() } : item) })); return true
  }, [currentUser.role, data.requirements, handleApiError])

  const addComment = useCallback(async (id: string, content: string) => {
    const trimmed = content.trim(); if (!trimmed) return
    if (API_MODE) {
      await apiRequest(`/requirements/${id}/comments`, { method: 'POST', body: JSON.stringify({ content: trimmed }) })
      const updated = await apiRequest<Requirement>(`/requirements/${id}`)
      setData((previous) => ({ ...previous, requirements: previous.requirements.map((item) => item.id === id ? updated : item) })); return
    }
    const timestamp = nowIso(); setData((previous) => ({ ...previous, requirements: previous.requirements.map((requirement) => requirement.id === id ? { ...requirement, version: (requirement.version ?? 1) + 1, updatedAt: timestamp, comments: [...requirement.comments, { id: crypto.randomUUID(), authorId: currentUser.id, content: trimmed, createdAt: timestamp }], history: [{ id: crypto.randomUUID(), actorId: currentUser.id, action: '添加评论', detail: trimmed.length > 30 ? `${trimmed.slice(0, 30)}…` : trimmed, createdAt: timestamp }, ...requirement.history] } : requirement) }))
  }, [currentUser.id])

  const uploadAttachment = useCallback(async (id: string, file: File, onProgress?: (progress: number) => void) => {
    if (API_MODE) {
      const session = await apiRequest<UploadSession>(`/requirements/${id}/attachments/uploads`, { method: 'POST', body: JSON.stringify({ fileName: file.name, contentType: file.type, totalSize: file.size }) })
      for (let index = 0; index < session.totalChunks; index++) {
        const start = index * session.chunkSize; const chunk = file.slice(start, Math.min(file.size, start + session.chunkSize))
        await apiRequest<void>(`/attachments/uploads/${session.uploadId}/chunks/${index}`, { method: 'PUT', headers: { 'Content-Type': 'application/octet-stream' }, body: chunk })
        onProgress?.(Math.round(((index + 1) / session.totalChunks) * 95))
      }
      const completed = await apiRequest<CompleteUpload>(`/attachments/uploads/${session.uploadId}/complete`, { method: 'POST' })
      onProgress?.(100)
      setData((previous) => ({ ...previous, requirements: previous.requirements.map((item) => item.id === id ? { ...item, attachments: [...item.attachments, completed.attachment], updatedAt: nowIso(), version: (item.version ?? 1) + 1 } : item) }))
      return completed.attachment
    }
    for (const progress of [8, 30, 52, 74, 96, 100]) { onProgress?.(progress); await wait(90) }
    const attachment: Attachment = { id: crypto.randomUUID(), name: file.name, size: file.size, type: file.type, uploadedBy: currentUser.id, createdAt: nowIso() }
    await saveAttachmentBlob(attachment.id, file)
    setData((previous) => ({ ...previous, requirements: previous.requirements.map((item) => item.id === id ? { ...item, attachments: [...item.attachments, attachment], updatedAt: nowIso(), version: (item.version ?? 1) + 1 } : item) }))
    return attachment
  }, [currentUser.id])

  const deleteAttachment = useCallback(async (requirementId: string, attachmentId: string): Promise<OperationResult> => {
    try {
      if (API_MODE) await apiRequest(`/attachments/${attachmentId}`, { method: 'DELETE' })
      else await deleteAttachmentBlobs([attachmentId])
      setData(previous => ({ ...previous, requirements: previous.requirements.map(item => item.id === requirementId ? { ...item, attachments: item.attachments.filter(a => a.id !== attachmentId), version: (item.version ?? 1) + 1 } : item) }))
      return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])
  const loadAttachmentBlob = useCallback(async (attachment: Attachment) => API_MODE ? apiBlob(`/attachments/${attachment.id}`) : getAttachmentBlob(attachment.id).then((blob) => blob ?? null), [])
  const setCurrentUser = useCallback((id: string) => { if (!API_MODE) setData((previous) => ({ ...previous, currentUserId: id })) }, [])

  const updateUserRole = useCallback(async (id: string, role: AppData['users'][number]['role']) => {
    if (API_MODE) await apiRequest(`/users/${id}`, { method: 'PATCH', body: JSON.stringify({ role }) })
    setData((previous) => ({ ...previous, users: previous.users.map((user) => user.id === id ? { ...user, role } : user) }))
  }, [])

  const createUser = useCallback(async (input: { account: string; name: string; role: User['role']; password: string; color?: string; weComEmail?: string | null }): Promise<OperationResult> => {
    try { const user: User = API_MODE ? await apiRequest('/users', { method: 'POST', body: JSON.stringify(input) }) : { id: `u-${crypto.randomUUID()}`, account: input.account, name: input.name, role: input.role, password: undefined, initials: input.name.slice(0,1), color: input.color ?? '#0a84ff', wecomBound: Boolean(input.weComEmail), active: true } as User; setData((previous) => ({ ...previous, users: [...previous.users, user] })); return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])

  const validateWeComEmail = useCallback(async (email: string): Promise<{ ok: boolean; reason?: string; profile?: WeComUserProfile }> => {
    const trimmed = email.trim(); if (!trimmed) return { ok: false, reason: '请输入企业邮箱' }
    if (!API_MODE) return { ok: true, profile: { userId: `wx-${trimmed.split('@')[0]}`, name: 'Mock 企微成员', departments: [1], status: 1, active: true } }
    try { return { ok: true, profile: await apiRequest<WeComUserProfile>('/users/wecom/validate', { method: 'POST', body: JSON.stringify({ email: trimmed }) }) } }
    catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])

  const bindWeComUser = useCallback(async (id: string, email: string): Promise<OperationResult> => {
    try { const user: User = API_MODE ? await apiRequest(`/users/${id}/wecom/bind`, { method: 'POST', body: JSON.stringify({ email }) }) : { ...data.users.find((item) => item.id === id)!, wecomBound: true }; setData((previous) => ({ ...previous, users: previous.users.map((item) => item.id === id ? user : item) })); return { ok: true } }
    catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [data.users, handleApiError])

  const unbindWeComUser = useCallback(async (id: string): Promise<OperationResult> => {
    try { const user: User = API_MODE ? await apiRequest(`/users/${id}/wecom/bind`, { method: 'DELETE' }) : { ...data.users.find((item) => item.id === id)!, wecomBound: false }; setData((previous) => ({ ...previous, users: previous.users.map((item) => item.id === id ? user : item) })); return { ok: true } }
    catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [data.users, handleApiError])

  const updateUser = useCallback(async (id: string, input: { password?: string; role?: User['role'] }): Promise<OperationResult> => {
    try { const user: User = API_MODE ? await apiRequest(`/users/${id}`, { method: 'PATCH', body: JSON.stringify(input) }) : { ...data.users.find((item) => item.id === id)!, ...input }; setData((previous) => ({ ...previous, users: previous.users.map((item) => item.id === id ? user : item) })); return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [data.users, handleApiError])

  const deleteUser = useCallback(async (id: string): Promise<OperationResult> => {
    try {
      if (API_MODE) {
        await apiRequest<void>(`/users/${id}`, { method: 'DELETE' })
        await refresh()
        return { ok: true }
      }
      const deletedUser: User = { id: 'u-deleted', account: '__deleted__', name: '已删除用户', role: 'developer', initials: '删', color: '#71717a', wecomBound: false, active: false }
      setData((previous) => ({
        ...previous,
        users: [...previous.users.filter((user) => user.id !== id && user.id !== deletedUser.id), deletedUser],
        requirementDefaults: {
          ...previous.requirementDefaults,
          assigneeId: previous.requirementDefaults.assigneeId === id ? null : previous.requirementDefaults.assigneeId,
          reviewerId: previous.requirementDefaults.reviewerId === id ? null : previous.requirementDefaults.reviewerId,
        },
        requirements: previous.requirements.map((item) => ({
          ...item,
          assigneeId: assigneeIds(item).filter(value => value !== id)[0] ?? null,
          assigneeIds: assigneeIds(item).filter(value => value !== id),
          reviewerId: item.reviewerId === id ? null : item.reviewerId,
          creatorId: item.creatorId === id ? deletedUser.id : item.creatorId,
          comments: item.comments.map((comment) => comment.authorId === id ? { ...comment, authorId: deletedUser.id } : comment),
          history: item.history.map((entry) => entry.actorId === id ? { ...entry, actorId: deletedUser.id } : entry),
          attachments: item.attachments.map((attachment) => attachment.uploadedBy === id ? { ...attachment, uploadedBy: deletedUser.id } : attachment),
        })),
      }))
      return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError, refresh])

  const addModule = useCallback(async (name: string): Promise<OperationResult> => {
    const trimmed = name.trim(); if (!trimmed) return { ok: false, reason: '请输入模块名称' }; if (data.modules.some((item) => item.toLowerCase() === trimmed.toLowerCase())) return { ok: false, reason: '该模块已存在' }
    try {
      if (API_MODE) { const created = await apiRequest<ApiModule>('/modules', { method: 'POST', body: JSON.stringify({ name: trimmed }) }); setModuleIds((previous) => ({ ...previous, [created.name]: created.id })) }
      setData((previous) => ({ ...previous, modules: [...previous.modules, trimmed] })); return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [data.modules, handleApiError])


  const renameModule = useCallback(async (oldName: string, name: string): Promise<OperationResult> => {
    const trimmed = name.trim()
    if (!trimmed || data.modules.some(x => x !== oldName && x.toLowerCase() === trimmed.toLowerCase())) return { ok: false, reason: '名称不能为空或重复' }
    try {
      if (API_MODE) await apiRequest(`/modules/${moduleIds[oldName]}`, { method: 'PATCH', body: JSON.stringify({ name: trimmed }) })
      setModuleIds(previous => { const next = { ...previous, [trimmed]: previous[oldName] }; if (oldName !== trimmed) delete next[oldName]; return next })
      setData(previous => ({ ...previous, modules: previous.modules.map(x => x === oldName ? trimmed : x), requirements: previous.requirements.map(x => x.module === oldName ? { ...x, module: trimmed, version: (x.version ?? 0) + 1 } : x), requirementDefaults: { ...previous.requirementDefaults, module: previous.requirementDefaults.module === oldName ? trimmed : previous.requirementDefaults.module } }))
      return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [data.modules, moduleIds, handleApiError])
  const setStatusProtection = useCallback(async (id: string, value: boolean): Promise<OperationResult> => {
    try {
      if (API_MODE) await apiRequest(`/statuses/${id}`, { method: 'PATCH', body: JSON.stringify({ protected: value }) })
      setData(previous => ({ ...previous, statuses: previous.statuses.map(x => x.id === id ? { ...x, protected: value } : x) }))
      return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])
  const reorderStatuses = useCallback(async (ids: string[]): Promise<OperationResult> => {
    try {
      if (API_MODE) {
        const statuses = await apiRequest<StatusDefinition[]>('/statuses/order', { method: 'PUT', body: JSON.stringify({ ids }) })
        setData(previous => ({ ...previous, statuses }))
      } else setData(previous => ({ ...previous, statuses: ids.map(id => previous.statuses.find(x => x.id === id)!) }))
      return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])

  const removeModule = useCallback(async (name: string): Promise<OperationResult> => {
    if (!API_MODE) {
      if (data.requirements.some((requirement) => requirement.module === name)) return { ok: false, reason: '仍有需求使用此模块，不能删除' }
      if (data.modules.length <= 1) return { ok: false, reason: '至少保留一个模块' }
    }
    try { if (API_MODE) await apiRequest<void>(`/modules/${moduleIds[name]}`, { method: 'DELETE' }); setData((previous) => ({ ...previous, modules: previous.modules.filter((item) => item !== name) })); return { ok: true } }
    catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [data.modules.length, data.requirements, handleApiError, moduleIds])

  const addStatus = useCallback(async (name: string, color: string): Promise<OperationResult> => {
    const trimmed = name.trim(); if (!trimmed) return { ok: false, reason: '请输入状态名称' }
    try {
      const status: StatusDefinition = API_MODE ? await apiRequest('/statuses', { method: 'POST', body: JSON.stringify({ name: trimmed, color }) }) : { id: `status-${crypto.randomUUID()}`, name: trimmed, color, terminal: false }
      setData((previous) => ({ ...previous, statuses: [...previous.statuses, status] })); return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])

  const renameStatus = useCallback(async (id: string, name: string): Promise<OperationResult> => {
    const trimmed = name.trim(); if (!trimmed) return { ok: false, reason: '状态名称不能为空' }
    try { if (API_MODE) await apiRequest(`/statuses/${id}`, { method: 'PATCH', body: JSON.stringify({ name: trimmed }) }); setData((previous) => ({ ...previous, statuses: previous.statuses.map((status) => status.id === id ? { ...status, name: trimmed } : status) })); return { ok: true } }
    catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])

  const removeStatus = useCallback(async (id: string): Promise<OperationResult> => {
    if (!API_MODE) { if (data.statuses.length <= 1) return { ok: false, reason: '至少保留一个状态' }; if (data.requirements.some((item) => item.statusId === id)) return { ok: false, reason: '仍有需求使用此状态，请先迁移需求' } }
    try { if (API_MODE) await apiRequest<void>(`/statuses/${id}`, { method: 'DELETE' }); setData((previous) => ({ ...previous, statuses: previous.statuses.filter((item) => item.id !== id) })); return { ok: true } }
    catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [data.requirements, data.statuses, handleApiError])

  const addCustomField = useCallback(async (field: Omit<CustomField, 'id' | 'enabled'>): Promise<OperationResult> => {
    try {
      const created: CustomField = API_MODE ? await apiRequest('/custom-fields', { method: 'POST', body: JSON.stringify(field) }) : { ...field, id: `cf-${crypto.randomUUID()}`, enabled: true }
      setData((previous) => ({ ...previous, customFields: [...previous.customFields, created] })); return { ok: true }
    } catch (error) { return { ok: false, reason: handleApiError(error) } }
  }, [handleApiError])

  const toggleCustomField = useCallback(async (id: string) => {
    const field = data.customFields.find((item) => item.id === id); if (!field) return
    if (API_MODE) await apiRequest(`/custom-fields/${id}`, { method: 'PATCH', body: JSON.stringify({ enabled: !field.enabled }) })
    setData((previous) => ({ ...previous, customFields: previous.customFields.map((item) => item.id === id ? { ...item, enabled: !item.enabled } : item) }))
  }, [data.customFields])

  const addRequirementType = useCallback(async (name: string): Promise<OperationResult> => { try { const created: RequirementType = API_MODE ? await apiRequest('/requirement-types', { method: 'POST', body: JSON.stringify({ name }) }) : { id: crypto.randomUUID(), name, color: nextRequirementTypeColor(data.requirementTypes), sortOrder: data.requirementTypes.length + 1, enabled: true }; setData((previous) => ({ ...previous, requirementTypes: [...previous.requirementTypes, created] })); return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } } }, [data.requirementTypes, handleApiError])
  const updateRequirementType = useCallback(async (id: string, input: Partial<RequirementType>): Promise<OperationResult> => { try { const updated: RequirementType = API_MODE ? await apiRequest(`/requirement-types/${id}`, { method: 'PATCH', body: JSON.stringify(input) }) : { ...data.requirementTypes.find((item) => item.id === id)!, ...input }; setData((previous) => ({ ...previous, requirementTypes: previous.requirementTypes.map((item) => item.id === id ? updated : item) })); return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } } }, [data.requirementTypes, handleApiError])
  const updateRequirementDefaults = useCallback(async (input: Partial<RequirementDefaults>): Promise<OperationResult> => { try { const payload = { ...input, clearAssignee: input.assigneeId === null, clearReviewer: input.reviewerId === null, clearRequirementType: input.requirementTypeId === null, clearDueDateOffset: input.dueDateOffsetDays === null }; const updated: RequirementDefaults = API_MODE ? await apiRequest('/requirement-defaults', { method: 'PATCH', body: JSON.stringify(payload) }) : { ...data.requirementDefaults, ...input }; setData((previous) => ({ ...previous, requirementDefaults: updated })); return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } } }, [data.requirementDefaults, handleApiError])
  const updateBranding = useCallback(async (projectName: string): Promise<OperationResult> => { try { const branding: SystemBranding = API_MODE ? await apiRequest('/system-branding', { method: 'PATCH', body: JSON.stringify({ projectName }) }) : { ...data.branding, projectName, updatedAt: nowIso() }; setData((previous) => ({ ...previous, branding })); return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } } }, [data.branding, handleApiError])
  const uploadBrandingBackground = useCallback(async (file: File): Promise<OperationResult> => { try { if (API_MODE) { const form = new FormData(); form.append('file', file); const branding = await apiRequest<SystemBranding>('/system-branding/background', { method: 'POST', body: form }); setData((previous) => ({ ...previous, branding })) } else { await saveAttachmentBlob('branding-background', file); setData((previous) => ({ ...previous, branding: { ...previous.branding, loginBackgroundUrl: URL.createObjectURL(file), updatedAt: nowIso() } })) } return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } } }, [handleApiError])
  const getWorkCalendar = useCallback(async (year: number) => API_MODE ? apiRequest<WorkCalendarDay[]>(`/work-calendar?year=${year}`) : [], [])
  const updateWorkCalendar = useCallback(async (date: string, isWorkday: boolean, note?: string): Promise<OperationResult> => { if (!API_MODE) return { ok: true }; try { await apiRequest(`/work-calendar/${date}`, { method: 'PUT', body: JSON.stringify({ isWorkday, note }) }); return { ok: true } } catch (error) { return { ok: false, reason: handleApiError(error) } } }, [handleApiError])
  const queryRequirementTree = useCallback(async (query: RequirementTreeQuery): Promise<RequirementTreePage> => { if (!API_MODE) throw new Error('Mock mode uses local tree query'); const params = new URLSearchParams(); Object.entries(query).forEach(([key,value]) => { if (value !== undefined && value !== '') params.set(key,String(value)) }); return apiRequest(`/requirements/tree?${params}`) }, [])

  const importRequirements = useCallback(async (file: File, commit: boolean): Promise<RequirementImportResult> => {
    if (!API_MODE) throw new Error('批量导入需要连接服务端')
    const form = new FormData(); form.append('file', file)
    const result = await apiRequest<RequirementImportResult>(`/requirements/import?commit=${commit}`, { method: 'POST', body: form })
    if (commit && result.importedRows > 0) await refresh()
    return result
  }, [refresh])

  const resetDemo = useCallback(async () => { if (API_MODE) await refresh(); else { await clearAttachmentBlobs(); setData(cloneInitialData()) } }, [refresh])

  const value = useMemo<AppStoreValue>(() => ({ ...data, currentUser, mode: API_MODE ? 'api' : 'mock', authenticated, initializing, lastOperationError, login, logout, refresh, createRequirement, updateRequirement, moveRequirementStatus, deleteRequirement, addComment, uploadAttachment, deleteAttachment, loadAttachmentBlob, setCurrentUser, updateUserRole, createUser, validateWeComEmail, bindWeComUser, unbindWeComUser, updateUser, deleteUser, addModule, renameModule, setStatusProtection, reorderStatuses, removeModule, addStatus, renameStatus, removeStatus, addCustomField, toggleCustomField, addRequirementType, updateRequirementType, updateRequirementDefaults, updateBranding, uploadBrandingBackground, getWorkCalendar, updateWorkCalendar, queryRequirementTree, importRequirements, resetDemo }), [data, currentUser, authenticated, initializing, lastOperationError, login, logout, refresh, createRequirement, updateRequirement, moveRequirementStatus, deleteRequirement, addComment, uploadAttachment, deleteAttachment, loadAttachmentBlob, setCurrentUser, updateUserRole, createUser, validateWeComEmail, bindWeComUser, unbindWeComUser, updateUser, deleteUser, addModule, renameModule, setStatusProtection, reorderStatuses, removeModule, addStatus, renameStatus, removeStatus, addCustomField, toggleCustomField, addRequirementType, updateRequirementType, updateRequirementDefaults, updateBranding, uploadBrandingBackground, getWorkCalendar, updateWorkCalendar, queryRequirementTree, importRequirements, resetDemo])
  return <AppStoreContext.Provider value={value}>{children}</AppStoreContext.Provider>
}

export function useAppStore() { const value = useContext(AppStoreContext); if (!value) throw new Error('useAppStore must be used inside AppStoreProvider'); return value }
import { assigneeIds } from './assignees'
