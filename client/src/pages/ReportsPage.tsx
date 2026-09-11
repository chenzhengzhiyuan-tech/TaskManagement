import { assigneeIds, assigneeNames } from '../assignees'
import { AlertTriangle, BarChart3, CheckCircle2, Clock3, UsersRound } from 'lucide-react'
import { useAppStore } from '../store'
import { chinaDateKey, getCompletion, isOverdue } from '../utils'
import { useClock } from '../useClock'

export function ReportsPage() {
  const { requirements, statuses, users, iterations } = useAppStore()
  const now = useClock()
  const active = iterations.find((item) => item.state === 'active')
  const current = requirements.filter((item) => item.iterationId === active?.id)
  const completion = getCompletion(current, statuses)
  const overdue = requirements.filter((item) => isOverdue(item, statuses, now))
  const load = users.filter((user) => user.active !== false).map((user) => ({
    user,
    count: requirements.filter((item) => assigneeIds(item).includes(user.id) && !statuses.find((status) => status.id === item.statusId)?.terminal).length,
  })).sort((a, b) => b.count - a.count)
  const maxLoad = Math.max(...load.map((item) => item.count), 1)
  const statusCounts = statuses.map((status) => ({ status, count: current.filter((item) => item.statusId === status.id).length })).filter((item) => item.count)

  return (
    <div className="page reports-page">
      <header className="page-header"><div><span className="eyebrow">REPORTS</span><h1>报表</h1><p>第一期按需求数量统计进度、负载和超期风险。</p></div></header>
      <section className="report-kpis">
        <article><span className="report-kpi-icon report-kpi-icon--green"><CheckCircle2 size={19} /></span><div><small>迭代完成率</small><strong>{completion}%</strong><p>{current.filter((item) => statuses.find((status) => status.id === item.statusId)?.terminal).length} / {current.length} 条已完成</p></div></article>
        <article><span className="report-kpi-icon report-kpi-icon--blue"><BarChart3 size={19} /></span><div><small>进行中</small><strong>{requirements.filter((item) => item.statusId === 'in_progress').length}</strong><p>当前正在处理</p></div></article>
        <article><span className="report-kpi-icon report-kpi-icon--orange"><Clock3 size={19} /></span><div><small>待验收</small><strong>{requirements.filter((item) => item.statusId === 'review').length}</strong><p>需要管理员确认</p></div></article>
        <article><span className="report-kpi-icon report-kpi-icon--red"><AlertTriangle size={19} /></span><div><small>超期需求</small><strong>{overdue.length}</strong><p>截至{chinaDateKey(now)}</p></div></article>
      </section>

      <div className="report-grid">
        <section className="panel chart-panel"><header className="panel__header"><div><h2>迭代状态分布</h2><p>{active?.name}</p></div></header><div className="status-chart"><div className="donut" style={{ '--completion': `${completion * 3.6}deg` } as React.CSSProperties}><div><strong>{current.length}</strong><span>需求总数</span></div></div><div className="chart-legend">{statusCounts.map((item) => <div key={item.status.id}><span style={{ background: item.status.color }} /><strong>{item.status.name}</strong><small>{item.count} 条</small></div>)}</div></div></section>
        <section className="panel chart-panel"><header className="panel__header"><div><h2>成员负载</h2><p>未完成需求数量</p></div><UsersRound size={18} /></header><div className="load-chart">{load.map(({ user, count }) => <div key={user.id}><span className="avatar avatar--small" style={{ '--avatar-color': user.color } as React.CSSProperties}>{user.initials}</span><strong>{user.name}</strong><div><span style={{ width: `${Math.max(5, count / maxLoad * 100)}%` }} /></div><b>{count}</b></div>)}</div></section>
      </div>

      <section className="panel risk-panel"><header className="panel__header"><div><h2>超期风险</h2><p>未完成且超过期望时间</p></div></header>{overdue.length ? <div className="risk-table">{overdue.map((item) => <div key={item.id}><span className="mono-id">{item.id}</span><strong>{item.title}</strong><span>{assigneeNames(item, users) || '未分配'}</span><time>{item.dueDate}</time></div>)}</div> : <div className="empty-state"><CheckCircle2 size={28} /><strong>没有超期需求</strong></div>}</section>
    </div>
  )
}

