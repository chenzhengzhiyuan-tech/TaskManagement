import {
  BadgeCheck, BellRing, Check, Database, HardDrive, KeyRound, Link2, Plus,
  Server, Settings2, ShieldCheck, Tag, Trash2, Unlink, UserPlus, Users, Workflow, X,
} from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { useAppStore, type WeComUserProfile } from '../store'
import type { Priority, RequirementDefaults, Role, User, WorkCalendarDay } from '../types'
import { priorityLabel } from '../utils'
import { EditableSettingName } from '../components/EditableSettingName'
import { ConfirmDialog } from '../components/ConfirmDialog'

type Category = 'account' | 'project' | 'wecom'
type ProjectSection = 'statuses' | 'modules' | 'types' | 'defaults' | 'calendar' | 'branding' | 'system'
const categories = [
  { id: 'account', label: '账号', icon: Users },
  { id: 'project', label: '项目设置', icon: Settings2 },
  { id: 'wecom', label: '企微设置', icon: BellRing },
] as const
const projectSections = [
  { id: 'statuses', label: '状态' }, { id: 'modules', label: '归属模块' }, { id: 'types', label: '需求单类型' },
  { id: 'defaults', label: '需求默认值' }, { id: 'calendar', label: '工作日历' }, { id: 'branding', label: '品牌设置' }, { id: 'system', label: '系统信息' },
] as const

export function SettingsPage() {
  const store = useAppStore()
  const { users, statuses, modules, requirementTypes, requirementDefaults, iterations, branding, currentUser, mode } = store
  const [category, setCategory] = useState<Category>('account')
  const [projectSection, setProjectSection] = useState<ProjectSection>('statuses')
  const [draggedStatus, setDraggedStatus] = useState<string | null>(null)
  const [sorting, setSorting] = useState(false)
  const [message, setMessage] = useState('')
  const [newStatus, setNewStatus] = useState(''); const [statusColor, setStatusColor] = useState('#0a84ff'); const [newModule, setNewModule] = useState(''); const [newType, setNewType] = useState('')
  const [member, setMember] = useState({ account: '', name: '', password: '', role: 'developer' as Role, weComEmail: '' })
  const [verifiedWeCom, setVerifiedWeCom] = useState<WeComUserProfile | null>(null)
  const [validatingWeCom, setValidatingWeCom] = useState(false)
  const [weComBindingTarget, setWeComBindingTarget] = useState<User | null>(null)
  const [weComBindingEmail, setWeComBindingEmail] = useState('')
  const [verifiedBinding, setVerifiedBinding] = useState<WeComUserProfile | null>(null)
  const [validatingBinding, setValidatingBinding] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<User | null>(null)
  const [brandName, setBrandName] = useState(branding.projectName); const backgroundRef = useRef<HTMLInputElement>(null)
  const [calendar, setCalendar] = useState<WorkCalendarDay[]>([]); const [calendarDraft, setCalendarDraft] = useState({ date: '2026-01-01', isWorkday: false, note: '' })
  const [defaults, setDefaults] = useState<RequirementDefaults>(requirementDefaults)
  const activeUsers = users.filter((user) => user.active !== false)
  const { getWorkCalendar } = store
  const notify = (text: string) => { setMessage(text); window.setTimeout(() => setMessage(''), 2200) }
  const result = async (promise: Promise<{ ok: boolean; reason?: string }>, success: string) => { const value = await promise; notify(value.ok ? success : value.reason ?? '操作失败'); return value.ok }
  useEffect(() => setDefaults(requirementDefaults), [requirementDefaults])
  useEffect(() => setBrandName(branding.projectName), [branding.projectName])
  useEffect(() => { if (category === 'project' && projectSection === 'calendar') void getWorkCalendar(2026).then(setCalendar) }, [category, projectSection, getWorkCalendar])
  if (currentUser.role !== 'admin') return null

  const saveDefaults = () => result(store.updateRequirementDefaults(defaults), '需求默认值已保存')
  const saveCalendar = async () => { if (await result(store.updateWorkCalendar(calendarDraft.date, calendarDraft.isWorkday, calendarDraft.note), '工作日历已更新')) setCalendar(await store.getWorkCalendar(2026)) }
  const confirmDelete = async () => { if (!deleteTarget) return; const ok = await result(store.deleteUser(deleteTarget.id), '账号已删除'); if (ok) setDeleteTarget(null) }
  const validateMemberEmail = async () => { setValidatingWeCom(true); const value = await store.validateWeComEmail(member.weComEmail); setValidatingWeCom(false); if (!value.ok || !value.profile) { setVerifiedWeCom(null); notify(value.reason ?? '企微邮箱验证失败'); return } setVerifiedWeCom(value.profile); notify(value.profile.active ? `企微验证通过：${value.profile.name}` : '该企微成员尚未激活') }
  const createMember = async () => {
    const weComEmail = verifiedWeCom?.active ? member.weComEmail.trim() : null
    const success = weComEmail ? '成员已创建并绑定企微' : '成员已创建，可稍后绑定企微'
    if (await result(store.createUser({ ...member, weComEmail }), success)) {
      setMember({ account: '', name: '', password: '', role: 'developer', weComEmail: '' })
      setVerifiedWeCom(null)
    }
  }
  const openWeComBinding = (user: User) => { setWeComBindingTarget(user); setWeComBindingEmail(''); setVerifiedBinding(null) }
  const closeWeComBinding = () => { if (validatingBinding) return; setWeComBindingTarget(null); setWeComBindingEmail(''); setVerifiedBinding(null) }
  const validateBindingEmail = async () => { setValidatingBinding(true); const value = await store.validateWeComEmail(weComBindingEmail); setValidatingBinding(false); if (!value.ok || !value.profile) { setVerifiedBinding(null); notify(value.reason ?? '企微邮箱验证失败'); return } setVerifiedBinding(value.profile); notify(value.profile.active ? `企微验证通过：${value.profile.name}` : '该企微成员尚未激活') }
  const confirmWeComBinding = async () => {
    if (!weComBindingTarget || !verifiedBinding?.active) return
    if (await result(store.bindWeComUser(weComBindingTarget.id, weComBindingEmail.trim()), '企微成员已绑定')) closeWeComBinding()
  }

  return <div className="page settings-page">
    <header className="page-header"><div><span className="eyebrow">SYSTEM SETTINGS</span><h1>系统设置</h1><p>账号、项目配置和企微配置均为系统级。</p></div><span className="admin-badge"><ShieldCheck size={15} />管理员</span></header>
    <div className="settings-layout">
      <aside className="settings-nav">{categories.map((item) => { const Icon = item.icon; return <button key={item.id} type="button" className={category === item.id ? 'active' : ''} onClick={() => setCategory(item.id)}><Icon size={17} />{item.label}</button> })}</aside>
      <section className="settings-content">
        {category === 'account' && renderAccountSection()}
        {category === 'project' && <><div className="settings-subnav">{projectSections.map((item) => <button key={item.id} type="button" className={projectSection === item.id ? 'active' : ''} onClick={() => setProjectSection(item.id)}>{item.label}</button>)}</div><div className="settings-subcontent">{renderProjectSection()}</div></>}
        {category === 'wecom' && <WeComSection />}
      </section>
    </div>
    {message && <div className="toast"><Check size={15} />{message}</div>}
    <ConfirmDialog open={Boolean(deleteTarget)} title={`删除账号“${deleteTarget?.name ?? ''}”？`} description="确认后该账号将立即无法登录，未完成需求的处理人会被清空；历史操作记录仍保留原成员信息。" confirmLabel="确认删除账号" danger onClose={() => setDeleteTarget(null)} onConfirm={() => void confirmDelete()} />
    {weComBindingTarget && <div className="modal-layer" role="presentation" onMouseDown={(event) => event.target === event.currentTarget && closeWeComBinding()}>
      <section className="create-modal wecom-bind-modal" role="dialog" aria-modal="true" aria-labelledby="wecom-bind-title">
        <header><div><span className="eyebrow">WECOM IDENTITY</span><h2 id="wecom-bind-title">绑定企微成员</h2></div><button className="icon-button" type="button" aria-label="关闭企微绑定" onClick={closeWeComBinding}><X size={17} /></button></header>
        <div className="create-modal__body"><p className="wecom-bind-copy">平台账号 <strong>{weComBindingTarget.account}</strong> 保持不变。输入该成员的企业邮箱，系统将从企业微信获取并保存真实 userid。</p><label className="field"><span>企业邮箱 <em>*</em></span><div className="account-verify-control"><input type="email" value={weComBindingEmail} onChange={(event) => { setWeComBindingEmail(event.target.value); setVerifiedBinding(null) }} placeholder="name@company.com" autoFocus /><button className="button button--ghost button--compact" type="button" disabled={!weComBindingEmail.trim() || validatingBinding} onClick={() => void validateBindingEmail()}>{validatingBinding ? '验证中…' : '验证邮箱'}</button></div>{verifiedBinding && <small className={verifiedBinding.active ? 'binding-ok' : 'binding-missing'}><BadgeCheck size={13} />{verifiedBinding.name} · userid {verifiedBinding.userId}</small>}</label></div>
        <footer><span>同一个企微成员只能绑定一个平台账号。</span><div><button className="button button--ghost" type="button" onClick={closeWeComBinding}>取消</button><button className="button button--primary" type="button" disabled={!verifiedBinding?.active} onClick={() => void confirmWeComBinding()}>确认绑定</button></div></footer>
      </section>
    </div>}
  </div>

  function renderAccountSection() {
    return <><div className="settings-heading"><div><h2>账号</h2><p>成员用于登录和系统显示；账号是平台唯一标识，企微身份通过企业邮箱独立绑定。</p></div></div>
      <div className="settings-form-card"><h3>新增成员</h3><div className="form-grid">
        <label className="field"><span>成员 <em>*</em></span><input value={member.name} onChange={(e) => setMember({ ...member, name: e.target.value })} placeholder="登录昵称 / 系统显示名称" /></label>
        <label className="field"><span>账号 <em>*</em></span><input value={member.account} onChange={(e) => setMember({ ...member, account: e.target.value })} placeholder="平台唯一账号" /><small>账号不会被企微 userid 覆盖。</small></label>
        <label className="field"><span>企微企业邮箱（可选）</span><div className="account-verify-control"><input type="email" value={member.weComEmail} onChange={(e) => { setMember({ ...member, weComEmail: e.target.value }); setVerifiedWeCom(null) }} placeholder="name@company.com" /><button className="button button--ghost button--compact" type="button" disabled={!member.weComEmail.trim() || validatingWeCom} onClick={() => void validateMemberEmail()}>{validatingWeCom ? '验证中…' : '验证企微'}</button></div>{verifiedWeCom ? <small className={verifiedWeCom.active ? 'binding-ok' : 'binding-missing'}><BadgeCheck size={13} />{verifiedWeCom.name} · userid {verifiedWeCom.userId}</small> : <small>不填写或不验证也可以新增，之后再绑定。</small>}</label>
        <label className="field"><span>初始密码 <em>*</em></span><input type="password" value={member.password} onChange={(e) => setMember({ ...member, password: e.target.value })} /></label>
        <label className="field"><span>角色</span><select value={member.role} onChange={(e) => setMember({ ...member, role: e.target.value as Role })}><option value="developer">开发人员</option><option value="admin">管理员</option></select></label>
      </div><div className="settings-action-row"><button className="button button--primary button--compact" disabled={!member.account.trim() || !member.name.trim() || !member.password} onClick={() => void createMember()}><UserPlus size={14} />新增成员</button></div></div>
      <div className="settings-table users-settings-table"><div className="settings-table__head"><span>成员</span><span>账号</span><span>角色</span><span>企微状态</span><span>操作</span></div>{activeUsers.map((user) => <div className="settings-table__row" key={user.id}><span className="settings-user"><span className="avatar avatar--small" style={{ '--avatar-color': user.color } as React.CSSProperties}>{user.initials}</span><strong>{user.name}</strong></span><span>{user.account}</span><span><select value={user.role} disabled={user.id === 'u-admin'} onChange={(e) => void store.updateUserRole(user.id, e.target.value as Role).then(() => notify('角色已更新'))}><option value="admin">管理员</option><option value="developer">开发人员</option></select></span><span className={user.wecomBound ? 'binding-ok' : 'binding-missing'}>{user.wecomBound ? '已绑定' : '未绑定'}</span><span className="member-actions"><button className="icon-button icon-button--small" title={user.wecomBound ? '更换企微绑定' : '绑定企微成员'} onClick={() => openWeComBinding(user)}><Link2 size={14} /></button>{user.wecomBound && <button className="icon-button icon-button--small" title="解除企微绑定" onClick={() => void result(store.unbindWeComUser(user.id), '企微绑定已解除')}><Unlink size={14} /></button>}<button className="icon-button icon-button--small" title="重置密码" onClick={() => { const password = window.prompt(`请输入 ${user.name} 的新密码`); if (password) void result(store.updateUser(user.id, { password }), '密码已重置') }}><KeyRound size={14} /></button><button className="button button--danger button--compact member-delete-button" type="button" disabled={user.id === 'u-admin'} onClick={() => setDeleteTarget(user)}><Trash2 size={13} />删除账号</button></span></div>)}</div>
    </>
  }

  function renderProjectSection() {
    if (projectSection === 'statuses') return <><div className="settings-heading"><div><h2>状态</h2><p>拖拽手柄调整看板列顺序；系统保护仅限制普通成员切换到该状态。</p></div></div><div className="status-settings-list">{statuses.map((status, index) => <div key={status.id} onDragOver={event => event.preventDefault()} onDrop={event => { event.preventDefault(); if (!draggedStatus || draggedStatus === status.id || sorting) return; const ids = statuses.map(x => x.id); ids.splice(ids.indexOf(draggedStatus), 1); ids.splice(statuses.findIndex(x => x.id === status.id), 0, draggedStatus); setDraggedStatus(null); setSorting(true); void result(store.reorderStatuses(ids), '状态顺序已保存').finally(() => setSorting(false)) }}><span className="drag-handle" draggable={!sorting} title="拖拽调整状态顺序" onDragStart={event => { setDraggedStatus(status.id); event.dataTransfer.setData('text/plain', status.id); event.dataTransfer.effectAllowed = 'move' }} onDragEnd={() => setDraggedStatus(null)}>⠿</span><span className="status-color" style={{ background: status.color }} /><EditableSettingName name={status.name} onSave={name => result(store.renameStatus(status.id, name), '状态已更新')} /><small>第 {index + 1} 列</small><label className="status-protection"><input type="checkbox" aria-label={`${status.name}系统保护`} checked={Boolean(status.protected)} onChange={event => void result(store.setStatusProtection(status.id, event.target.checked), '系统保护已更新')} />系统保护</label><button className="icon-button icon-button--small" aria-label={`删除${status.name}`} onClick={() => void result(store.removeStatus(status.id), '状态已删除')}><Trash2 size={14} /></button></div>)}</div><div className="inline-create settings-spaced"><input value={newStatus} onChange={(e) => setNewStatus(e.target.value)} placeholder="新状态名称" /><input className="color-input" type="color" value={statusColor} onChange={(e) => setStatusColor(e.target.value)} /><button className="button button--primary button--compact" disabled={!newStatus.trim()} onClick={() => void result(store.addStatus(newStatus, statusColor), '状态已添加').then((ok) => ok && setNewStatus(''))}><Plus size={14} />添加状态</button></div></>
    if (projectSection === 'modules') return <><div className="settings-heading"><div><h2>归属模块</h2><p>使用中的模块不能删除。</p></div></div><div className="module-settings-list">{modules.map((module) => <div key={module}><span className="module-settings-list__mark">{module.slice(0, 1)}</span><EditableSettingName name={module} onSave={name => result(store.renameModule(module, name), '模块已更新')} /><button className="icon-button icon-button--small" onClick={() => void result(store.removeModule(module), '模块已删除')}><Trash2 size={14} /></button></div>)}</div><div className="inline-create settings-spaced"><input value={newModule} onChange={(e) => setNewModule(e.target.value)} placeholder="新模块名称" /><button className="button button--primary button--compact" disabled={!newModule.trim()} onClick={() => void result(store.addModule(newModule), '模块已添加').then((ok) => ok && setNewModule(''))}><Plus size={14} />添加模块</button></div></>
    if (projectSection === 'types') return <><div className="settings-heading"><div><h2>需求单类型</h2><p>系统自动为新增类型分配不同颜色；停用不会删除已有需求关联。</p></div></div><div className="status-settings-list">{requirementTypes.map((item) => <div key={item.id}><span className="status-color" style={{ background: item.color }} /><Tag size={15} /><EditableSettingName name={item.name} onSave={name => result(store.updateRequirementType(item.id, { name }), '类型已更新')} /><small style={{ color: item.color }}>{item.enabled ? '启用' : '停用'}</small><button className="button button--ghost button--compact" onClick={() => void result(store.updateRequirementType(item.id, { enabled: !item.enabled }), item.enabled ? '类型已停用' : '类型已启用')}>{item.enabled ? '停用' : '启用'}</button></div>)}</div><div className="inline-create settings-spaced"><input value={newType} onChange={(e) => setNewType(e.target.value)} placeholder="新需求单类型" /><button className="button button--primary button--compact" disabled={!newType.trim()} onClick={() => void result(store.addRequirementType(newType), '类型已添加').then((ok) => ok && setNewType(''))}><Plus size={14} />添加类型</button></div></>
    if (projectSection === 'defaults') return <><div className="settings-heading"><div><h2>需求默认值</h2><p>新建需求时自动带出，用户仍可修改。</p></div></div><div className="settings-form-card"><div className="form-grid"><label className="field"><span>需求单类型</span><select value={defaults.requirementTypeId ?? ''} onChange={(e) => setDefaults({ ...defaults, requirementTypeId: e.target.value || null })}><option value="">无默认值</option>{requirementTypes.filter((x) => x.enabled).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label className="field"><span>模块</span><select value={defaults.module ?? ''} onChange={(e) => setDefaults({ ...defaults, module: e.target.value || null })}>{modules.map((x) => <option key={x}>{x}</option>)}</select></label><label className="field"><span>优先级</span><select value={defaults.priority} onChange={(e) => setDefaults({ ...defaults, priority: e.target.value as Priority })}>{(Object.keys(priorityLabel) as Priority[]).map((x) => <option key={x} value={x}>{priorityLabel[x]}</option>)}</select></label><label className="field"><span>状态</span><select value={defaults.statusId ?? ''} onChange={(e) => setDefaults({ ...defaults, statusId: e.target.value || null })}>{statuses.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label className="field"><span>处理人</span><select value={defaults.assigneeId ?? ''} onChange={(e) => setDefaults({ ...defaults, assigneeId: e.target.value || null })}><option value="">无默认值</option>{activeUsers.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label className="field"><span>验收人</span><select value={defaults.reviewerId ?? ''} onChange={(e) => setDefaults({ ...defaults, reviewerId: e.target.value || null })}><option value="">无默认值</option>{activeUsers.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label className="field"><span>迭代</span><select value={defaults.iterationMode} onChange={(e) => setDefaults({ ...defaults, iterationMode: e.target.value as typeof defaults.iterationMode })}><option value="current">当前迭代</option><option value="none">需求池</option><option value="specific">指定迭代</option></select></label>{defaults.iterationMode === 'specific' && <label className="field"><span>指定迭代</span><select value={defaults.iterationId ?? ''} onChange={(e) => setDefaults({ ...defaults, iterationId: e.target.value || null })}>{iterations.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>}<label className="field"><span>截止日期偏移天数</span><input type="number" min="0" max="365" value={defaults.dueDateOffsetDays ?? ''} onChange={(e) => setDefaults({ ...defaults, dueDateOffsetDays: e.target.value === '' ? null : Number(e.target.value) })} /></label><label className="field field--wide"><span>描述模板</span><textarea value={defaults.descriptionTemplate} onChange={(e) => setDefaults({ ...defaults, descriptionTemplate: e.target.value })} /></label></div><div className="settings-action-row"><button className="button button--primary" onClick={() => void saveDefaults()}>保存默认值</button></div></div></>
    if (projectSection === 'calendar') return <><div className="settings-heading"><div><h2>2026工作日历</h2><p>内置法定节假日和调休，管理员可修正。</p></div></div><div className="settings-form-card"><div className="form-grid"><label className="field"><span>日期</span><input type="date" value={calendarDraft.date} onChange={(e) => setCalendarDraft({ ...calendarDraft, date: e.target.value })} /></label><label className="field"><span>类型</span><select value={calendarDraft.isWorkday ? 'work' : 'rest'} onChange={(e) => setCalendarDraft({ ...calendarDraft, isWorkday: e.target.value === 'work' })}><option value="rest">休息日</option><option value="work">工作日/调休上班</option></select></label><label className="field field--wide"><span>说明</span><input value={calendarDraft.note} onChange={(e) => setCalendarDraft({ ...calendarDraft, note: e.target.value })} /></label></div><div className="settings-action-row"><button className="button button--primary button--compact" onClick={() => void saveCalendar()}>保存日期</button></div></div><div className="calendar-list">{calendar.map((day) => <div key={day.date}><strong>{day.date}</strong><span className={day.isWorkday ? 'binding-ok' : 'binding-missing'}>{day.isWorkday ? '工作日' : '休息日'}</span><span>{day.note}</span></div>)}</div></>
    if (projectSection === 'branding') return <><div className="settings-heading"><div><h2>品牌设置</h2><p>项目名称和登录背景图对所有用户生效。</p></div></div><div className="settings-form-card"><div className="form-grid"><label className="field field--wide"><span>项目名称</span><input value={brandName} onChange={(e) => setBrandName(e.target.value)} /></label><label className="field field--wide"><span>登录背景图</span><input ref={backgroundRef} type="file" accept="image/png,image/jpeg,image/webp" onChange={(e) => { const file = e.target.files?.[0]; if (file) void result(store.uploadBrandingBackground(file), '背景图已更新') }} /></label></div><img className="branding-preview" src={branding.loginBackgroundUrl} alt="登录背景预览" /><div className="settings-action-row"><button className="button button--primary" disabled={!brandName.trim()} onClick={() => void result(store.updateBranding(brandName), '项目名称已更新')}>保存品牌设置</button></div></div></>
    return <><div className="settings-heading"><div><h2>系统信息</h2><p>运行、数据、附件与后台任务概览。</p></div></div><div className="system-grid"><article><Server size={20} /><span>运行模式</span><strong>{mode === 'api' ? 'API服务端' : '浏览器Mock'}</strong></article><article><Database size={20} /><span>数据来源</span><strong>{mode === 'api' ? 'PostgreSQL / SQLite API' : 'LocalStorage'}</strong></article><article><HardDrive size={20} /><span>附件上限</span><strong>500MB / 图片</strong></article><article><Workflow size={20} /><span>后台任务</span><strong>迭代 / 日历 / 任务锁</strong></article></div></>
  }

  function WeComSection() {
    return <><div className="settings-heading"><div><h2>企微设置</h2><p>使用企业微信自建应用向成员本人发送定时事项汇总。</p></div><span className="connection-badge"><span />自建应用消息</span></div><div className="settings-form-card"><div className="wecom-summary"><BellRing size={26} /><div><strong>每日未完成事项汇总</strong><span>配置保存在服务端环境变量中，本页面不会展示 CorpSecret。平台账号与企微身份相互独立，通过企业邮箱查询并保存真实 userid。</span></div></div><div className="notification-rules"><div><span>发送时间</span><strong>中国工作日 20:00</strong></div><div><span>发送范围</span><strong>每位成员的全部未完成事项</strong></div><div><span>重点标记</span><strong>明日到期 / 已超期</strong></div></div><div className="wecom-link-note"><Link2 size={16} /><span>每条消息附带需求详情链接；当前访问地址为 <code>{window.location.origin}</code>。</span></div></div></>
  }
}

