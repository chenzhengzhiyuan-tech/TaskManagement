import { expect, test } from '@playwright/test'

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => { localStorage.clear(); sessionStorage.clear() })
})

async function login(page: import('@playwright/test').Page, account = 'admin') {
  await page.goto('/')
  await page.getByLabel('成员').fill(account)
  await page.locator('#login-password').fill('demo123')
  await page.getByRole('button', { name: /^登录/ }).click()
}

test('管理员完成登录、新建、编辑、评论和导出流程', async ({ page }) => {
  const errors: string[] = []
  page.on('console', (message) => { if (message.type() === 'error') errors.push(message.text()) })
  page.on('pageerror', (error) => errors.push(error.message))

  await login(page)
  await expect(page.getByRole('heading', { name: /好，示例管理员/ })).toBeVisible()
  await page.getByRole('button', { name: '新建需求' }).click()
  const dialog = page.getByRole('dialog', { name: '新建需求' })
  await dialog.getByPlaceholder('用一句话清晰描述需求').fill('E2E 客户端交互验证')
  await dialog.getByPlaceholder('输入需求背景、目标、范围和验收说明…').fill('验证创建、详情、自定义字段、评论和导出。')
  await dialog.getByRole('button', { name: '创建需求' }).click()

  const drawer = page.getByLabel(/REQ-0049 需求详情/)
  await expect(drawer.locator('textarea.drawer-title-input')).toHaveValue('E2E 客户端交互验证')
  await drawer.getByRole('button', { name: /评论 0/ }).click()
  await drawer.getByPlaceholder(/发表评论/).fill('@示例成员甲 请检查客户端表现')
  await drawer.getByRole('button', { name: '发送' }).click()
  await expect(drawer.getByText('@示例成员甲 请检查客户端表现')).toBeVisible()
  await drawer.getByRole('button', { name: '关闭详情' }).click()

  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: '导出' }).click()
  await download
  expect(errors).toEqual([])
})

test('开发人员看不到系统设置且不能进入保护终态', async ({ page }) => {
  await login(page, 'member-a')
  await expect(page.getByRole('button', { name: '系统设置' })).toHaveCount(0)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByText('REQ-0048').first().click()
  const drawer = page.getByLabel(/REQ-0048 需求详情/)
  await expect(drawer.getByRole('button', { name: '永久删除' })).toBeDisabled()
  await expect(drawer.getByLabel('需求状态').getByRole('option', { name: '已完成（仅管理员）' })).toBeDisabled()
  await expect(drawer.getByLabel('需求状态').getByRole('option', { name: '已关闭（仅管理员）' })).toBeDisabled()
})

test('新增成员表单连续输入时保持焦点', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '账号', exact: true }).click()

  const memberName = page.getByPlaceholder('登录昵称 / 系统显示名称')
  await memberName.click()
  await memberName.pressSequentially('FocusTester')
  await expect(memberName).toHaveValue('FocusTester')
  await expect(memberName).toBeFocused()

  const account = page.getByPlaceholder('企业微信 userid')
  await account.click()
  await account.pressSequentially('focus.tester')
  await expect(account).toHaveValue('focus.tester')
  await expect(account).toBeFocused()

  const password = page.getByRole('textbox', { name: /初始密码/ })
  await password.click()
  await password.pressSequentially('Initial123!')
  await expect(password).toHaveValue('Initial123!')
  await expect(password).toBeFocused()
})

test('管理员可直接新增未绑定成员并通过可见入口删除账号', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '账号', exact: true }).click()
  const card = page.locator('.settings-form-card').filter({ hasText: '新增成员' })
  await card.getByPlaceholder('登录昵称 / 系统显示名称').fill('未验证成员')
  await card.getByPlaceholder('企业微信 userid').fill('unbound.user')
  await card.getByRole('textbox', { name: /初始密码/ }).fill('Initial123!')
  await expect(card.getByRole('button', { name: '新增成员' })).toBeEnabled()
  await card.getByRole('button', { name: '新增成员' }).click()

  const row = page.locator('.users-settings-table .settings-table__row').filter({ hasText: '未验证成员' })
  await expect(row).toContainText('未绑定')
  await expect(row.getByRole('button', { name: '删除账号' })).toBeVisible()
  await row.getByRole('button', { name: '删除账号' }).click()
  const confirmation = page.getByRole('dialog', { name: '删除账号“未验证成员”？' })
  await confirmation.getByRole('button', { name: '确认删除账号' }).click()
  await expect(row).toHaveCount(0)
})

test('1366x768下主应用无页面级横向溢出', async ({ page }) => {
  await login(page)
  const metrics = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, viewport: document.documentElement.clientWidth }))
  expect(metrics.width).toBeLessThanOrEqual(metrics.viewport)
  await page.getByRole('button', { name: '看板', exact: true }).click()
  await expect(page.getByRole('heading', { name: '看板' })).toBeVisible()
})



test('管理员新增归属模块后可在新建需求中选择', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '系统设置' }).click()
  await page.getByRole('button', { name: '项目设置', exact: true }).click()
  await page.getByRole('button', { name: '归属模块', exact: true }).click()
  await page.getByPlaceholder('新模块名称').fill('音频')
  await page.getByRole('button', { name: '添加模块' }).click()
  await expect(page.getByText('模块已添加')).toBeVisible()

  await page.getByRole('button', { name: '新建需求' }).click()
  const dialog = page.getByRole('dialog', { name: '新建需求' })
  await expect(dialog.getByText('归属模块').locator('..').getByRole('combobox')).toContainText('音频')
})

test('父需求可搜索，创建后列表展示归属关系', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '新建需求' }).click()
  const dialog = page.getByRole('dialog', { name: '新建需求' })
  await dialog.getByPlaceholder('用一句话清晰描述需求').fill('父子层级展示测试')
  await dialog.getByPlaceholder('输入需求背景、目标、范围和验收说明…').fill('验证父需求搜索和列表层级视觉。')
  await dialog.getByPlaceholder('搜索需求编号、标题或模块').fill('REQ-0045')
  await dialog.getByRole('option', { name: /REQ-0045/ }).click()
  await dialog.getByRole('button', { name: '创建需求' }).click()
  await page.getByRole('button', { name: '关闭详情' }).click()
  const row = page.getByRole('row', { name: /父子层级展示测试/ })
  await expect(row).toHaveClass(/requirement-row--child/)
  const rows = await page.locator('.requirements-table tbody tr').allTextContents()
  expect(rows.findIndex((text) => text.includes('父子层级展示测试'))).toBe(rows.findIndex((text) => text.includes('REQ-0045')) + 1)
})

test('图片附件只新增一次，并支持预览和下载', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByText('REQ-0048').first().click()
  const drawer = page.getByLabel(/REQ-0048 需求详情/)
  await drawer.locator('input[type=file]').setInputFiles('e2e/fixtures/preview.png')
  await expect(drawer.getByText('图片已上传，可预览或下载')).toBeVisible()
  await expect(drawer.getByText('preview.png')).toHaveCount(1)

  await drawer.getByRole('button', { name: '预览 preview.png' }).click()
  const preview = page.getByRole('dialog', { name: '预览 preview.png' })
  await expect(preview.locator('img')).toBeVisible()
  const download = page.waitForEvent('download')
  await preview.getByRole('button', { name: '下载' }).click()
  const file = await download
  expect(file.suggestedFilename()).toBe('preview.png')
})

test('父需求可以搜索并绑定现有需求为子需求', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByText('REQ-0048').first().click()
  const drawer = page.getByLabel(/REQ-0048 需求详情/)
  await drawer.getByPlaceholder('搜索需要绑定的子需求编号、标题或模块').fill('REQ-0047')
  await drawer.getByRole('option', { name: /REQ-0047/ }).click()
  await expect(drawer.getByText('REQ-0047')).toBeVisible()
  await drawer.getByRole('button', { name: '关闭详情' }).click()

  const rootRow = page.locator('tr.requirement-row--root').filter({ hasText: 'REQ-0048' })
  const childRow = page.locator('tr.requirement-row--child').filter({ hasText: 'REQ-0047' })
  await expect(rootRow.locator('.child-count')).toHaveText('1')
  await expect(childRow).toHaveClass(/requirement-row--child/)
  await page.getByRole('button', { name: '收起 REQ-0048 子需求' }).click()
  await expect(childRow).toHaveCount(0)
})

test('树状列表按顶层需求排序且子需求紧跟父需求', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByLabel('排序').selectOption('priority')
  const rows = await page.locator('.requirements-table tbody tr').allTextContents()
  const parentIndex = rows.findIndex((text) => text.includes('REQ-0045'))
  const childIndex = rows.findIndex((text) => text.includes('REQ-0041'))
  expect(parentIndex).toBeGreaterThanOrEqual(0)
  expect(childIndex).toBe(parentIndex + 1)
  expect(rows[childIndex]).toContain('REQ-0041')
})




test('树展开按钮高亮且与需求编号对齐，列表不重复标注父子文字', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  const rootRow = page.locator('tr.requirement-row--root').filter({ hasText: 'REQ-0045' })
  const toggle = rootRow.getByRole('button', { name: '收起 REQ-0045 子需求' })
  const id = rootRow.locator('.mono-id').first()
  const [toggleBox, idBox] = await Promise.all([toggle.boundingBox(), id.boundingBox()])
  expect(toggleBox).not.toBeNull(); expect(idBox).not.toBeNull()
  expect((toggleBox?.x ?? 0) + (toggleBox?.width ?? 0)).toBeLessThan((idBox?.x ?? 0) - 2)
  expect(Math.abs(((toggleBox?.y ?? 0) + (toggleBox?.height ?? 0) / 2) - ((idBox?.y ?? 0) + (idBox?.height ?? 0) / 2))).toBeLessThanOrEqual(3)
  const style = await toggle.evaluate((element) => { const value = getComputedStyle(element); return { color: value.color, background: value.backgroundColor, width: value.width } })
  expect(style.width).toBe('22px')
  expect(style.background).not.toBe('rgba(0, 0, 0, 0)')
  const tableText = await page.locator('.requirements-table tbody').innerText()
  expect(tableText).not.toContain('顶层需求')
  expect(tableText).not.toContain('父需求 REQ')
})

test('父需求详情中的解除关系按钮在子需求行内居中', async ({ page }) => {
  await login(page)
  await page.getByRole('button', { name: '需求', exact: true }).click()
  await page.getByText('REQ-0045').first().click()
  const drawer = page.getByLabel(/REQ-0045 需求详情/)
  const unlink = drawer.getByRole('button', { name: '解除子需求 REQ-0041' })
  const row = unlink.locator('..')
  const [buttonBox, rowBox] = await Promise.all([unlink.boundingBox(), row.boundingBox()])
  expect(buttonBox).not.toBeNull(); expect(rowBox).not.toBeNull()
  expect(buttonBox?.width).toBe(28)
  expect(buttonBox?.height).toBe(28)
  expect(Math.abs(((buttonBox?.y ?? 0) + 14) - ((rowBox?.y ?? 0) + (rowBox?.height ?? 0) / 2))).toBeLessThanOrEqual(2)
})
