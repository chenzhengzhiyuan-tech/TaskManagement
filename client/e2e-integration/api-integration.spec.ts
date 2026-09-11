import { expect, test } from '@playwright/test'

async function login(page: import('@playwright/test').Page, account = 'admin') {
  await page.goto('/')
  await page.getByLabel('成员').fill(account)
  await page.locator('#login-password').fill('demo123')
  await page.getByRole('button', { name: /^登录/ }).click()
  await expect(page.getByRole('heading', { name: account === 'admin' ? /好，示例管理员/ : /好，示例成员甲/ })).toBeVisible()
}

test('API登录和Bootstrap真实加载', async ({ page }) => {
  const responses: number[] = []
  page.on('response', (response) => { if (response.url().includes('/api/bootstrap')) responses.push(response.status()) })
  await login(page)
  expect(responses).toContain(200)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '项目设置', exact: true }).click()
  await page.getByRole('button', { name: '系统信息', exact: true }).click()
  await expect(page.getByText('API服务端')).toBeVisible()
  await expect(page.getByText('PostgreSQL / SQLite API')).toBeVisible()
})

test('服务端创建的模块和需求刷新后仍然存在', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '项目设置', exact: true }).click()
  await page.getByRole('button', { name: '归属模块', exact: true }).click()
  await page.getByPlaceholder('新模块名称').fill('联调模块')
  await page.getByRole('button', { name: '添加模块' }).click()
  await expect(page.getByText('模块已添加')).toBeVisible()

  await page.getByRole('button', { name: '新建需求' }).click()
  const dialog = page.getByRole('dialog', { name: '新建需求' })
  await dialog.getByPlaceholder('用一句话清晰描述需求').fill('API联调持久化需求')
  await dialog.getByText('归属模块').locator('..').getByRole('combobox').selectOption('联调模块')
  await dialog.getByPlaceholder('输入需求背景、目标、范围和验收说明…').fill('验证服务端保存后刷新仍然存在。')
  await dialog.getByRole('button', { name: '创建需求' }).click()
  await expect(page.locator('textarea.drawer-title-input')).toHaveValue('API联调持久化需求')
  await page.getByRole('button', { name: '关闭详情' }).click()
  await page.reload()
  await expect(page.getByRole('heading', { name: /好，示例管理员/ })).toBeVisible()
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByPlaceholder('搜索编号、标题或模块').fill('API联调持久化需求')
  await expect(page.getByText('API联调持久化需求')).toBeVisible()
})

test('服务端权限拒绝开发人员保护终态', async ({ page }) => {
  await login(page, 'member-a')
  await expect(page.getByRole('button', { name: '系统设置' })).toHaveCount(0)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByText('REQ-0048').first().click()
  const status = page.getByLabel('需求状态')
  await expect(status.getByRole('option', { name: '已完成（仅管理员）' })).toBeDisabled()
  await expect(page.getByRole('button', { name: '永久删除' })).toBeDisabled()
})

test('客户端通过服务端执行真实分片上传并预览下载', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByText('REQ-0048').first().click()
  const drawer = page.getByLabel(/REQ-0048 需求详情/)
  await drawer.locator('input[type=file]').setInputFiles('e2e/fixtures/preview.png')
  await expect(drawer.getByText('图片已上传，可预览或下载')).toBeVisible({ timeout: 15_000 })
  await expect(drawer.getByText('preview.png')).toHaveCount(1)
  await drawer.getByRole('button', { name: '预览 preview.png' }).click()
  const preview = page.getByRole('dialog', { name: '预览 preview.png' })
  await expect(preview.locator('img')).toBeVisible()
  const download = page.waitForEvent('download')
  await preview.getByRole('button', { name: '下载' }).click()
  expect((await download).suggestedFilename()).toBe('preview.png')
})

test('管理员无需企微验证也可新建并删除未绑定成员', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '账号', exact: true }).click()
  const card = page.locator('.settings-form-card').filter({ hasText: '新增成员' })
  await card.getByPlaceholder('登录昵称 / 系统显示名称').fill('功能测试成员')
  await card.getByPlaceholder('企业微信 userid').fill('featureuser')
  await card.getByRole('textbox', { name: /初始密码/ }).fill('Initial123!')
  await expect(card.getByText(/企微验证为可选/)).toBeVisible()
  await expect(card.getByRole('button', { name: '新增成员' })).toBeEnabled()
  await card.getByRole('button', { name: '新增成员' }).click()
  await expect(page.getByText('成员已创建，可稍后绑定企微')).toBeVisible()

  const row = page.locator('.users-settings-table .settings-table__row').filter({ hasText: '功能测试成员' })
  await expect(row).toContainText('featureuser')
  await expect(row).toContainText('未绑定')
  await row.getByRole('button', { name: '删除账号' }).click()
  const confirmation = page.getByRole('dialog', { name: '删除账号“功能测试成员”？' })
  await expect(confirmation).toContainText('该账号将立即无法登录')
  await confirmation.getByRole('button', { name: '确认删除账号' }).click()
  await expect(page.getByText('账号已删除')).toBeVisible()
  await expect(row).toHaveCount(0)
})

test('管理员可预检并批量导入独立需求', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByRole('button', { name: '导入', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '批量导入需求' })
  await dialog.locator('input[type=file]').setInputFiles('e2e/fixtures/requirements-import.csv')
  await expect(dialog.getByText('2 条需求可以导入')).toBeVisible()
  await expect(dialog.getByText(/忽略“父需求”列/)).toBeVisible()
  await dialog.getByRole('button', { name: /确认导入 2 条/ }).click()
  await expect(page.getByText('已批量创建 2 条需求')).toBeVisible()
  await page.getByPlaceholder('搜索编号、标题或模块').fill('浏览器批量导入')
  await expect(page.getByText('浏览器批量导入一')).toBeVisible()
  await expect(page.getByText('浏览器批量导入二')).toBeVisible()
})

test('企微需求链接可直接打开对应需求详情且右上角无通知入口', async ({ page }) => {
  await login(page)
  await expect(page.getByRole('button', { name: /通知/ })).toHaveCount(0)
  await page.goto('/?requirement=REQ-0048')
  await expect(page.getByLabel(/REQ-0048 需求详情/)).toBeVisible()
})

test('管理员设置需求类型和新建默认值', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '项目设置', exact: true }).click()
  await page.getByRole('button', { name: '需求单类型', exact: true }).click()
  await page.getByPlaceholder('新需求单类型').fill('临时专项内容')
  await page.getByRole('button', { name: '添加类型' }).click()
  await expect(page.getByText('类型已添加')).toBeVisible()
  await page.getByRole('button', { name: '需求默认值' }).click()
  const defaults = page.locator('.settings-form-card').filter({ hasText: '需求单类型' })
  await defaults.getByText('需求单类型').locator('..').getByRole('combobox').selectOption({ label: '临时专项内容' })
  await defaults.getByText('模块').locator('..').getByRole('combobox').selectOption('UI')
  await defaults.getByRole('button', { name: '保存默认值' }).click()
  await expect(page.getByText('需求默认值已保存')).toBeVisible()
  await page.getByRole('button', { name: '新建需求' }).click()
  const dialog = page.getByRole('dialog', { name: '新建需求' })
  await expect(dialog.getByText('需求单类型').locator('..').getByRole('combobox')).toHaveValue(/.+/)
  await expect(dialog.getByText('模块').locator('..').getByRole('combobox')).toHaveValue('UI')
})

test('登录页使用G43海报且支持浅色模式', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: '登录 G43' })).toBeVisible()
  await expect(page.getByText('让每一条需求')).toHaveCount(0)
  await expect(page.locator('.login-brand-panel')).toHaveCSS('background-image', /g43-login-background/)
  await page.getByRole('button', { name: '切换明暗主题' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
})

test('管理员工作日历加载2026官方调休数据', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '项目设置', exact: true }).click()
  await page.getByRole('button', { name: '工作日历', exact: true }).click()
  const row = page.locator('.calendar-list > div').filter({ hasText: '2026-02-14' })
  await expect(row).toContainText('工作日')
  await expect(row).toContainText('春节调休上班')
})


test('系统设置一级导航仅保留账号、项目设置、企微设置', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await expect(page.locator('.settings-nav button')).toHaveText(['账号', '项目设置', '企微设置'])
  await page.getByRole('button', { name: '项目设置', exact: true }).click()
  await expect(page.locator('.settings-subnav')).toContainText('状态')
  await expect(page.locator('.settings-subnav')).toContainText('品牌设置')
})

test('需求单类型为独立列且不同类型自动使用不同颜色', async ({ page }) => {
  await login(page)
  for (const [title, type] of [['类型颜色A', '周版本预期内容'], ['类型颜色B', '周开发新增内容']] as const) {
    await page.getByRole('button', { name: '新建需求', exact: true }).click()
    const dialog = page.getByRole('dialog', { name: '新建需求' })
    await dialog.getByPlaceholder('用一句话清晰描述需求').fill(title)
    await dialog.getByText('需求单类型').locator('..').getByRole('combobox').selectOption({ label: type })
    await dialog.getByPlaceholder('输入需求背景、目标、范围和验收说明…').fill('验证需求单类型独立列和自动配色。')
    await dialog.getByRole('button', { name: '创建需求' }).click()
    await page.getByRole('button', { name: '关闭详情' }).click()
  }
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByPlaceholder('搜索编号、标题或模块').fill('类型颜色')
  await expect(page.getByRole('columnheader', { name: '需求单类型' })).toBeVisible()
  const cells = page.locator('.requirement-type-cell')
  await expect(cells).toHaveCount(2)
  const colors = await cells.evaluateAll((items) => items.map((item) => getComputedStyle(item).backgroundColor))
  expect(new Set(colors).size).toBe(2)
})

test('admin看板可移动任意需求到任意状态并自动处理版本冲突', async ({ page, request }) => {
  await login(page)
  await page.getByRole('button', { name: '看板', exact: true }).click()
  const token = await page.evaluate(() => localStorage.getItem('g43-api-token-v1'))
  const headers = { Authorization: `Bearer ${token}` }
  const req = await request.get('http://127.0.0.1:5080/api/requirements/REQ-0048', { headers })
  const current = await req.json()
  await request.patch('http://127.0.0.1:5080/api/requirements/REQ-0048', { headers, data: { statusId: 'todo', version: current.version, summary: '制造客户端版本冲突' } })
  for (const status of ['已完成', '已关闭', '进行中']) {
    const card = page.locator('.kanban-card').filter({ hasText: 'REQ-0048' })
    const target = page.locator('.kanban-column').filter({ has: page.getByText(status, { exact: true }) })
    const persisted = page.waitForResponse((response) => response.request().method() === 'PATCH' && response.url().endsWith('/api/requirements/REQ-0048') && response.status() === 200)
    const dataTransfer = await page.evaluateHandle(() => new DataTransfer())
    await card.dispatchEvent('dragstart', { dataTransfer })
    await target.dispatchEvent('dragover', { dataTransfer })
    await target.dispatchEvent('drop', { dataTransfer })
    await card.dispatchEvent('dragend', { dataTransfer })
    await persisted
    await expect(page.getByText(`已移动到“${status}”`)).toBeVisible()
  }
})

test('报表页不显示未实现的导出按钮', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '报表', exact: true }).click()
  await expect(page.getByRole('button', { name: /导出/ })).toHaveCount(0)
})

test('项目设置操作按钮与内容保持统一间距', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '项目设置', exact: true }).click()
  await page.getByRole('button', { name: '品牌设置', exact: true }).click()
  const preview = page.locator('.branding-preview')
  const actions = page.locator('.settings-action-row')
  const [previewBox, actionBox] = await Promise.all([preview.boundingBox(), actions.boundingBox()])
  expect(previewBox).not.toBeNull(); expect(actionBox).not.toBeNull()
  expect((actionBox?.y ?? 0) - ((previewBox?.y ?? 0) + (previewBox?.height ?? 0))).toBeGreaterThanOrEqual(18)
})

test('浅色模式关键页面不存在低对比文字', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '切换明暗主题' }).click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  const pages = ['工作台', '需求', '迭代', '看板', '报表', '系统设置']
  for (const name of pages) {
    await page.getByRole('button', { name, exact: true }).click()
    const failures = await page.evaluate(() => {
      const parse = (value: string) => { const match = value.match(/rgba?\(([^)]+)\)/); if (!match) return null; const parts = match[1].split(',').map(Number); return [parts[0], parts[1], parts[2], parts[3] ?? 1] }
      const blend = (fg: number[], bg: number[]) => [0, 1, 2].map((index) => fg[index] * fg[3] + bg[index] * (1 - fg[3]))
      const luminance = (rgb: number[]) => { const values = rgb.map((item) => { const value = item / 255; return value <= .03928 ? value / 12.92 : Math.pow((value + .055) / 1.055, 2.4) }); return .2126 * values[0] + .7152 * values[1] + .0722 * values[2] }
      const contrast = (a: number[], b: number[]) => (Math.max(luminance(a), luminance(b)) + .05) / (Math.min(luminance(a), luminance(b)) + .05)
      const background = (element: Element) => { let node: Element | null = element; let result = [255, 255, 255]; const stack: Element[] = []; while (node) { stack.push(node); node = node.parentElement } for (let i = stack.length - 1; i >= 0; i--) { const color = parse(getComputedStyle(stack[i]).backgroundColor); if (color && color[3] > 0) result = blend(color, [...result, 1]) } return result }
      const output: string[] = []
      document.querySelectorAll('h1,h2,h3,p,span,small,strong,label,button,th,td,time,a').forEach((element) => { const rect = element.getBoundingClientRect(); const style = getComputedStyle(element); if (!rect.width || !rect.height || style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) < .55 || element.matches(':disabled')) return; const text = [...element.childNodes].filter((node) => node.nodeType === 3).map((node) => node.textContent?.trim()).join(' ').trim(); if (!text) return; const color = parse(style.color); if (!color) return; const ratio = contrast(color, background(element)); const size = parseFloat(style.fontSize); const weight = parseInt(style.fontWeight) || 400; const minimum = size >= 24 || (size >= 18.66 && weight >= 700) ? 3 : 4.5; if (ratio < minimum) output.push(`${text.slice(0, 25)}:${ratio.toFixed(2)}`) })
      return output
    })
    expect(failures, `${name}: ${failures.join(', ')}`).toEqual([])
  }
})
