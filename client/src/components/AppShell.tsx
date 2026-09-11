import {
  BarChart3,
  ChevronDown,
  ChevronsLeft,
  ChevronsRight,
  ClipboardList,
  Columns3,
  Gauge,
  IterationCcw,
  LogOut,
  Plus,
  Search,
  Settings,
  Sun,
  Moon,
} from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { applyTheme, getTheme, type Theme } from '../theme'
import type { PageKey } from '../types'
import { useAppStore } from '../store'

const navItems: Array<{ key: PageKey; label: string; icon: typeof Gauge; adminOnly?: boolean }> = [
  { key: 'dashboard', label: '工作台', icon: Gauge },
  { key: 'requirements', label: '需求', icon: ClipboardList },
  { key: 'board', label: '看板', icon: Columns3 },
  { key: 'iterations', label: '迭代', icon: IterationCcw },
  { key: 'reports', label: '报表', icon: BarChart3 },
  { key: 'settings', label: '系统设置', icon: Settings, adminOnly: true },
]

interface AppShellProps {
  page: PageKey
  onPageChange: (page: PageKey) => void
  onNewRequirement: () => void
  onGlobalSearch: (query: string) => void
  children: React.ReactNode
}

export function AppShell({ page, onPageChange, onNewRequirement, onGlobalSearch, children }: AppShellProps) {
  const { users, currentUser, mode, branding, setCurrentUser, logout } = useAppStore()
  const [collapsed, setCollapsed] = useState(false)
  const [profileOpen, setProfileOpen] = useState(false)
  const [globalQuery, setGlobalQuery] = useState('')
  const [theme, setTheme] = useState<Theme>(getTheme)
  const toggleTheme = () => { const next = theme === 'dark' ? 'light' : 'dark'; setTheme(next); applyTheme(next) }
  const searchRef = useRef<HTMLInputElement>(null)
  const profileRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!profileOpen) return
    const closeOutside = (event: PointerEvent) => {
      if (event.target instanceof Node && !profileRef.current?.contains(event.target)) {
        setProfileOpen(false)
      }
    }
    document.addEventListener('pointerdown', closeOutside, true)
    return () => document.removeEventListener('pointerdown', closeOutside, true)
  }, [profileOpen])

  useEffect(() => {
    const handleKeydown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement
      const isTyping = ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName) || target.isContentEditable
      if (event.key === '/' && !isTyping) {
        event.preventDefault()
        searchRef.current?.focus()
      }
      if (event.key.toLowerCase() === 'n' && !isTyping) {
        event.preventDefault()
        onNewRequirement()
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault()
        searchRef.current?.focus()
      }
    }
    window.addEventListener('keydown', handleKeydown)
    return () => window.removeEventListener('keydown', handleKeydown)
  }, [onNewRequirement])

  const submitSearch = (event: React.FormEvent) => {
    event.preventDefault()
    onGlobalSearch(globalQuery)
    onPageChange('requirements')
  }

  return (
    <div className={`app-shell ${collapsed ? 'app-shell--collapsed' : ''}`}>
      <aside className="sidebar">
        <div className="brand">
          <div className="brand__mark">G</div>
          {!collapsed && (
            <div>
              <strong>{branding.projectName}</strong>
              <span>需求协作平台</span>
            </div>
          )}
        </div>
        <nav className="sidebar__nav" aria-label="主导航">
          {navItems
            .filter((item) => !item.adminOnly || currentUser.role === 'admin')
            .map((item) => {
              const Icon = item.icon
              return (
                <button
                  className={`nav-item ${page === item.key ? 'nav-item--active' : ''}`}
                  key={item.key}
                  onClick={() => onPageChange(item.key)}
                  title={collapsed ? item.label : undefined}
                  type="button"
                >
                  <Icon size={18} strokeWidth={1.8} />
                  {!collapsed && <span>{item.label}</span>}
                </button>
              )
            })}
        </nav>
        <button
          className="sidebar__collapse"
          type="button"
          onClick={() => setCollapsed((value) => !value)}
          aria-label={collapsed ? '展开导航' : '收起导航'}
        >
          {collapsed ? <ChevronsRight size={16} /> : <ChevronsLeft size={16} />}
          {!collapsed && <span>收起导航</span>}
        </button>
      </aside>

      <div className="app-main">
        <header className="topbar">
          <form className="global-search" onSubmit={submitSearch}>
            <Search size={16} />
            <input
              ref={searchRef}
              value={globalQuery}
              onChange={(event) => setGlobalQuery(event.target.value)}
              placeholder="搜索需求编号、标题或模块"
              aria-label="全局搜索"
            />
            <kbd>⌘K</kbd>
          </form>
          <div className="topbar__actions"><button className="icon-button" type="button" aria-label="切换明暗主题" title="切换明暗主题" onClick={toggleTheme}>{theme === 'dark' ? <Sun size={17}/> : <Moon size={17}/>}</button>
            <button className="button button--primary button--compact" onClick={onNewRequirement} type="button">
              <Plus size={16} />
              新建需求
            </button>
            <div className="popover-wrap" ref={profileRef}>
              <button
                className="profile-button"
                aria-expanded={profileOpen}
                type="button"
                onClick={() => {
                  setProfileOpen((value) => !value)
                }}
              >
                <span className="avatar" style={{ '--avatar-color': currentUser.color } as React.CSSProperties}>{currentUser.initials}</span>
                <span className="profile-button__text"><strong>{currentUser.name}</strong><small>{currentUser.role === 'admin' ? '管理员' : '开发人员'}</small></span>
                <ChevronDown size={14} />
              </button>
              {profileOpen && (
                <div className="popover profile-popover">
                  <div className="profile-popover__title">{mode === 'mock' ? '切换测试身份' : '当前登录账号'}</div>
                  {mode === 'mock' && users.filter((user) => user.active !== false).map((user) => (
                    <button
                      type="button"
                      key={user.id}
                      className={`profile-option ${user.id === currentUser.id ? 'profile-option--active' : ''}`}
                      onClick={() => {
                        setCurrentUser(user.id)
                        setProfileOpen(false)
                        if (user.role !== 'admin' && page === 'settings') onPageChange('dashboard')
                      }}
                    >
                      <span className="avatar avatar--small" style={{ '--avatar-color': user.color } as React.CSSProperties}>{user.initials}</span>
                      <span><strong>{user.name}</strong><small>{user.role === 'admin' ? '管理员' : '开发人员'}</small></span>
                    </button>
                  ))}
                  <div className="popover__divider" />
                  <button type="button" className="profile-option profile-option--danger" onClick={() => void logout()}><LogOut size={15} />退出登录</button>
                </div>
              )}
            </div>
          </div>
        </header>
        <main className="content">{children}</main>
      </div>
    </div>
  )
}
