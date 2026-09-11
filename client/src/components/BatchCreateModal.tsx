import { AssigneePicker } from './AssigneePicker'
import { useEffect, useRef, useState } from 'react'
import { useAppStore } from '../store'
import { apiRequest, ApiError } from '../api'
import { columns, parseClipboard, type BatchField, type BatchRow } from '../batchDraft'
import { chinaDateKey, priorityLabel } from '../utils'
import { requirementLink, copyText } from '../copyRequirementLink'
import type { CreateRequirementInput, Priority } from '../types'

interface Draft { rows: BatchRow[]; requestId: string; pending: boolean; ids?: string[] }
export function BatchCreateModal({ onClose, onOpen }: { onClose: () => void; onOpen: (id: string) => void }) {
  const { currentUser, users, modules, statuses, iterations, requirementTypes, requirementDefaults: defaults, refresh, mode } = useAppStore()
  const storageKey = `g43-batch-draft:${currentUser.id}`
  function emptyRow(): BatchRow { return { key: crypto.randomUUID(), title: '', description: defaults.descriptionTemplate ?? '', assignee: users.find(x => x.id === defaults.assigneeId)?.account ?? '', reviewer: users.find(x => x.id === defaults.reviewerId)?.account ?? '', priority: priorityLabel[defaults.priority ?? 'medium'], module: defaults.module ?? modules[0] ?? '', type: requirementTypes.find(x => x.id === defaults.requirementTypeId)?.name ?? requirementTypes.find(x => x.enabled)?.name ?? '', due: defaults.dueDateOffsetDays == null ? '' : chinaDateKey(new Date(Date.now() + defaults.dueDateOffsetDays * 86400000)), iteration: iterations.find(x => x.id === (defaults.iterationMode === 'specific' ? defaults.iterationId : defaults.iterationMode === 'current' ? iterations.find(x => x.state === 'active')?.id : null))?.name ?? '', status: statuses.find(x => x.id === defaults.statusId)?.name ?? '未开始' } }
  const [draft, setDraft] = useState<Draft>(() => { try { const saved = JSON.parse(localStorage.getItem(storageKey) ?? 'null'); if (saved && Array.isArray(saved.rows) && saved.rows.length <= 100 && typeof saved.requestId === 'string') return saved } catch { /* Start a fresh draft. */ } return { rows: [emptyRow()], requestId: crypto.randomUUID(), pending: false } })
  const [selected, setSelected] = useState<string[]>([])
  const [paste, setPaste] = useState('')
  const [cells, setCells] = useState<string[][]>([])
  const [mapping, setMapping] = useState<string[]>([])
  const [hasHeader, setHasHeader] = useState(false)
  const [field, setField] = useState<BatchField>('reviewer')
  const [value, setValue] = useState('')
  const [overwrite, setOverwrite] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [storageError, setStorageError] = useState('')
  const [copyMessage, setCopyMessage] = useState('')
  const gate = useRef(false)
  useEffect(() => { try { localStorage.setItem(storageKey, JSON.stringify(draft)); setStorageError('') } catch { setStorageError('草稿保存失败，请勿关闭页面；检查浏览器存储空间') } }, [draft, storageKey])
  const activeUsers = users.filter(x => x.active !== false)
  const options: Partial<Record<BatchField, string[]>> = { assignee: activeUsers.map(x => x.account), reviewer: activeUsers.map(x => x.account), module: modules, priority: Object.values(priorityLabel), type: requirementTypes.filter(x => x.enabled).map(x => x.name), iteration: iterations.map(x => x.name), status: statuses.filter(x => currentUser.role === 'admin' || !x.protected).map(x => x.name) }
  const lookup = (account: string) => activeUsers.find(x => x.account.toLowerCase() === account.trim().toLowerCase())
  function rowErrors(row: BatchRow) {
    const errors: Partial<Record<BatchField, string>> = {}
    if (!row.title.trim() || row.title.length > 500) errors.title = '标题必填，最多500字'
    if (!row.description.trim()) errors.description = '描述必填'
    for (const key of ['assignee', 'reviewer'] as const) if (row[key].trim() && (key === 'assignee' ? row[key].split(';').filter(Boolean).some(account => !lookup(account)) : !lookup(row[key]))) errors[key] = '唯一账号不存在或已停用'
    for (const key of ['module', 'priority', 'type', 'status'] as const) if (!options[key]?.includes(row[key])) errors[key] = '请选择有效选项'
    if (row.iteration && !options.iteration?.includes(row.iteration)) errors.iteration = '迭代不存在'
    if (row.due && (!/^\d{4}-\d{2}-\d{2}$/.test(row.due) || Number.isNaN(Date.parse(row.due)) || new Date(row.due).toISOString().slice(0,10) !== row.due)) errors.due = '日期格式 YYYY-MM-DD'
    return errors
  }
  const errors = draft.rows.map(rowErrors)
  const invalid = errors.filter(x => Object.keys(x).length).length
  const duplicates = draft.rows.filter((row, i, all) => row.title.trim() && all.findIndex(x => x.title.trim() === row.title.trim()) !== i).length
  function edit(rows: BatchRow[]) { setConfirm(false); setError(''); setDraft({ rows, requestId: crypto.randomUUID(), pending: false }) }
  function inspectPaste() {
    try { if (paste.length > 1_000_000) throw new Error('粘贴内容过大'); const table = parseClipboard(paste); if (!table.length || table.length > 101) throw new Error('每批最多100行'); const guessed = table[0].map(text => columns.find(([, label]) => label === text.trim())?.[0] ?? (text.trim() === '负责人' ? 'assignee' : text.trim() === '需求描述' ? 'description' : text.trim() === '需求标题' ? 'title' : '')); const header = guessed.includes('title'); setCells(table); setHasHeader(header); setMapping(Array.from({ length: Math.max(...table.map(row => row.length)) }, (_, i) => header ? guessed[i] ?? '' : columns[i]?.[0] ?? '')); setError('') } catch (err) { setError(String(err instanceof Error ? err.message : err)) }
  }
  function appendPaste() {
    const fields = mapping.filter(Boolean)
    if (!fields.includes('title') || new Set(fields).size !== fields.length) { setError('必须映射标题，且同一字段不能重复映射'); return }
    const added = cells.slice(hasHeader ? 1 : 0).map(line => { const row = emptyRow(); mapping.forEach((key, i) => { if (key) row[key as BatchField] = (line[i] ?? '').trim() }); return row })
    const existing = draft.rows.filter(row => row.title.trim() || row.description.trim() !== (defaults.descriptionTemplate ?? '').trim())
    if (!added.length || existing.length + added.length > 100) { setError('每批支持1～100条，请减少粘贴行数'); return }
    edit([...existing, ...added]); setCells([]); setPaste('')
  }
  function payload(): CreateRequirementInput[] { return draft.rows.map(row => ({ title: row.title.trim(), description: row.description.trim(), assigneeIds: row.assignee.split(';').filter(Boolean).map(account => lookup(account)!.id), assigneeId: lookup(row.assignee.split(';')[0])?.id ?? null, reviewerId: lookup(row.reviewer)?.id ?? null, priority: Object.entries(priorityLabel).find(([, label]) => label === row.priority)![0] as Priority, module: row.module, requirementTypeId: requirementTypes.find(x => x.name === row.type)!.id, statusId: statuses.find(x => x.name === row.status)!.id, iterationId: iterations.find(x => x.name === row.iteration)?.id ?? null, dueDate: row.due || null, parentId: null })) }
  async function commit() {
    if (gate.current || invalid || !draft.rows.length) return
    gate.current = true; setBusy(true); setError('')
    const pending = { ...draft, pending: true }
    try {
      if (mode !== 'api') throw new Error('批量创建需要连接服务端，请使用本地API预览')
      localStorage.setItem(storageKey, JSON.stringify(pending)); setDraft(pending)
      const result = await apiRequest<{ ids: string[] }>('/requirements/batch', { method: 'POST', body: JSON.stringify({ requestId: draft.requestId, items: payload() }) })
      const completed = { ...pending, pending: false, ids: result.ids }; setDraft(completed)
      try { localStorage.setItem(storageKey, JSON.stringify(completed)) } catch { /* Replay is safe using the same request ID. */ }
      try { await refresh() } catch { /* Results remain available even if refreshing fails. */ }
    } catch (err) {
      setError(err instanceof Error ? err.message : '提交失败，请重试')
      if (err instanceof ApiError && err.status === 400) setDraft({ ...draft, pending: false, requestId: crypto.randomUUID() })
    } finally { gate.current = false; setBusy(false) }
  }
  const frozen = busy || draft.pending
  return <div className="modal-layer"><section className="create-modal batch-create" role="dialog" aria-modal="true" aria-label="批量新建需求">
    <header><div><span className="eyebrow">BATCH CREATE</span><h2>批量新建需求</h2></div><button className="button button--ghost" disabled={busy} onClick={onClose}>关闭并保留草稿</button></header>
    <div className="create-modal__body">
      {storageError && <p role="alert">{storageError}</p>}{error && <p className="form-error" role="alert">{error}</p>}
      {draft.ids && <div><button className="button button--ghost" onClick={() => void copyText(draft.ids!.map(id => `${id} ${requirementLink(id)}`).join('\n')).then(() => setCopyMessage('单号及链接已复制')).catch(() => setCopyMessage('复制失败，请从下方文本框全选复制'))}>复制本批单号及链接</button><span role="status">{copyMessage}</span></div>}
      {draft.ids ? <section><h3>已成功创建 {draft.ids.length} 条需求</h3><p>点击单号查看需求；以下为本批创建结果。</p><div className="batch-results">{draft.ids.map((id, i) => <button className="button button--ghost" key={id} onClick={() => onOpen(id)}>{id} · {draft.rows[i]?.title}</button>)}</div><label className="field">本批单号及链接（可全选复制）<textarea readOnly rows={6} value={draft.ids.map(id => `${id} ${requirementLink(id)}`).join('\n')} /></label><button className="button button--primary" onClick={() => edit([emptyRow()])}>开始下一批</button></section> : <>
      <p>每批最多100条。人员按唯一账号匹配；空的人员、迭代和日期沿用平台新建默认规则。草稿仅保存在当前账号的本机浏览器。</p>
      <fieldset disabled={frozen} className="batch-fieldset">
        <details><summary>从 Excel 或标题列表粘贴</summary><textarea aria-label="粘贴批量需求" rows={5} value={paste} onChange={event => setPaste(event.target.value)} placeholder="一行一个标题，或粘贴 Excel 多行多列" /><button className="button button--ghost" onClick={inspectPaste}>识别列</button>
        {cells.length > 0 && <><label><input type="checkbox" checked={hasHeader} onChange={event => setHasHeader(event.target.checked)} />首行为表头</label><div className="batch-mapping">{mapping.map((key, i) => <label key={i}>第{i + 1}列：{cells[0][i]?.slice(0,30)}<select value={key} onChange={event => setMapping(previous => previous.map((old, j) => i === j ? event.target.value : old))}><option value="">忽略</option>{columns.map(([id, label]) => <option value={id} key={id}>{label}</option>)}</select></label>)}</div><button className="button button--primary" onClick={appendPaste}>追加到表格</button></>}
        </details>
        <div className="batch-tools"><strong>统一设置（已选 {selected.length} 行）</strong><select aria-label="统一设置字段" value={field} onChange={event => { setField(event.target.value as BatchField); setValue('') }}>{columns.filter(([key]) => !['title','description'].includes(key)).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select>{field === 'assignee' ? <AssigneePicker label="统一设置处理人" users={activeUsers} value={value.split(';').map(account => lookup(account)?.id).filter((id): id is string => Boolean(id))} onChange={ids => setValue(ids.map(id => activeUsers.find(user => user.id === id)!.account).join(';'))} /> : <input aria-label="统一设置值" list={`batch-${field}`} value={value} onChange={event => setValue(event.target.value)} placeholder="选择或输入值" />}<label><input type="checkbox" checked={overwrite} onChange={event => setOverwrite(event.target.checked)} />覆盖已有值</label><button className="button button--ghost" disabled={!selected.length} onClick={() => edit(draft.rows.map(row => selected.includes(row.key) && (overwrite || !row[field]) ? { ...row, [field]: value } : row))}>应用到选中行</button></div>
        {Object.entries(options).map(([key, values]) => <datalist id={`batch-${key}`} key={key}>{values!.map(v => <option value={v} key={v} />)}</datalist>)}
        <div className="batch-grid"><table><thead><tr><th><input aria-label="选择全部草稿行" type="checkbox" checked={draft.rows.length > 0 && draft.rows.every(row => selected.includes(row.key))} onChange={event => setSelected(event.target.checked ? draft.rows.map(row => row.key) : [])} /></th><th>#</th>{columns.map(([key, label]) => <th key={key}>{label}</th>)}<th>操作</th></tr></thead><tbody>{draft.rows.map((row, index) => <tr key={row.key}><td><input aria-label={`选择第${index + 1}行`} type="checkbox" checked={selected.includes(row.key)} onChange={event => setSelected(previous => event.target.checked ? [...previous, row.key] : previous.filter(key => key !== row.key))} /></td><td>{index + 1}</td>{columns.map(([key, label]) => <td key={key}>{key === 'assignee' ? <AssigneePicker label={`第${index + 1}行处理人`} users={activeUsers} value={row.assignee.split(';').map(account => lookup(account)?.id).filter((id): id is string => Boolean(id))} onChange={ids => edit(draft.rows.map(entry => entry.key === row.key ? { ...entry, assignee: ids.map(id => activeUsers.find(user => user.id === id)!.account).join(';') } : entry))} /> : <textarea rows={key === 'description' ? 3 : 1} aria-label={`第${index + 1}行${label}`} aria-invalid={Boolean(errors[index][key])} value={row[key]} onChange={event => edit(draft.rows.map(entry => entry.key === row.key ? { ...entry, [key]: event.target.value } : entry))} />}{key !== 'assignee' && options[key] && <select aria-label={`第${index + 1}行选择${label}`} value={options[key]!.includes(row[key]) ? row[key] : ''} onChange={event => edit(draft.rows.map(entry => entry.key === row.key ? { ...entry, [key]: event.target.value } : entry))}><option value="">选择</option>{options[key]!.map(v => <option key={v}>{v}</option>)}</select>}{errors[index][key] && <small>{errors[index][key]}</small>}</td>)}<td><button onClick={() => edit(draft.rows.filter(entry => entry.key !== row.key))}>删除行</button></td></tr>)}</tbody></table></div>
        <button className="button button--ghost" disabled={draft.rows.length >= 100} onClick={() => edit([...draft.rows, emptyRow()])}>＋添加一行</button>
      </fieldset>
      {duplicates > 0 && <p>有 {duplicates} 条重复标题，请核对（不阻止提交）。</p>}
      {draft.pending && <p role="alert">上次提交结果待确认，编辑已锁定。请重试同一批次以获取结果，不会重复创建。</p>}
      {confirm && <div className="batch-confirm"><h3>即将创建 {draft.rows.length} 条需求</h3><p>{Object.entries(draft.rows.reduce<Record<string, number>>((counts, row) => { const key = row.assignee || '默认处理人/未分配'; counts[key] = (counts[key] ?? 0) + 1; return counts }, {})).map(([key, count]) => `${key}：${count}条`).join('；')}</p><p>创建成功后按现有规则发送企微通知；本地预览不发送。整批成功或整批不创建。</p><button className="button button--primary" disabled={busy} onClick={() => void commit()}>确认创建</button><button className="button button--ghost" disabled={frozen} onClick={() => setConfirm(false)}>返回编辑</button></div>}
      </>}
    </div>
    {!draft.ids && <footer><span>共 {draft.rows.length} 条 · {invalid} 条需要补充</span><div><button className="button button--ghost" disabled={frozen} onClick={() => { if (window.confirm('确认放弃当前批量草稿？')) { localStorage.removeItem(storageKey); onClose() } }}>放弃草稿</button><button className="button button--primary" disabled={busy || invalid > 0 || !draft.rows.length} onClick={() => draft.pending ? void commit() : setConfirm(true)}>{busy ? '正在创建…' : draft.pending ? '重试并查询结果' : '检查并创建'}</button></div></footer>}
  </section></div>
}
