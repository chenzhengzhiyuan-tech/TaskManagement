import { AssigneePicker } from '../components/AssigneePicker'
import { assigneeIds, assigneeMatches } from '../assignees'
import { RequirementFilters, filterMatches, isMyTask } from '../components/RequirementFilters'
import {
  ArrowDownAZ,
  Check,  ChevronLeft,
  ChevronRight,
  FileDown,
  FileUp,
  FolderTree,
  ListFilter,
  Minus,
  Plus,
  Search,
  X,
} from 'lucide-react'
import { useEffect, useState } from 'react'
import { useRequirementFilter } from '../useRequirementFilter'
import { RequirementImportModal } from '../components/RequirementImportModal'
import { BatchCreateModal } from '../components/BatchCreateModal'
import { InlineRequirementFields } from '../components/InlineRequirementFields'
import { useAppStore, type RequirementTreeGroup, type RequirementTreePage } from '../store'
import type { Requirement } from '../types'
import { formatDate, getUser, isOverdue, parentFinalStatusBlockReason, priorityLabel, priorityRank, requirementMatches } from '../utils'

interface RequirementsPageProps {
  initialQuery: string
  onOpenRequirement: (id: string) => void
}

const PAGE_SIZE = 50

import { useIterationFilter } from '../useIterationFilter'
import { compareTaskStatus } from '../requirementSort'

type SortKey = 'updated' | 'priority' | 'assignee' | 'due' | 'status'

export function RequirementsPage({ initialQuery, onOpenRequirement }: RequirementsPageProps) {
  const { requirements, statuses, users, iterations, customFields, requirementTypes, mode, currentUser, updateRequirement, queryRequirementTree } = useAppStore()
  const [query, setQuery] = useRequirementFilter(currentUser.id, 'query', initialQuery)
  const [statusFilter, setStatusFilter] = useRequirementFilter(currentUser.id, 'status')
  const [assigneeFilter, setAssigneeFilter] = useRequirementFilter(currentUser.id, 'assignee')
  const [priorityFilter, setPriorityFilter] = useRequirementFilter(currentUser.id, 'priority')
  const [iterationFilter, setIterationFilter] = useIterationFilter(currentUser.id)
  const [typeFilter, setTypeFilter] = useRequirementFilter(currentUser.id, 'type')
  const [mine, setMine] = useRequirementFilter(currentUser.id, 'mine')
  const [apiTree, setApiTree] = useState<RequirementTreePage | null>(null)
  const [loading, setLoading] = useState(false)
  const [sort, setSort] = useState<SortKey>('priority')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<string[]>([])
  const [collapsedParents, setCollapsedParents] = useState<string[]>([])
  const [toast, setToast] = useState('')
  const [bulkAssignee, setBulkAssignee] = useState<string[]>([])
  const [bulkIteration, setBulkIteration] = useState('')
  const [importOpen, setImportOpen] = useState(false)
  const [batchOpen, setBatchOpen] = useState(false)

  useEffect(() => { if (initialQuery) setQuery(initialQuery) }, [initialQuery, setQuery])

  const hasCriteria = Boolean(mine || query || statusFilter || assigneeFilter || priorityFilter || iterationFilter || typeFilter)
  const activeFilters = [statusFilter, assigneeFilter, priorityFilter, iterationFilter, typeFilter, mine].filter(Boolean).length

  const matches = (item: Requirement) => requirementMatches(item, query)
    && (!mine || isMyTask(item, currentUser.id))
    && filterMatches(statusFilter, item.statusId)
    && assigneeMatches(assigneeFilter, item)
    && filterMatches(priorityFilter, item.priority)
    && filterMatches(iterationFilter, item.iterationId)
    && filterMatches(typeFilter, item.requirementTypeId)

  const compareRoots = (a: Requirement, b: Requirement) => {
    if (sort === 'status') return compareTaskStatus(a, b)
    if (sort === 'priority') return priorityRank[b.priority] - priorityRank[a.priority] || b.updatedAt.localeCompare(a.updatedAt)
    if (sort === 'assignee') return (a.assigneeId ? 0 : 1) - (b.assigneeId ? 0 : 1) || (getUser(users, a.assigneeId)?.name ?? '').localeCompare(getUser(users, b.assigneeId)?.name ?? '') || b.updatedAt.localeCompare(a.updatedAt)
    if (sort === 'due') return (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999') || b.updatedAt.localeCompare(a.updatedAt)
    return b.updatedAt.localeCompare(a.updatedAt)
  }

  const matchingRequirements = requirements.filter(matches)

  const localGroups: RequirementTreeGroup[] = (() => {
    const knownIds = new Set(requirements.map((item) => item.id))
    const roots = requirements.filter((item) => !item.parentId || !knownIds.has(item.parentId))
    return roots
      .map((root) => {
        const allChildren = requirements.filter((item) => item.parentId === root.id).sort((a, b) => b.id.localeCompare(a.id))
        const rootMatches = matches(root)
        const matchedChildren = allChildren.filter(matches)
        return {
          root,
          rootMatches,
          children: hasCriteria ? matchedChildren : allChildren,
        }
      })
      .filter((group) => group.rootMatches || group.children.length > 0)
      .sort((a, b) => compareRoots(a.root, b.root))
  })()

  useEffect(() => {
    if (mode !== 'api') return
    setLoading(true)
    void queryRequirementTree({ mine: Boolean(mine), query, statusId: statusFilter, assigneeId: assigneeFilter, priority: priorityFilter, iterationId: iterationFilter, requirementTypeId: typeFilter, page, pageSize: PAGE_SIZE, sort })
      .then(setApiTree).finally(() => setLoading(false))
  }, [mode, query, statusFilter, assigneeFilter, priorityFilter, iterationFilter, typeFilter, page, sort, requirements, queryRequirementTree, mine])
  const rootCount = mode === 'api' ? apiTree?.rootCount ?? 0 : localGroups.length
  const resultCount = mode === 'api' ? apiTree?.requirementCount ?? 0 : matchingRequirements.length
  const groups = mode === 'api' ? apiTree?.groups ?? [] : localGroups
  const pageCount = Math.max(1, Math.ceil(rootCount / PAGE_SIZE))
  const visibleGroups = mode === 'api' ? groups : groups.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  const visibleItems = visibleGroups.flatMap((group) => { const expanded = hasCriteria || !collapsedParents.includes(group.root.id); return [group.root, ...(expanded ? group.children : [])] })

  useEffect(() => {
    if (page > pageCount) setPage(pageCount)
  }, [page, pageCount])

  const clearFilters = () => {
    setMine('')
    setStatusFilter(''); setAssigneeFilter(''); setPriorityFilter(''); setIterationFilter(''); setTypeFilter(''); setQuery(''); setPage(1)
  }

  const notify = (message: string) => {
    setToast(message)
    window.setTimeout(() => setToast(''), 1800)
  }

  const updateStatus = async (id: string, statusId: string) => {
    const status = statuses.find((item) => item.id === statusId)
    const requirement = requirements.find((item) => item.id === id)
    const parentStatusBlock = requirement ? parentFinalStatusBlockReason(requirement, statusId, requirements) : null
    if (parentStatusBlock) { notify(parentStatusBlock); return }
    const ok = await updateRequirement(id, { statusId }, `状态变更为“${status?.name}”`)
    notify(ok ? '状态已更新' : '开发人员不能设置为已完成或已关闭')
  }

  const applyBulkAssignee = async () => {
    if (!bulkAssignee.length) return
    await Promise.all(selected.map((id) => updateRequirement(id, { assigneeIds: bulkAssignee, assigneeId: bulkAssignee[0] ?? null }, '批量更新了处理人')))
    notify(`已更新 ${selected.length} 条需求的处理人`); setBulkAssignee([]); setSelected([])
  }

  const applyBulkIteration = async () => {
    if (!bulkIteration) return
    await Promise.all(selected.map((id) => updateRequirement(id, { iterationId: bulkIteration }, '批量更新了所属迭代')))
    notify(`已将 ${selected.length} 条需求加入迭代`); setBulkIteration(''); setSelected([])
  }

  const exportCsv = () => {
    const enabledFields = customFields.filter((field) => field.enabled)
    const header = ['需求编号', '标题', '父需求', '需求单类型', '状态', '处理人', '验收人', '优先级', '模块', '迭代', '期望完成时间', '需求描述', ...enabledFields.map((field) => field.name)]
    const escape = (value: unknown) => `"${String(value ?? '').replaceAll('"', '""')}"`
    const rows = matchingRequirements.map((item) => [
      item.id, item.title, item.parentId ?? '', requirementTypes.find((entry) => entry.id === item.requirementTypeId)?.name ?? '', statuses.find((entry) => entry.id === item.statusId)?.name,
      assigneeIds(item).map(id => getUser(users, id)?.account ?? '').filter(Boolean).join(';'), getUser(users, item.reviewerId ?? null)?.name, priorityLabel[item.priority], item.module,
      iterations.find((entry) => entry.id === item.iterationId)?.name ?? '需求池', item.dueDate ?? '', item.description,
      ...enabledFields.map((field) => Array.isArray(item.customValues[field.id]) ? (item.customValues[field.id] as string[]).join('、') : item.customValues[field.id] ?? ''),
    ])
    const csv = `\ufeff${[header, ...rows].map((row) => row.map(escape).join(',')).join('\r\n')}`
    const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
    const link = document.createElement('a'); link.href = url; link.download = `需求导出-${new Date().toISOString().slice(0, 10)}.csv`; link.click(); URL.revokeObjectURL(url)
    notify(`已导出 ${matchingRequirements.length} 条需求`)
  }

  const toggleParent = (id: string) => setCollapsedParents((previous) => previous.includes(id) ? previous.filter((item) => item !== id) : [...previous, id])

  const renderRow = (item: Requirement, parent: Requirement | null, childCount: number, contextOnly = false) => {
    const status = statuses.find((entry) => entry.id === item.statusId)
    const assignee = getUser(users, item.assigneeId)
    const iteration = iterations.find((entry) => entry.id === item.iterationId)
    const requirementType = requirementTypes.find((entry) => entry.id === item.requirementTypeId)
    const overdue = isOverdue(item, statuses)
    const isChild = Boolean(parent)
    const collapsed = collapsedParents.includes(item.id)
    return (
      <tr key={item.id} className={`${selected.includes(item.id) ? 'selected ' : ''}${isChild ? 'requirement-row--child' : 'requirement-row--root'}${contextOnly ? ' requirement-row--context' : ''}`}>
        <td className="check-cell"><input aria-label={`选择 ${item.id}`} type="checkbox" checked={selected.includes(item.id)} onChange={(event) => setSelected(event.target.checked ? Array.from(new Set([...selected, item.id])) : selected.filter((id) => id !== item.id))} /></td>
        <td className="title-cell">
          <div className={`tree-node ${isChild ? 'tree-node--child' : 'tree-node--root'}`}>
            {isChild ? <span className="tree-connector" aria-hidden="true" /> : childCount > 0 ? <button className="tree-toggle" type="button" aria-label={`${collapsed ? '展开' : '收起'} ${item.id} 子需求`} onClick={() => toggleParent(item.id)}>{collapsed ? <Plus size={12} /> : <Minus size={12} />}</button> : <span className="tree-toggle tree-toggle--empty" />}
            <div className="requirement-title-block"><button className="requirement-title-main" type="button" onClick={() => onOpenRequirement(item.id)}><span className="mono-id">{item.id}</span><strong>{item.title}</strong>{!isChild && childCount > 0 && <span className="child-count" title={`${childCount} 条子需求`}><FolderTree size={12} />{childCount}</span>}{contextOnly && <span className="context-match-dot" title="筛选结果来自下级节点" />}</button></div>
          </div>
        </td>
        <td><span className="requirement-type-cell" style={{ '--type-color': requirementType?.color ?? '#8E8E93' } as React.CSSProperties}>{requirementType?.name ?? '未设置'}</span></td>
        <td><div className="inline-select inline-select--status" style={{ '--status-color': status?.color } as React.CSSProperties}><i /><select aria-label={`${item.id} 状态`} value={item.statusId} onChange={(event) => void updateStatus(item.id, event.target.value)}>{statuses.map((entry) => { const blockedByChildren = Boolean(parentFinalStatusBlockReason(item, entry.id, requirements)); return <option key={entry.id} value={entry.id} disabled={(currentUser.role === 'developer' && entry.protected) || blockedByChildren}>{entry.name}{blockedByChildren ? '（需先完成全部子任务）' : ''}</option> })}</select></div></td>
        <td><div className="inline-select inline-select--user">{assignee ? <span className="avatar avatar--tiny" style={{ '--avatar-color': assignee.color } as React.CSSProperties}>{assignee.initials}</span> : <span className="avatar avatar--tiny avatar--empty">—</span>}<AssigneePicker label={`${item.id} 处理人`} value={assigneeIds(item)} users={users} onChange={ids => updateRequirement(item.id, { assigneeIds: ids, assigneeId: ids[0] ?? null }, '更新了处理人')} /></div></td>
        <td><InlineRequirementFields item={item} field="priority" /></td>
        <td><InlineRequirementFields item={item} field="module" /></td>
        <td><span className="muted-cell">{iteration?.name.slice(4, 8) ?? '需求池'}</span></td>
        <td><InlineRequirementFields item={item} field="dueDate" />{overdue && <small className="date-overdue">超期</small>}</td>
        <td><span className="muted-cell">{formatDate(item.updatedAt)}</span></td>
      </tr>
    )
  }

  return (
    <div className="page requirements-page">
      <header className="page-header"><div><span className="eyebrow">REQUIREMENTS</span><h1>需求</h1><p>以顶层需求为排序和分页单位，子需求始终跟随父需求展示。</p></div><div className="page-header__summary"><span>{resultCount}</span><small>{rootCount} 个顶层需求</small></div></header>

      <RequirementFilters mine={mine} setMine={value => { setMine(value); setPage(1) }} query={query} setQuery={value => { setQuery(value); setPage(1) }} values={[statusFilter, assigneeFilter, priorityFilter, iterationFilter, typeFilter]} setters={[setStatusFilter, setAssigneeFilter, setPriorityFilter, setIterationFilter, setTypeFilter].map(set => value => { set(value); setPage(1) })}>
        {currentUser.role === 'admin' && <button className="button button--ghost button--compact" onClick={() => setImportOpen(true)}><FileUp size={14} />导入</button>}
        <button className="button button--ghost button--compact" onClick={() => setBatchOpen(true)}><Plus size={14} />批量新建</button>
        <button className="button button--ghost button--compact" onClick={exportCsv}><FileDown size={14} />导出</button>
      </RequirementFilters>
      {batchOpen && <BatchCreateModal onClose={() => setBatchOpen(false)} onOpen={id => { setBatchOpen(false); onOpenRequirement(id) }} />}

        <div className="list-context-bar"><div>{activeFilters > 0 && <button type="button" onClick={clearFilters}><ListFilter size={14} />已启用 {activeFilters} 个筛选 <X size={13} /></button>}</div><label><ArrowDownAZ size={14} />顶层需求排序<select value={sort} onChange={(event) => { setSort(event.target.value as SortKey); setPage(1) }}><option value="updated">最近更新</option><option value="priority">优先级</option><option value="status">任务状态</option><option value="assignee">处理人</option><option value="due">截止时间</option></select></label></div>

      {selected.length > 0 && <div className="bulk-bar"><strong>已选择 {selected.length} 条</strong><label className="bulk-control"><AssigneePicker label="批量处理人" value={bulkAssignee} users={users} onChange={setBulkAssignee} /><button className="button button--ghost button--compact" type="button" disabled={!bulkAssignee.length} onClick={() => void applyBulkAssignee()}>应用</button></label><label className="bulk-control"><select aria-label="批量迭代" value={bulkIteration} onChange={(event) => setBulkIteration(event.target.value)}><option value="">选择迭代</option>{iterations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select><button className="button button--ghost button--compact" type="button" disabled={!bulkIteration} onClick={() => void applyBulkIteration()}>应用</button></label><button className="icon-button icon-button--small" type="button" aria-label="取消批量选择" onClick={() => setSelected([])}><X size={14} /></button></div>}

      <section className="requirements-table-wrap">
        <table className="requirements-table requirements-table--tree">
          <thead><tr><th className="check-cell"><input aria-label="选择当前页" type="checkbox" checked={visibleItems.length > 0 && visibleItems.every((item) => selected.includes(item.id))} onChange={(event) => setSelected(event.target.checked ? Array.from(new Set([...selected, ...visibleItems.map((item) => item.id)])) : selected.filter((id) => !visibleItems.some((item) => item.id === id)))} /></th><th>需求树</th><th>需求单类型</th><th>状态</th><th>处理人</th><th>优先级</th><th>模块</th><th>迭代</th><th>期望完成</th><th>更新时间</th></tr></thead>
          <tbody>{visibleGroups.flatMap((group) => { const expanded = hasCriteria || !collapsedParents.includes(group.root.id); return [renderRow(group.root, null, requirements.filter((item)=>item.parentId===group.root.id).length, !group.rootMatches), ...(expanded ? group.children.map((child) => renderRow(child, group.root, 0)) : [])] })}</tbody>
        </table>
        {loading && <div className="list-loading">正在查询服务端…</div>}{!loading && !groups.length && <div className="empty-state"><Search size={28} /><strong>没有符合条件的需求</strong><span>尝试清空筛选条件或使用其他关键词</span><button className="button button--ghost" type="button" onClick={clearFilters}>清空筛选</button></div>}
      </section>

      <footer className="table-footer"><span>共 {resultCount} 条匹配 · {rootCount} 个顶层需求 · 第 {page}/{pageCount} 页</span><div><button className="icon-button icon-button--small" type="button" disabled={page === 1} onClick={() => setPage((value) => value - 1)}><ChevronLeft size={15} /></button>{Array.from({ length: pageCount }, (_, index) => index + 1).map((value) => <button type="button" key={value} className={`page-number ${value === page ? 'active' : ''}`} onClick={() => setPage(value)}>{value}</button>)}<button className="icon-button icon-button--small" type="button" disabled={page === pageCount} onClick={() => setPage((value) => value + 1)}><ChevronRight size={15} /></button></div></footer>
      {toast && <div className="toast"><Check size={15} />{toast}</div>}
      <RequirementImportModal open={importOpen} onClose={() => setImportOpen(false)} onImported={(count) => { setImportOpen(false); notify(`已批量创建 ${count} 条需求`) }} />
    </div>
  )
}


