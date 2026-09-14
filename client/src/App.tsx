import { useCallback, useEffect, useState } from 'react'
import './App.css'
import { AppShell } from './components/AppShell'
import { NewRequirementModal } from './components/NewRequirementModal'
import { RequirementDrawer } from './components/RequirementDrawer'
import { LoginPage } from './pages/LoginPage'
import { DashboardPage } from './pages/DashboardPage'
import { BoardPage } from './pages/BoardPage'
import { IterationsPage } from './pages/IterationsPage'
import { ReportsPage } from './pages/ReportsPage'
import { RequirementsPage } from './pages/RequirementsPage'
import { SettingsPage } from './pages/SettingsPage'
import { useAppStore } from './store'
import type { PageKey } from './types'
import { applyTheme, getTheme } from './theme'

function App() {
  const { authenticated, initializing, branding } = useAppStore()
  useEffect(() => applyTheme(getTheme()), [])
  useEffect(() => { document.title = `${branding.projectName} · 需求协作平台` }, [branding.projectName])
  const [page, setPage] = useState<PageKey>('dashboard')
  const [selectedRequirementId, setSelectedRequirementId] = useState<string | null>(null)
  const [createOpen, setCreateOpen] = useState(false)
  const [createdNotice, setCreatedNotice] = useState('')
  const [globalQuery, setGlobalQuery] = useState('')
  useEffect(() => {
    if (!createdNotice) return
    const timer = window.setTimeout(() => setCreatedNotice(''), 3000)
    return () => window.clearTimeout(timer)
  }, [createdNotice])

  const openRequirement = useCallback((id: string) => {
    setSelectedRequirementId(id)
    const url = new URL(window.location.href); url.searchParams.delete('comment'); url.searchParams.set('requirement', id); window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`)
  }, [])
  const closeRequirement = useCallback(() => {
    setSelectedRequirementId(null)
    const url = new URL(window.location.href); url.searchParams.delete('comment'); url.searchParams.delete('requirement'); window.history.replaceState({}, '', `${url.pathname}${url.search}${url.hash}`)
  }, [])
  useEffect(() => {
    if (!authenticated) return
    const requirementId = new URLSearchParams(window.location.search).get('requirement')
    if (requirementId) { setPage('requirements'); setSelectedRequirementId(requirementId) }
  }, [authenticated])

  const openNew = useCallback(() => setCreateOpen(true), [])
  if (initializing) return <div className="app-loading"><span className="brand__mark">G</span><strong>正在连接服务端…</strong><small>加载用户、需求和系统配置</small></div>
  if (!authenticated) return <LoginPage />

  const pageContent = (() => {
    switch (page) {
      case 'dashboard': return <DashboardPage onOpenRequirement={openRequirement} onNavigate={setPage} />
      case 'requirements': return <RequirementsPage initialQuery={globalQuery} onOpenRequirement={openRequirement} />
      case 'iterations': return <IterationsPage onOpenRequirement={openRequirement} />
      case 'board': return <BoardPage onOpenRequirement={openRequirement} />
      case 'reports': return <ReportsPage />
      case 'settings': return <SettingsPage />
      default: return null
    }
  })()

  return (
    <AppShell page={page} onPageChange={setPage} onNewRequirement={openNew} onGlobalSearch={setGlobalQuery}>
      {pageContent}
      <RequirementDrawer key={selectedRequirementId ?? 'closed'} requirementId={selectedRequirementId} onClose={closeRequirement} onOpenRequirement={openRequirement} />
      <NewRequirementModal open={createOpen} onClose={() => setCreateOpen(false)} onCreated={(id) => { setCreatedNotice(id); setCreateOpen(false); if (page !== 'board') { setPage('requirements'); setGlobalQuery('') } openRequirement(id) }} />
      {createdNotice && <div className="created-notice" role="status"><button type="button" onClick={() => openRequirement(createdNotice)}>创建成功：{createdNotice} · 点击查看</button><button type="button" aria-label="关闭创建提示" onClick={() => setCreatedNotice('')}>×</button></div>}
    </AppShell>
  )
}

export default App
