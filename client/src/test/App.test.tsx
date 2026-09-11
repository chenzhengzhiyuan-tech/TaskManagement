import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import App from '../App'
import { AppStoreProvider } from '../store'

function renderApp() {
  return render(<AppStoreProvider><App /></AppStoreProvider>)
}

async function loginAs(account = 'admin') {
  const user = userEvent.setup()
  await user.clear(screen.getByLabelText('成员'))
  await user.type(screen.getByLabelText('成员'), account)
  await user.clear(screen.getByLabelText('密码'))
  await user.type(screen.getByLabelText('密码'), 'demo123')
  await user.click(screen.getByRole('button', { name: /^登录/ }))
  return user
}

describe('客户端核心流程', () => {
  beforeEach(() => {
    localStorage.clear()
    sessionStorage.clear()
  })

  it('验证登录失败并允许管理员登录退出', async () => {
    renderApp()
    const user = userEvent.setup()
    expect(screen.getByLabelText('成员')).toHaveValue('')
    expect(screen.getByLabelText('密码')).toHaveValue('')
    expect(screen.queryByText(/统一密码/)).not.toBeInTheDocument()
    expect(screen.queryByText(/客户端测试成员/)).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^登录/ })).toBeDisabled()
    await user.type(screen.getByLabelText('成员'), 'admin')
    await user.clear(screen.getByLabelText('密码'))
    await user.type(screen.getByLabelText('密码'), 'wrong')
    await user.click(screen.getByRole('button', { name: /^登录/ }))
    expect(screen.getByRole('alert')).toHaveTextContent('成员或密码错误')

    await user.clear(screen.getByLabelText('密码'))
    await user.type(screen.getByLabelText('密码'), 'demo123')
    await user.click(screen.getByRole('button', { name: /^登录/ }))
    expect(screen.getByRole('heading', { name: /好，示例管理员/ })).toBeInTheDocument()

    await user.click(screen.getAllByRole('button', { name: /示例管理员/ })[0])
    await user.click(screen.getByRole('button', { name: '退出登录' }))
    expect(screen.getByRole('heading', { name: '登录 G43' })).toBeInTheDocument()
  })

  it('创建需求时校验必填项并使用基础字段', async () => {
    renderApp()
    const user = await loginAs()
    await user.click(screen.getByRole('button', { name: '新建需求' }))
    const dialog = screen.getByRole('dialog', { name: '新建需求' })
    await user.click(within(dialog).getByRole('button', { name: '创建需求' }))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('请填写需求标题')

    await user.type(within(dialog).getByPlaceholderText('用一句话清晰描述需求'), '客户端自动化测试需求')
    await user.click(within(dialog).getByRole('button', { name: '创建需求' }))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('请填写需求描述')

    await user.type(within(dialog).getByPlaceholderText('输入需求背景、目标、范围和验收说明…'), '验证客户端创建、详情与本地存储流程。')
    await user.click(within(dialog).getByRole('button', { name: '创建需求' }))

    expect(screen.getByDisplayValue('客户端自动化测试需求')).toBeInTheDocument()
  })

  it('开发人员不能删除需求或设置系统保护终态', async () => {
    renderApp()
    const user = await loginAs('member-a')
    await user.click(screen.getByRole('button', { name: '需求' }))
    await user.click(screen.getAllByText('REQ-0048')[0])
    const drawer = screen.getByLabelText(/REQ-0048 需求详情/)
    expect(within(drawer).getByRole('button', { name: '永久删除' })).toBeDisabled()
    const status = within(drawer).getByLabelText('需求状态')
    expect(within(status).getByRole('option', { name: '已完成（仅管理员）' })).toBeDisabled()
    expect(within(status).getByRole('option', { name: '已关闭（仅管理员）' })).toBeDisabled()
  })

  it('父任务存在未完成子任务时不能设置为已完成或已关闭', async () => {
    renderApp()
    const user = await loginAs()
    await user.click(screen.getByRole('button', { name: '需求' }))
    await user.click(screen.getAllByText('REQ-0045')[0])
    const drawer = screen.getByLabelText(/REQ-0045 需求详情/)
    const status = within(drawer).getByLabelText('需求状态')
    expect(within(status).getByRole('option', { name: '已完成（需先完成全部子任务）' })).toBeDisabled()
    expect(within(status).getByRole('option', { name: '已关闭（需先完成全部子任务）' })).toBeDisabled()
    expect(within(status).getByRole('option', { name: '已上线' })).toBeEnabled()
  })

  it('平台账号与企微邮箱独立，并允许不绑定企微新增成员', async () => {
    renderApp()
    const user = await loginAs()
    await user.click(screen.getByRole('button', { name: '系统设置' }))

    await user.type(screen.getByPlaceholderText('登录昵称 / 系统显示名称'), '邮箱绑定成员')
    await user.type(screen.getByPlaceholderText('平台唯一账号'), 'platform-user')
    await user.type(screen.getByPlaceholderText('name@company.com'), 'member@example.com')
    await user.type(screen.getByLabelText(/初始密码/), 'Initial123!')
    await user.click(screen.getByRole('button', { name: '验证企微' }))
    expect(screen.getByText(/userid wx-member/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '新增成员' }))

    expect(screen.getAllByText('邮箱绑定成员').length).toBeGreaterThan(0)
    expect(screen.getByText('platform-user')).toBeInTheDocument()
    expect(screen.getAllByText('已绑定').length).toBeGreaterThan(0)

    await user.type(screen.getByPlaceholderText('登录昵称 / 系统显示名称'), '未绑定成员')
    await user.type(screen.getByPlaceholderText('平台唯一账号'), 'unbound-user')
    await user.type(screen.getByLabelText(/初始密码/), 'Initial123!')
    await user.click(screen.getByRole('button', { name: '新增成员' }))
    expect(screen.getAllByText('未绑定成员').length).toBeGreaterThan(0)
  })
})


