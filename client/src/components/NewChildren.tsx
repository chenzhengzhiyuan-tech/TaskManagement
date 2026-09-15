import { createPortal } from 'react-dom'
import { useDropdown } from './useDropdown'
import type { CreateRequirementInput } from '../types'
import { useAppStore } from '../store'
import { AssigneePicker } from './AssigneePicker'
import { assigneeIds } from '../assignees'
import { priorityLabel } from '../utils'
import { useState } from 'react'
export function ExistingChildren({ selected, onChange, exclude = [] }: { selected: string[]; onChange: (ids: string[]) => void; exclude?: string[] }) {
  const { requirements } = useAppStore()
  const [query, setQuery] = useState('')
  const { open, setOpen, root, menu, position } = useDropdown(380)
  const rows = requirements.filter(x => !exclude.includes(x.id) && `${x.id} ${x.title}`.toLowerCase().includes(query.trim().toLowerCase()))
  return <div ref={root} className="existing-children"><input aria-label="搜索已有子需求" placeholder="按单号或标题搜索已有需求" value={query} onClick={() => setOpen(true)} onFocus={() => setOpen(true)} onChange={e => { setQuery(e.target.value); setOpen(true) }}/>
    {!!selected.length && <div className="child-selected">{selected.map(id => <button type="button" key={id} onClick={() => onChange(selected.filter(x => x !== id))}>{id} · {requirements.find(x => x.id === id)?.title} ×</button>)}</div>}
    {open && createPortal(<div ref={menu} className="existing-children__results relation-options dropdown-menu" role="group" aria-label="子需求选项" style={{position: 'fixed', ...position, zIndex: 350}}>{rows.map(item => { const reason = item.parentId ? '该需求已有父需求' : requirements.some(x => x.parentId === item.id) ? '该需求包含子需求' : ''
      return <label key={item.id} aria-disabled={!!reason}><input type="checkbox" disabled={!!reason} checked={selected.includes(item.id)} onChange={e => onChange(e.target.checked ? [...selected, item.id] : selected.filter(id => id !== item.id))}/><span><strong>{item.id}</strong> {item.title}<small>{reason || `${item.module} · ${item.description.slice(0, 100)}`}</small></span></label>
    })}{!rows.length && <p>没有匹配的需求</p>}</div>, document.body)}
  </div>
}
export function NewChildren({ rows, onChange }: { rows: CreateRequirementInput[]; onChange: (rows: CreateRequirementInput[]) => void }) {
  const { users, iterations, modules, requirementTypes } = useAppStore()
  function patch(index: number, change: Partial<CreateRequirementInput>) { onChange(rows.map((row, i) => i === index ? {...row, ...change} : row)) }
  return <div className="new-children">{rows.map((row, i) => <section className="new-child" key={i} aria-label={`新子需求 ${i + 1}`}>
    <header><strong>子需求 {i + 1}</strong><button type="button" className="button button--ghost button--compact" onClick={() => onChange(rows.filter((_, j) => i !== j))}>删除第 {i + 1} 行</button></header>
    <label className="field"><span>子需求标题 *</span><input aria-label={`子需求 ${i + 1} 标题`} value={row.title} onChange={e => patch(i, {title: e.target.value})}/></label>
    <div className="form-grid">
      <div className="field"><span>处理人</span><AssigneePicker label={`子需求 ${i + 1} 处理人`} users={users} value={assigneeIds(row)} onChange={ids => patch(i, {assigneeIds: ids, assigneeId: ids[0] ?? null})}/></div>
      <div className="field"><span>验收人</span><AssigneePicker single label={`子需求 ${i + 1} 验收人`} users={users} value={row.reviewerId ? [row.reviewerId] : []} onChange={ids => patch(i, {reviewerId: ids[0] ?? null})}/></div>
      <label className="field"><span>优先级</span><select value={row.priority} onChange={e => patch(i, {priority: e.target.value as CreateRequirementInput['priority']})}>{Object.entries(priorityLabel).map(([id, name]) => <option value={id} key={id}>{name}</option>)}</select></label>
      <label className="field"><span>模块</span><select value={row.module} onChange={e => patch(i, {module: e.target.value})}>{modules.map(x => <option key={x}>{x}</option>)}</select></label>
      <label className="field"><span>迭代</span><select value={row.iterationId ?? ''} onChange={e => patch(i, {iterationId: e.target.value || null})}><option value="">需求池</option>{iterations.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
      <label className="field"><span>需求单类型 *</span><select value={row.requirementTypeId ?? ''} onChange={e => patch(i, {requirementTypeId: e.target.value})}><option value="">请选择</option>{requirementTypes.filter(x => x.enabled).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
      <label className="field"><span>期望完成时间</span><input type="date" value={row.dueDate ?? ''} onChange={e => patch(i, {dueDate: e.target.value || null})}/></label>
    </div>
    <label className="field"><span>子需求描述 *</span><textarea aria-label={`子需求 ${i + 1} 描述`} value={row.description} onChange={e => patch(i, {description: e.target.value})}/></label>
  </section>)}</div>
}
