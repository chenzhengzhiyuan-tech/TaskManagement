import { attachmentAccept, attachmentError, attachmentHint } from '../attachmentMedia'
import { MediaContent } from './MediaContent'
import { uuid } from '../uuid'
import { AssigneePicker } from './AssigneePicker'
import { assigneeIds, assigneeNames } from '../assignees'
import { AttachmentThumbnail } from './AttachmentThumbnail'
import { copyRequirementLink } from '../copyRequirementLink'
import {
  Check,
  Download,
  ImageOff,
  ChevronRight,
  Link2,
  Maximize2,
  MessageSquare,
  Paperclip,
  Save,
  Trash2,
  Upload,
  X,
} from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import type { Attachment, Priority } from '../types'
import { useAppStore } from '../store'
import { formatDate, getUser, parentFinalStatusBlockReason, priorityLabel } from '../utils'
import { ConfirmDialog } from './ConfirmDialog'
import { ParentRequirementPicker } from './ParentRequirementPicker'
import { ChildRequirementPicker } from './ChildRequirementPicker'
import { CommentsPanel } from './CommentsPanel'

interface RequirementDrawerProps {
  requirementId: string | null
  onClose: () => void
  onOpenRequirement: (id: string) => void
}

type DrawerTab = 'overview' | 'comments' | 'history'

export function RequirementDrawer({ requirementId, onClose, onOpenRequirement }: RequirementDrawerProps) {
  const {
    requirements,
    statuses,
    users,
    iterations,
    modules,
    requirementTypes,
    currentUser,
    updateRequirement,
    deleteRequirement,
    uploadAttachment,
    loadAttachmentBlob,
    deleteAttachment,
  } = useAppStore()
  const requirement = requirements.find((item) => item.id === requirementId)
  const activeUsers = users.filter((user) => user.active !== false)
  const [tab, setTab] = useState<DrawerTab>(() => new URLSearchParams(window.location.search).has('comment') ? 'comments' : 'overview')
  const [expanded, setExpanded] = useState(false)
  const [title, setTitle] = useState(requirement?.title ?? '')
  const [description, setDescription] = useState(requirement?.description ?? '')
  const [commentDirty, setCommentDirty] = useState(false)
  const [descriptionEditing, setDescriptionEditing] = useState(false)
  const [dirty, setDirty] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [uploads, setUploads] = useState<{ id: string; name: string; progress: number; error?: string }[]>([])
  const [deleteImage, setDeleteImage] = useState<Attachment | null>(null)
  const [previewAttachment, setPreviewAttachment] = useState<Attachment | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [previewError, setPreviewError] = useState('')
  const fileRef = useRef<HTMLInputElement>(null)
  const previewRequest = useRef(0)
  useEffect(() => () => { previewRequest.current++ }, [])

  useEffect(() => {
    const handleKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && requirementId) { if (previewAttachment) closePreview(); else requestClose() }
      if ((event.ctrlKey || event.metaKey) && event.key === 'Enter' && dirty) saveText()
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  })

  const children = useMemo(
    () => requirements.filter((item) => item.parentId === requirement?.id),
    [requirement?.id, requirements],
  )

  useEffect(() => {
    if (!requirementId || tab === 'comments') return
    const paste = (event: ClipboardEvent) => {
      const target = event.target
      if (target instanceof Element && target.closest('input,textarea,select,[contenteditable]:not([contenteditable="false"]),[role="textbox"]')) return
      const files = Array.from(event.clipboardData?.items ?? []).filter(item => item.kind === 'file' && item.type.startsWith('image/')).map(item => item.getAsFile()).filter((file): file is File => Boolean(file))
      if (files.length) { event.preventDefault(); void handleFiles(files) }
    }
    document.addEventListener('paste', paste)
    return () => document.removeEventListener('paste', paste)
  })
  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl) }, [previewUrl])
  if (!requirement) return null

  const activeRequirement = requirement
  const activeId = requirement.id
  const assignee = getUser(users, requirement.assigneeId)
  const iteration = iterations.find((item) => item.id === requirement.iterationId)

  function requestClose() {
    if ((dirty || commentDirty) && !window.confirm('存在未保存的修改，确定关闭吗？')) return
    onClose()
  }

  async function saveText() {
    if (!title.trim()) { setMessage('需求标题不能为空'); return }
    const ok = await updateRequirement(activeId, { title: title.trim(), description }, '更新了标题或需求描述')
    if (!ok) { setMessage('保存失败，数据可能已被其他用户修改'); return }
    setDirty(false); setDescriptionEditing(false); setMessage('已保存'); window.setTimeout(() => setMessage(null), 1800)
  }

  async function changeStatus(statusId: string) {
    const target = statuses.find((item) => item.id === statusId)
    const parentStatusBlock = parentFinalStatusBlockReason(activeRequirement, statusId, requirements)
    if (parentStatusBlock) { setMessage(parentStatusBlock); window.setTimeout(() => setMessage(null), 3200); return }
    const ok = await updateRequirement(activeId, { statusId }, `状态变更为“${target?.name ?? statusId}”`)
    if (!ok) { setMessage('状态更新失败或当前身份无权限'); window.setTimeout(() => setMessage(null), 2400) }
  }

  async function handleAttachment(file: File | undefined) {
    if (!file) return
    const id = uuid()
    setUploads(previous => [...previous, { id, name: file.name, progress: 0 }])
    const progress = (value: number) => setUploads(previous => previous.map(item => item.id === id ? { ...item, progress: value } : item))
    try {
      const validation = attachmentError(file)
      if (validation) throw new Error(validation)
      await uploadAttachment(activeId, file, progress); progress(100)
    } catch (error) { setUploads(previous => previous.map(item => item.id === id ? { ...item, error: error instanceof Error ? error.message : '上传失败' } : item)) }
  }
  async function handleFiles(files: File[]) {
    for (const file of files) await handleAttachment(file)
  }

  async function openAttachment(attachment: Attachment) {
    const request = ++previewRequest.current
    if (previewUrl) URL.revokeObjectURL(previewUrl)
    setPreviewAttachment(attachment)
    setPreviewUrl(null)
    setPreviewError('')
    setPreviewLoading(true)
    try {
      const blob = await loadAttachmentBlob(attachment)
      if (request !== previewRequest.current) return
      if (!blob) { setPreviewError('当前附件只有附件记录，未找到本地文件。请重新上传后预览。'); return }
      setPreviewUrl(URL.createObjectURL(blob))
    } catch {
      if (request === previewRequest.current) setPreviewError('附件读取失败，请重试。')
    } finally {
      if (request === previewRequest.current) setPreviewLoading(false)
    }
  }

  function closePreview() {
    previewRequest.current++
    if (previewUrl) URL.revokeObjectURL(previewUrl)
    setPreviewAttachment(null)
    setPreviewUrl(null)
    setPreviewError('')
  }

  async function downloadAttachment(attachment: Attachment) {
    try {
      const blob = await loadAttachmentBlob(attachment)
      if (!blob) { setMessage('未找到附件文件，请重新上传'); return }
      const url = URL.createObjectURL(blob)
      const link = document.createElement('a')
      link.href = url
      link.download = attachment.name
      link.click()
      window.setTimeout(() => URL.revokeObjectURL(url), 500)
    } catch {
      setMessage('下载失败，请重试')
    }
  }

  const deleteDescription = children.length
    ? `${requirement.id} 包含 ${children.length} 条子需求。删除后当前需求及评论、历史和附件将永久移除，子需求会保留并成为顶级需求。`
    : `${requirement.id} 及其评论、历史和附件将永久移除。该操作无法恢复。`

  return (
    <>
      <div className="drawer-backdrop" onMouseDown={(event) => event.target === event.currentTarget && requestClose()}>
        <aside className={`requirement-drawer ${expanded ? 'requirement-drawer--expanded' : ''}`} aria-label={`${requirement.id} 需求详情`}>
          <header className="drawer-header">
            <div className="drawer-header__meta">
              <span className="mono-id">{requirement.id}</span>
              <span className="dot-separator" />
              <span>{formatDate(requirement.updatedAt, true)} 更新</span>
            </div>
            <div className="drawer-header__actions">
              <button className="icon-button" type="button" onClick={() => setExpanded((value) => !value)} aria-label="展开详情">
                <Maximize2 size={17} />
              </button>
              <button className="icon-button" type="button" onClick={requestClose} aria-label="关闭详情">
                <X size={18} />
              </button>
            </div>
          </header>

          <div className="drawer-title-row">
            <textarea
              className="drawer-title-input"
              value={title}
              rows={2}
              onChange={(event) => { setTitle(event.target.value); setDirty(true) }}
              aria-label="需求标题"
            />
            <span className={`priority-dot priority-dot--${requirement.priority}`} title={`优先级：${priorityLabel[requirement.priority]}`} />
          </div>

          <div className="drawer-toolbar">

            <button className="button button--ghost button--compact" type="button" onClick={() => setTab('comments')}>
              <MessageSquare size={15} />{requirement.comments.length}
            </button>
            <button className="button button--ghost button--compact" type="button" onClick={() => fileRef.current?.click()}>
              <Paperclip size={15} />{requirement.attachments.length}
            </button>
            <button className="button button--ghost button--compact" type="button" onClick={() => void copyRequirementLink(requirement.id).then(() => setMessage('任务单地址已复制')).catch(() => setMessage('复制失败，请检查浏览器剪贴板权限'))}>
              <Link2 size={15} />复制地址
            </button>
            <input ref={fileRef} hidden type="file" multiple accept={attachmentAccept} onChange={(event) => { void handleFiles(Array.from(event.target.files ?? [])); event.target.value = '' }} />
            {dirty && <button className="button button--primary button--compact drawer-save" onClick={() => void saveText()} type="button"><Save size={15} />保存</button>}
          </div>

          {message && <div className="inline-message"><Check size={14} />{message}</div>}
          {uploads.map(item => <div className="upload-progress" key={item.id}><span>{item.name} · {item.error ?? (item.progress === 100 ? '已上传' : `${item.progress}%`)}</span><progress max={100} value={item.progress} /></div>)}


          <div className="drawer-tabs">
            {(['overview', 'comments', 'history'] as DrawerTab[]).map((item) => (
              <button key={item} className={tab === item ? 'active' : ''} type="button" onClick={() => setTab(item)}>
                {item === 'overview' ? '详情' : item === 'comments' ? `评论 ${requirement.comments.length}` : '动态'}
              </button>
            ))}
          </div>

          <div className="drawer-body">
            {tab === 'overview' && (
              <>
                <section className="detail-section">
                  <h3>基础信息</h3>
                  <div className="detail-grid">
                    <label><span>状态</span><select value={requirement.statusId} onChange={(event) => void changeStatus(event.target.value)} aria-label="需求状态">
              {statuses.map((item) => {
                const blockedByChildren = Boolean(parentFinalStatusBlockReason(activeRequirement, item.id, requirements))
                const blockedByRole = currentUser.role === 'developer' && item.protected
                return <option key={item.id} value={item.id} disabled={blockedByRole || blockedByChildren}>
                  {item.name}{blockedByRole ? '（仅管理员）' : blockedByChildren ? '（需先完成全部子任务）' : ''}
                </option>
              })}
            </select></label>
                    <label><span>需求单类型</span><select value={requirement.requirementTypeId ?? ''} onChange={(event) => void updateRequirement(requirement.id, { requirementTypeId: event.target.value || null }, '更新了需求单类型')}><option value="">未设置</option>{requirementTypes.filter((item)=>item.enabled).map((item)=><option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
                    <div className="detail-assignees"><span>处理人</span><AssigneePicker users={users} value={assigneeIds(requirement)} onChange={ids => updateRequirement(requirement.id, { assigneeIds: ids, assigneeId: ids[0] ?? null }, '更新了处理人')} /></div>
                    <div className="detail-assignees"><span>验收人</span><AssigneePicker label="验收人" single users={activeUsers} value={requirement.reviewerId ? [requirement.reviewerId] : []} onChange={ids => updateRequirement(requirement.id, { reviewerId: ids[0] ?? null }, '更新了验收人')}/></div>
                    <label><span>优先级</span><select value={requirement.priority} onChange={(event) => void updateRequirement(requirement.id, { priority: event.target.value as Priority }, '更新了优先级')}>{(Object.keys(priorityLabel) as Priority[]).map((item) => <option key={item} value={item}>{priorityLabel[item]}</option>)}</select></label>
                    <label><span>模块</span><select value={requirement.module} onChange={(event) => void updateRequirement(requirement.id, { module: event.target.value }, '更新了归属模块')}>{modules.map((module) => <option key={module}>{module}</option>)}</select></label>
                    <label><span>所属迭代</span><select value={requirement.iterationId ?? ''} onChange={(event) => void updateRequirement(requirement.id, { iterationId: event.target.value || null }, '更新了所属迭代')}><option value="">未规划</option>{iterations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
                    <label><span>期望完成</span><input type="date" value={requirement.dueDate ?? ''} onChange={(event) => void updateRequirement(requirement.id, { dueDate: event.target.value || null }, '更新了期望完成时间')} /></label>
                  </div>

                </section>

                <section className="detail-section">
                  <div className="section-heading"><h3>需求描述</h3>{!descriptionEditing && <button type="button" onClick={() => { setDescription(requirement.description); setDescriptionEditing(true) }}>编辑描述</button>}</div>
                  {descriptionEditing ? <>
                    <textarea className="description-editor" aria-label="需求描述" value={description}
                      onChange={event => { setDescription(event.target.value); setDirty(true) }} placeholder="输入需求背景、目标和验收说明…" />
                    <div className="description-actions"><button className="button button--primary button--compact" type="button" onClick={() => void saveText()}>保存描述</button>
                      <button className="button button--ghost button--compact" type="button" onClick={() => { setDescription(requirement.description); setDescriptionEditing(false); setDirty(title !== requirement.title) }}>取消</button></div>
                  </> : <div className="description-content">{requirement.description || '暂无需求描述'}</div>}
                </section>

                <section className="detail-section detail-relations">
                  <div className="detail-parent-field"><h3>父需求</h3><ParentRequirementPicker requirements={requirements} value={requirement.parentId} excludeId={requirement.id} disabled={children.length > 0} onChange={(parentId) => updateRequirement(requirement.id, { parentId }, parentId ? '更新了父需求' : '移除了父需求')} />{children.length > 0 && <small>当前需求包含子需求，不能再设置为其他需求的子需求。</small>}</div>
                  <div className="section-heading"><h3>子需求</h3><span>{children.length} 条</span></div>
                  {children.length ? (
                    <div className="child-list child-list--managed">
                      {children.map((child) => (
                        <div key={child.id}><button type="button" onClick={() => onOpenRequirement(child.id)}><span className="mono-id">{child.id}</span><span>{child.title}</span><ChevronRight size={15} /></button><button className="child-list__unlink" type="button" aria-label={`解除子需求 ${child.id}`} title="解除父子关系" onClick={() => void updateRequirement(child.id, { parentId: null }, `从 ${requirement.id} 解除子需求关系`)}><X size={14} /></button></div>
                      ))}
                    </div>
                  ) : <div className="empty-inline">暂无子需求</div>}
                  {!requirement.parentId ? <ChildRequirementPicker parentId={requirement.id} /> : <div className="relation-limit-note">当前需求已经是子需求，不能继续绑定下一级子需求。</div>}
                </section>

                <section className="detail-section">
                  <div className="section-heading"><h3>附件</h3><button type="button" onClick={() => fileRef.current?.click()}><Upload size={14} />上传附件</button></div>
                  {requirement.attachments.length ? (
                    <div className="attachment-grid">
                      {requirement.attachments.map(attachment => <AttachmentThumbnail key={attachment.id} attachment={attachment} onOpen={() => void openAttachment(attachment)} onDownload={() => void downloadAttachment(attachment)} onDelete={currentUser.role === 'admin' || currentUser.id === attachment.uploadedBy ? () => setDeleteImage(attachment) : undefined} />)}
                    </div>
                  ) : <button className="upload-empty" type="button" onClick={() => fileRef.current?.click()}><Upload size={20} /><strong>上传附件</strong><span>{attachmentHint} 支持多选或粘贴图片</span></button>}
                </section>

                <section className="detail-section detail-section--danger">
                  <h3>危险操作</h3>
                  <div><span>永久删除需求及其评论、动态和附件</span><button className="button button--danger button--compact" type="button" disabled={currentUser.role !== 'admin'} onClick={() => setDeleteOpen(true)}><Trash2 size={14} />永久删除</button></div>
                  {currentUser.role !== 'admin' && <small>只有管理员可以删除需求</small>}
                </section>
              </>
            )}

            <CommentsPanel requirement={requirement} visible={tab === 'comments'} onDirtyChange={setCommentDirty}
              onOpenImage={attachment => void openAttachment(attachment)} onDownloadImage={attachment => void downloadAttachment(attachment)} />

            {tab === 'history' && (
              <section className="timeline">
                {requirement.history.length ? requirement.history.map((item) => {
                  const actor = getUser(users, item.actorId)
                  return <article key={item.id}><span className="timeline__dot" /><div><header><strong>{actor?.name}</strong><time>{formatDate(item.createdAt, true)}</time></header><p>{item.action}</p><span>{item.detail}</span></div></article>
                }) : <div className="empty-state empty-state--compact"><Link2 size={24} /><strong>暂无操作动态</strong></div>}
              </section>
            )}
          </div>
          <footer className="drawer-footer">
            <span>创建人：{getUser(users, requirement.creatorId)?.name}</span>
            <span>创建于 {formatDate(requirement.createdAt, true)}</span>
            {assignee && <span>当前处理：{assigneeNames(requirement, users)}</span>}
            {iteration && <span>{iteration.name}</span>}
          </footer>
        </aside>
      </div>
      {previewAttachment && (
        <div className="modal-layer attachment-preview-layer" onMouseDown={(event) => event.target === event.currentTarget && closePreview()}>
          <section className="attachment-preview" role="dialog" aria-modal="true" aria-label={`预览 ${previewAttachment.name}`}>
            <header><div><strong>{previewAttachment.name}</strong><span>{(previewAttachment.size / 1024 / 1024).toFixed(1)} MB</span></div><div><button className="button button--ghost button--compact" type="button" onClick={() => downloadAttachment(previewAttachment)}><Download size={14} />下载</button><button className="icon-button" type="button" aria-label="关闭附件预览" onClick={closePreview}><X size={18} /></button></div></header>
            <div className="attachment-preview__body">{previewLoading ? <div className="attachment-preview__empty"><Upload size={26} /><span>正在读取附件…</span></div> : previewUrl ? <MediaContent key={previewUrl} url={previewUrl} type={previewAttachment.type} name={previewAttachment.name} /> : <div className="attachment-preview__empty"><ImageOff size={30} /><strong>无法预览</strong><span>{previewError}</span></div>}</div>
          </section>
        </div>
      )}
      <ConfirmDialog open={Boolean(deleteImage)} title="删除附件" description={`确定删除附件“${deleteImage?.name ?? ''}”？其他附件不受影响。`} onClose={() => setDeleteImage(null)} onConfirm={() => { if (deleteImage) void deleteAttachment(activeId, deleteImage.id).then(result => { if (!result.ok) setMessage(result.reason ?? '删除失败'); setDeleteImage(null) }) }} />
      <ConfirmDialog
        open={deleteOpen}
        title={`永久删除 ${requirement.id}？`}
        description={deleteDescription}
        confirmLabel="永久删除"
        danger
        onClose={() => setDeleteOpen(false)}
        onConfirm={() => void deleteRequirement(requirement.id).then((ok) => { if (ok) { setDeleteOpen(false); onClose() } })}
      />
    </>
  )
}



