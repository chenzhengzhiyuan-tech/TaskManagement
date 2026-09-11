import { assigneeIds } from '../assignees'
import { ArrowRight, CalendarClock, CheckCircle2, CircleDashed, Clock3, Flame, TrendingUp } from 'lucide-react'
import { useMemo } from 'react'
import { useAppStore } from '../store'
import { dashboardDate, formatDate, getCompletion, getUser, greeting, isOverdue, priorityLabel, priorityRank } from '../utils'
import { useClock } from '../useClock'

interface DashboardPageProps {
  onOpenRequirement: (id: string) => void
  onNavigate: (target: 'requirements' | 'board') => void
}

export function DashboardPage({ onOpenRequirement, onNavigate }: DashboardPageProps) {
  const { requirements, statuses, iterations, currentUser, users } = useAppStore()
  const now = useClock()
  const activeIteration = iterations.find((item) => item.state === 'active')
  const iterationRequirements = requirements.filter((item) => item.iterationId === activeIteration?.id)
  const myRequirements = requirements.filter((item) => assigneeIds(item).includes(currentUser.id) && !statuses.find((status) => status.id === item.statusId)?.terminal)
  const myReviews = requirements.filter(item => item.reviewerId === currentUser.id && item.statusId === 'review')
  const overdue = requirements.filter((item) => isOverdue(item, statuses, now))
  const completion = getCompletion(iterationRequirements, statuses)
  const recent = useMemo(
    () => requirements.flatMap((item) => item.history.map((entry) => ({ ...entry, requirement: item }))).sort((a, b) => b.createdAt.localeCompare(a.createdAt)).slice(0, 5),
    [requirements],
  )

  return (
    <div className="page dashboard-page">
      <header className="page-header dashboard-header">
        <div><span className="eyebrow">{dashboardDate(now)}</span><h1>{greeting(now)}，{currentUser.name}</h1><p>优先处理当前迭代中的高风险和临期事项。</p></div>
        <button className="button button--ghost" type="button" onClick={() => onNavigate('board')}>打开迭代看板<ArrowRight size={16} /></button>
      </header>

      <section className="metric-grid">
        <article className="metric-card metric-card--hero">
          <div className="metric-card__top"><span>当前迭代完成率</span><TrendingUp size={18} /></div>
          <strong>{completion}<small>%</small></strong>
          <div className="metric-progress"><span style={{ width: `${completion}%` }} /></div>
          <footer><span>{activeIteration?.name}</span><span>{iterationRequirements.length} 条需求</span></footer>
        </article>
        <article className="metric-card"><div className="metric-card__icon metric-card__icon--blue"><CircleDashed size={20} /></div><span>我的待办</span><strong>{myRequirements.length}</strong><small>需要跟进的需求</small></article>
        <article className="metric-card"><div className="metric-card__icon metric-card__icon--orange"><CalendarClock size={20} /></div><span>本周待验收</span><strong>{requirements.filter((item) => item.statusId === 'review').length}</strong><small>等待管理员确认</small></article>
        <article className="metric-card"><div className="metric-card__icon metric-card__icon--red"><Flame size={20} /></div><span>超期风险</span><strong>{overdue.length}</strong><small>需要立即处理</small></article>
      </section>

      <div className="dashboard-columns">
        <section className="panel">
          <header className="panel__header"><div><h2>我的需求</h2><p>按优先级和截止时间排序</p></div><button type="button" onClick={() => onNavigate('requirements')}>查看全部<ArrowRight size={14} /></button></header>
          {[{ label: '待我处理', items: myRequirements }, { label: '待我验收', items: myReviews }].map(group => <section key={group.label} aria-label={group.label}>
          <h3 style={{ padding: '12px 18px', margin: 0 }}>{group.label}（{group.items.length}）</h3>
          <div className="focus-list" style={{ maxHeight: 400, overflowY: 'auto' }}>
            {group.items.length ? [...group.items].sort((a, b) => priorityRank[b.priority] - priorityRank[a.priority] || (a.dueDate ?? '9999').localeCompare(b.dueDate ?? '9999')).map((item) => {
              const status = statuses.find((entry) => entry.id === item.statusId)
              return (
                <button type="button" key={item.id} onClick={() => onOpenRequirement(item.id)}>
                  <span className={`priority-line priority-line--${item.priority}`} />
                  <div><span className="mono-id">{item.id}</span><strong>{item.title}</strong><small>{item.module} · {priorityLabel[item.priority]}优先级</small></div>
                  <div className="focus-list__tail"><span className="status-chip" style={{ '--status-color': status?.color } as React.CSSProperties}><i />{status?.name}</span><time>{formatDate(item.dueDate)}</time></div>
                </button>
              )
            }) : <div className="empty-state"><CheckCircle2 size={30} /><strong>暂无{group.label}需求</strong></div>}
          </div>
          </section>)}
        </section>

        <section className="panel">
          <header className="panel__header"><div><h2>最近动态</h2><p>团队关键操作</p></div></header>
          <div className="activity-list">
            {recent.map((item) => {
              const user = getUser(users, item.actorId)
              return (
                <button type="button" key={item.id} onClick={() => onOpenRequirement(item.requirement.id)}>
                  <span className="avatar avatar--small" style={{ '--avatar-color': user?.color } as React.CSSProperties}>{user?.initials}</span>
                  <div><p><strong>{user?.name}</strong> {item.detail}</p><span>{item.requirement.id} · {item.requirement.title}</span></div>
                  <time><Clock3 size={12} />{formatDate(item.createdAt, true)}</time>
                </button>
              )
            })}
          </div>
        </section>
      </div>
    </div>
  )
}
