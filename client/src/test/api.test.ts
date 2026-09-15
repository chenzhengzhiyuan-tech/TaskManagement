import { afterEach, expect, it, vi } from 'vitest'
import { apiRequest } from '../api'

afterEach(() => vi.unstubAllGlobals())

it('接受绑定接口的空 200 成功响应', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 200 })))
  await expect(apiRequest('/requirements/parent/children', { method: 'POST', body: JSON.stringify({ childIds: ['child'] }) })).resolves.toBeUndefined()
})

it('保留 JSON 响应和业务错误处理', async () => {
  vi.stubGlobal('fetch', vi.fn()
    .mockResolvedValueOnce(new Response(JSON.stringify({ id: 'child' })))
    .mockResolvedValueOnce(new Response(JSON.stringify({ message: '该需求已有父需求' }), { status: 409 })))
  await expect(apiRequest('/requirements/child')).resolves.toEqual({ id: 'child' })
  await expect(apiRequest('/requirements/parent/children')).rejects.toThrow('该需求已有父需求')
})
