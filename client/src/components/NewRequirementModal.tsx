import { uuid } from '../uuid'
import { ExistingChildren, NewChildren } from './NewChildren'
import { AssigneePicker } from './AssigneePicker'
import { assigneeIds } from '../assignees'
import { CalendarDays, Plus, X } from 'lucide-react'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useAppStore } from '../store'
import type { CreateRequirementInput, Priority } from '../types'
import { chinaDateKey, priorityLabel } from '../utils'
import { ParentRequirementPicker } from './ParentRequirementPicker'
import { DraftImages, type DraftImage } from './DraftImages'

interface NewRequirementModalProps { open: boolean; onClose: () => void; onCreated: (id: string) => void }

export function NewRequirementModal({ open, onClose, onCreated }: NewRequirementModalProps) {
  const { users, statuses, iterations, requirements, modules, requirementTypes, requirementDefaults, currentUser, createRequirement, uploadAttachment } = useAppStore()
  const activeUsers = useMemo(() => users.filter((user) => user.active !== false), [users])
  const currentIteration = iterations.find((item) => item.state === 'active')
  const buildDefault = useCallback((): CreateRequirementInput => ({
    title: '', module: requirementDefaults.module ?? modules[0] ?? 'UI', priority: requirementDefaults.priority ?? 'medium', statusId: requirementDefaults.statusId ?? 'todo',
    assigneeId: requirementDefaults.assigneeId, reviewerId: requirementDefaults.reviewerId, requirementTypeId: requirementDefaults.requirementTypeId ?? requirementTypes.find((item) => item.enabled)?.id ?? null,
    iterationId: requirementDefaults.iterationMode === 'none' ? null : requirementDefaults.iterationMode === 'specific' ? requirementDefaults.iterationId : currentIteration?.id ?? null,
    parentId: null, dueDate: requirementDefaults.dueDateOffsetDays == null ? null : chinaDateKey(new Date(Date.now() + requirementDefaults.dueDateOffsetDays * 86400000)),
    description: requirementDefaults.descriptionTemplate ?? '',
  }), [requirementDefaults, modules, requirementTypes, currentIteration?.id])
  const [form, setForm] = useState<CreateRequirementInput>(buildDefault)
  const [children, setChildren] = useState<CreateRequirementInput[]>([])
  const [existingChildIds, setExistingChildIds] = useState<string[]>([])
  const submission = useRef({key: '', id: ''})
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const busy = useRef(false)
  const [createdId, setCreatedId] = useState<string | null>(null)
  const [images, setImages] = useState<DraftImage[]>([])
  const imagesRef = useRef(images)
  useEffect(() => { imagesRef.current = images }, [images])
  useEffect(() => () => { imagesRef.current.forEach(image => URL.revokeObjectURL(image.url)) }, [])
  function clearImages() { imagesRef.current.forEach(image => URL.revokeObjectURL(image.url)); setImages([]); setCreatedId(null) }

  const wasOpen = useRef(false)
  useEffect(() => {
    // A background iteration refresh must not erase a draft already being edited.
    if (open && !wasOpen.current) { setForm(buildDefault()); setChildren([]); setExistingChildIds([]); submission.current = {key: '', id: ''}; setError(''); clearImages() }
    wasOpen.current = open
  }, [open, buildDefault])
  function resetAndClose() { if (busy.current) return; const id = createdId; clearImages(); setForm(buildDefault()); setError(''); if (id) onCreated(id); else onClose() }
  async function submit() {
    if (busy.current) return
    if (!form.title.trim()) { setError('请填写需求标题'); return }
    if (!form.description.trim()) { setError('请填写需求描述'); return }
    if (!form.requirementTypeId) { setError('请选择需求单类型'); return }
    const invalid = children.findIndex(child => !child.title.trim() || !child.description.trim() || !child.requirementTypeId)
    if (invalid >= 0) { setError(`第 ${invalid + 1} 个子需求：请填写标题、描述和需求类型`); document.querySelector<HTMLInputElement>(`[aria-label="子需求 ${invalid + 1} 标题"]`)?.focus(); return }
    if (form.parentId && (children.length || existingChildIds.length)) { setError('不能同时指定父需求并创建子需求'); return }
    busy.current = true; setSubmitting(true)
    try {
      const payload = { ...form, title: form.title.trim(), description: form.description.trim() }
      const key = JSON.stringify({payload, children, existingChildIds})
      if (submission.current.key !== key) submission.current = {key, id: uuid()}
      const id = createdId ?? await createRequirement(payload, {requestId: submission.current.id, children, existingChildIds})
      setCreatedId(id)
      let failed = false
      for (const image of images.filter(image => !image.done)) {
        const update = (patch: Partial<DraftImage>) => setImages(previous => previous.map(entry => entry.id === image.id ? { ...entry, ...patch } : entry))
        try { update({ error: undefined }); await uploadAttachment(id, image.file, progress => update({ progress })); update({ done: true, progress: 100 }) }
        catch { failed = true; update({ error: '上传失败，可重试' }) }
      }
      if (failed) { setError(`需求 ${id} 已创建，部分附件上传失败。重试只上传失败图片，不会重复建单。`); return }
      clearImages(); setForm(buildDefault()); setError(''); onCreated(id)
    }
    catch (error) { setError(error instanceof Error ? error.message : '创建需求失败') }
    finally { busy.current = false; setSubmitting(false) }
  }
  useEffect(() => { if (!open) return; const handler=(event:KeyboardEvent)=>{if(event.key==='Escape')resetAndClose();if((event.ctrlKey||event.metaKey)&&event.key==='Enter')void submit()};window.addEventListener('keydown',handler);return()=>window.removeEventListener('keydown',handler) })
  if (!open) return null

  return <div className="modal-layer" onMouseDown={(event) => event.target === event.currentTarget && resetAndClose()}><div className="create-modal" role="dialog" aria-modal="true" aria-labelledby="create-title">
    <header><div><span className="eyebrow">NEW REQUIREMENT</span><h2 id="create-title">新建需求</h2></div><button className="icon-button" type="button" aria-label="关闭新建需求" onClick={resetAndClose}><X size={18}/></button></header>
    <div className="create-modal__body">
      <fieldset className="create-fields" disabled={submitting || Boolean(createdId)} style={{ border: 0, padding: 0, margin: 0, minWidth: 0 }}>
      <label className="field field--wide"><span>需求标题 <em>*</em></span><input autoFocus value={form.title} onChange={(event)=>{setForm({...form,title:event.target.value});setError('')}} placeholder="用一句话清晰描述需求"/></label>
      <div className="form-grid">
        <label className="field"><span>需求单类型 <em>*</em></span><select value={form.requirementTypeId ?? ''} onChange={(event)=>setForm({...form,requirementTypeId:event.target.value||null})}><option value="">请选择</option>{requirementTypes.filter((item)=>item.enabled).map((item)=><option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="field"><span>归属模块</span><select value={form.module} onChange={(event)=>setForm({...form,module:event.target.value})}>{modules.map((item)=><option key={item}>{item}</option>)}</select></label>
        <label className="field"><span>优先级</span><select value={form.priority} onChange={(event)=>setForm({...form,priority:event.target.value as Priority})}>{(Object.keys(priorityLabel) as Priority[]).map((item)=><option key={item} value={item}>{priorityLabel[item]}</option>)}</select></label>
        <label className="field"><span>初始状态</span><select value={form.statusId} onChange={(event)=>setForm({...form,statusId:event.target.value})}>{statuses.map((item)=><option key={item.id} value={item.id} disabled={currentUser.role==='developer'&&item.protected}>{item.name}</option>)}</select></label>
        <div className="field"><span>处理人</span><AssigneePicker users={activeUsers} value={assigneeIds(form)} onChange={ids => setForm({...form, assigneeIds: ids, assigneeId: ids[0] ?? null})} /></div>
        <div className="field"><span>验收人</span><AssigneePicker label="验收人" single users={activeUsers} value={form.reviewerId ? [form.reviewerId] : []} onChange={ids => setForm({...form, reviewerId: ids[0] ?? null})}/></div>
        <label className="field"><span>所属迭代</span><select value={form.iterationId ?? ''} onChange={(event)=>setForm({...form,iterationId:event.target.value||null})}><option value="">需求池</option>{iterations.map((item)=><option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
        <label className="field"><span>期望完成时间</span><div className="input-with-icon"><CalendarDays size={15}/><input type="date" value={form.dueDate ?? ''} onChange={(event)=>setForm({...form,dueDate:event.target.value||null})}/></div></label>
      </div>
      <label className="field field--wide field--description"><span>需求描述 <em>*</em></span><textarea value={form.description} onChange={(event)=>{setForm({...form,description:event.target.value});setError('')}} placeholder="输入需求背景、目标、范围和验收说明…"/></label>
      <section className="new-children-section"><h3>父需求</h3><ParentRequirementPicker requirements={requirements} value={form.parentId} onChange={(parentId)=>setForm({...form,parentId})}/></section>
      <section className="new-children-section"><h3>子需求</h3><p>关联已有需求，或批量新建直接子需求。最多 100 条。</p>
        {form.parentId ? <p>当前需求已指定父需求，不能继续创建孙需求。清空父需求后可添加子需求。</p> : <>
          <ExistingChildren selected={existingChildIds} onChange={setExistingChildIds}/>
          <NewChildren rows={children} onChange={setChildren}/>
          <button className="button button--ghost" type="button" disabled={children.length + existingChildIds.length >= 100} onClick={() => setChildren([...children, {...form, title: '', description: '', parentId: null, statusId: 'todo', assigneeIds: [...assigneeIds(form)]}])}>新增子需求一行</button>
          {!!children.length && <p>新增行带入当前父需求的字段，可逐行修改；之后修改父需求不会覆盖已填行。</p>}
        </>}
      </section>
      </fieldset>
      <DraftImages items={images} setItems={setImages} disabled={submitting} />
      {error&&<div className="form-error" role="alert">{error}</div>}
    </div>
    <footer><span>{createdId ? `已创建 ${createdId}` : '按 Ctrl / Cmd + Enter 快速创建'}</span><div><button className="button button--ghost" type="button" disabled={submitting} onClick={resetAndClose}>{createdId ? '完成并查看需求' : '取消'}</button><button className="button button--primary" type="button" onClick={()=>void submit()} disabled={submitting}><Plus size={16}/>{submitting?'提交中…':createdId?'重试附件上传':'创建需求'}</button></div></footer>
  </div></div>
}
