import { assigneeNames } from '../assignees'
import { ArrowRight, CalendarDays, CheckCircle2, Circle, Clock3, Sparkles } from 'lucide-react'
import { useAppStore } from '../store'
import { formatDate, getCompletion, getUser, priorityLabel } from '../utils'

interface IterationsPageProps {
  onOpenRequirement: (id: string) => void
}

export function IterationsPage({ onOpenRequirement }: IterationsPageProps) {
  const { iterations, requirements, statuses, users } = useAppStore()
  const active = iterations.find((item) => item.state === 'active')
  const timeline = [...iterations].sort((a, b) => b.startDate.localeCompare(a.startDate))
  const next = timeline.filter((item) => item.state === 'upcoming').at(-1)
  const activeRequirements = requirements.filter((item) => item.iterationId === active?.id)
  const completion = getCompletion(activeRequirements, statuses)

  return (
    <div className="page iterations-page">
      <header className="page-header"><div><span className="eyebrow">ITERATIONS</span><h1>迭代</h1><p>每周五 23:59（北京时间）结束当前迭代，并立即开启下一周。</p></div><span className="automation-pill"><Sparkles size={14} />自动迭代已启用</span></header>
      {active && (
        <section className="iteration-hero">
          <div className="iteration-hero__content"><span className="status-tag status-tag--active">进行中</span><h2>{active.name}</h2><p>{active.goal}</p><div className="iteration-dates"><CalendarDays size={15} />{active.startDate} — {active.endDate}<span>·</span><Clock3 size={15} />周五 23:59 自动结束</div></div>
          <div className="iteration-ring" style={{ '--progress': `${completion * 3.6}deg` } as React.CSSProperties}><div><strong>{completion}%</strong><span>完成率</span></div></div>
          <div className="iteration-summary"><div><strong>{activeRequirements.length}</strong><span>总需求</span></div><div><strong>{activeRequirements.filter((item) => statuses.find((status) => status.id === item.statusId)?.terminal).length}</strong><span>已完成</span></div><div><strong>{activeRequirements.filter((item) => item.statusId === 'review').length}</strong><span>待验收</span></div></div>
        </section>
      )}

      <div className="iterations-layout">
        <section className="panel">
          <header className="panel__header"><div><h2>当前迭代需求</h2><p>未完成事项将在结束后自动顺延</p></div></header>
          <div className="iteration-requirements">
            {activeRequirements.map((item) => {
              const status = statuses.find((entry) => entry.id === item.statusId)
              const user = getUser(users, item.assigneeId)
              return <button type="button" key={item.id} onClick={() => onOpenRequirement(item.id)}><span className="iteration-state-icon">{status?.terminal ? <CheckCircle2 size={18} /> : <Circle size={18} />}</span><div><span className="mono-id">{item.id}</span><strong>{item.title}</strong><small>{item.module} · {priorityLabel[item.priority]}</small></div><span className="status-chip" style={{ '--status-color': status?.color } as React.CSSProperties}><i />{status?.name}</span><span className="iteration-assignee">{user ? <><span className="avatar avatar--tiny" style={{ '--avatar-color': user.color } as React.CSSProperties}>{user.initials}</span>{assigneeNames(item, users)}</> : '未分配'}</span><time>{formatDate(item.dueDate)}</time><ArrowRight size={15} /></button>
            })}
          </div>
        </section>

        <aside className="iteration-side">
          <h2>迭代时间线</h2>
          {timeline.map((item) => (
            <article key={item.id} className={`iteration-timeline-card iteration-timeline-card--${item.state}`}>
              <div className="iteration-timeline-card__line"><span /></div><div><span>{item.state === 'active' ? '当前迭代' : item.state === 'completed' ? '已结束' : item.id === next?.id ? '下一迭代' : '待开始'}</span><strong>{item.name}</strong>{item.goal && <p>{item.goal}</p>}<small>{item.startDate} — {item.endDate}</small></div>
            </article>
          ))}
          <div className="info-note"><Sparkles size={16} /><p>除“已完成”和“已关闭”外，上一迭代的所有需求会自动顺延并记录历史。周末显示已开启的下一周；服务重启后自动补齐遗漏的切换。</p></div>
        </aside>
      </div>
    </div>
  )
}
