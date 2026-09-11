import { afterEach, expect, it, vi } from 'vitest'
import { copyRequirementLink, requirementLink } from '../copyRequirementLink'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

it('链接只包含需求编号，不携带原页面其他参数', () => {
  window.history.replaceState({}, '', '/?password=example&requirement=old#test')
  const url = new URL(requirementLink('REQ-0030'))
  expect(url.search).toBe('?requirement=REQ-0030')
  expect(url.hash).toBe('')
  window.history.replaceState({}, '', '/')
})

it('内网 HTTP 使用复制后备方案并清理临时输入框', async () => {
  vi.stubGlobal('isSecureContext', false)
  const copy = vi.fn(() => true)
  Object.defineProperty(document, 'execCommand', { configurable: true, value: copy })
  await copyRequirementLink('REQ-0030')
  expect(copy).toHaveBeenCalledWith('copy')
  expect(document.querySelector('textarea')).toBeNull()
})
