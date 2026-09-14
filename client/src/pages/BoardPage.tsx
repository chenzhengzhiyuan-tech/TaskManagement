import { assigneeMatches, assigneeNames } from '../assignees'
import { RequirementFilters, filterMatches, isMyTask } from '../components/RequirementFilters'
import { Check, GripVertical, LockKeyhole, MessageSquare, UserRound } from 'lucide-react'
import { useMemo, useState } from 'react'
import { useAppStore } from '../store'
import { useRequirementFilter } from '../useRequirementFilter'
import { useIterationFilter } from '../useIterationFilter'
import { comparePriorityAndNumber } from '../requirementSort'
import { formatDate, getUser, priorityLabel, requirementMatches } from '../utils'

interface BoardPageProps {
  onOpenRequirement: (id: string) => void
}

export function BoardPage({ onOpenRequirement }: BoardPageProps) {
  const { requirements, statuses, users, currentUser, moveRequirementStatus } = useAppStore()
  const [iterationId, setIterationId] = useIterationFilter(currentUser.id)
  const [assigneeId, setAssigneeId] = useRequirementFilter(currentUser.id, 'assignee')
  const [query, setQuery] = useRequirementFilter(currentUser.id, 'query')
  const [statusId, setStatusId] = useRequirementFilter(currentUser.id, 'status')
  const [priority, setPriority] = useRequirementFilter(currentUser.id, 'priority')
  const [typeId, setTypeId] = useRequirementFilter(currentUser.id, 'type')
  const [mine, setMine] = useRequirementFilter(currentUser.id, 'mine')
  const [draggedId, setDraggedId] = useState<string | null>(null)
  const [toast, setToast] = useState('')

  const visible = useMemo(
    () => requirements.filter((item) => (!mine || isMyTask(item, currentUser.id)) && filterMatches(iterationId, item.iterationId) && assigneeMatches(assigneeId, item) && filterMatches(statusId, item.statusId) && filterMatches(priority, item.priority) && filterMatches(typeId, item.requirementTypeId) && requirementMatches(item, query)),
    [assigneeId, iterationId, statusId, priority, typeId, query, requirements, mine, currentUser.id],
  )

  async function drop(requirementId: string, statusId: string) {
    const requirement = requirements.find((item) => item.id === requirementId)
    const status = statuses.find((item) => item.id === statusId)
    if (!requirement || !status || requirement.statusId === statusId) {
      setDraggedId(null)
      return
    }
    const result = await moveRequirementStatus(requirementId, statusId)
    setToast(result.ok ? `已移动到“${status.name}”` : result.reason ?? '状态移动失败')
    setDraggedId(null)
    window.setTimeout(() => setToast(''), 1800)
  }

  return (
    <div className="page board-page">
      <header className="page-header">
        <div><span className="eyebrow">KANBAN</span><h1>看板</h1><p>拖动卡片快速更新需求状态。</p></div>

      </header>

      <RequirementFilters mine={mine} setMine={setMine} query={query} setQuery={setQuery} values={[statusId, assigneeId, priority, iterationId, typeId]} setters={[setStatusId, setAssigneeId, setPriority, setIterationId, setTypeId]} />

      <div className="kanban" role="list">
        {statuses.map((status) => {
          const cards = visible.filter((item) => item.statusId === status.id).sort(comparePriorityAndNumber)
          const locked = currentUser.role === 'developer' && status.protected
          return (
            <section
              className={`kanban-column ${locked ? 'kanban-column--locked' : ''}`}
              key={status.id}
              onDragOver={(event) => { if (!locked) event.preventDefault() }}
              onDrop={(event) => {
                if (locked) return
                const requirementId = event.dataTransfer.getData('application/x-ground43-requirement') || event.dataTransfer.getData('text/plain') || draggedId
                if (requirementId) void drop(requirementId, status.id)
              }}
            >
              <header><div><span className="kanban-column__dot" style={{ background: status.color }} /><strong>{status.name}</strong><small>{cards.length}</small></div>{locked && <LockKeyhole size={14} />}</header>
              <div className="kanban-cards">
                {cards.map((item) => {
                  const user = getUser(users, item.assigneeId)
                  return (
                    <article
                      role="listitem"
                      className="kanban-card"
                      key={item.id}
                      draggable
                      onDragStart={(event) => {
                        event.dataTransfer.effectAllowed = 'move'
                        event.dataTransfer.setData('application/x-ground43-requirement', item.id)
                        event.dataTransfer.setData('text/plain', item.id)
                        setDraggedId(item.id)
                      }}
                      onDragEnd={() => setDraggedId(null)}
                      onClick={() => onOpenRequirement(item.id)}
                    >
                      <div className="kanban-card__meta"><span className="mono-id">{item.id}</span><GripVertical size={14} /></div>
                      <h3>{item.title}</h3>
                      <div className="kanban-card__labels"><span className={`priority-badge priority-badge--${item.priority}`}><i />{priorityLabel[item.priority]}</span><span className="module-label">{item.module}</span></div>
                      <footer>{user ? <span><span className="avatar avatar--tiny" style={{ '--avatar-color': user.color } as React.CSSProperties}>{user.initials}</span>{assigneeNames(item, users)}</span> : <span><UserRound size={13} />未分配</span>}<span><MessageSquare size={13} />{item.comments.length}</span><time>{formatDate(item.dueDate)}</time></footer>
                    </article>
                  )
                })}
                {!cards.length && <div className="kanban-empty">拖动需求到这里</div>}
              </div>
            </section>
          )
        })}
      </div>
      {toast && <div className="toast"><Check size={15} />{toast}</div>}
    </div>
  )
}
