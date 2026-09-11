import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
vi.hoisted(() => vi.stubEnv('VITE_DATA_MODE', 'api'))
vi.mock('../api', async (importOriginal) => ({
  ...await importOriginal<typeof import('../api')>(),
  getApiToken: () => 'test-session',
  apiRequest: vi.fn(),
}))
import { apiRequest } from '../api'
import { initialData } from '../data'
import { AppStoreProvider, useAppStore } from '../store'
import { IterationsPage } from '../pages/IterationsPage'
import { BoardPage } from '../pages/BoardPage'
import { NewRequirementModal } from '../components/NewRequirementModal'

const snapshot = () => ({
  ...structuredClone(initialData),
  currentUser: initialData.users[0],
  modules: initialData.modules.map((name, sortOrder) => ({ id: name, name, sortOrder })),
})
let latest: ReturnType<typeof snapshot>

beforeEach(() => {
  vi.useFakeTimers()
  latest = snapshot()
  vi.mocked(apiRequest).mockImplementation(async (path) => {
    if (path === '/iterations') return latest.iterations
    if (path === '/bootstrap') return structuredClone(latest)
    throw new Error(`Unexpected request: ${path}`)
  })
})
afterEach(() => { vi.useRealTimers(); vi.clearAllMocks() })

function rollover() {
  latest.iterations = latest.iterations.map((item) => ({ ...item, state: item.id === 'it-next' ? 'active' : 'completed' }))
  latest.requirements = latest.requirements.map((item) => item.iterationId === 'it-current'
    && !['completed', 'closed'].includes(item.statusId) ? { ...item, iterationId: 'it-next', version: (item.version ?? 1) + 1 } : item)
}

function Harness() {
  const { initializing } = useAppStore()
  if (initializing) return null
  return <><IterationsPage onOpenRequirement={() => {}} /><NewRequirementModal open onClose={() => {}} onCreated={() => {}} /></>
}

it('后台轮询同步当前迭代、时间线和需求，同时保留新建需求草稿', async () => {
  await act(async () => { render(<AppStoreProvider><Harness /></AppStoreProvider>) })
  expect(screen.getByRole('heading', { name: '20260824-20260828' })).toBeInTheDocument()
  const title = screen.getByPlaceholderText('用一句话清晰描述需求')
  fireEvent.change(title, { target: { value: '跨迭代时正在输入的草稿' } })
  rollover()
  await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })
  expect(screen.getByRole('heading', { name: '20260831-20260904' })).toBeInTheDocument()
  expect(title).toHaveValue('跨迭代时正在输入的草稿')
  expect(screen.getByText('2026-08-31 — 2026-09-04', { selector: 'small' })).toBeInTheDocument()
  expect(vi.mocked(apiRequest).mock.calls.filter(([path]) => path === '/bootstrap')).toHaveLength(2)
  await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })
  expect(vi.mocked(apiRequest).mock.calls.filter(([path]) => path === '/bootstrap')).toHaveLength(2)
})

it('切回页面时同步迭代，看板保留全部迭代筛选', async () => {
  await act(async () => { render(<AppStoreProvider><BoardPage onOpenRequirement={() => {}} /></AppStoreProvider>) })
  rollover()
  await act(async () => { window.dispatchEvent(new Event('focus')) })
  expect(screen.getByRole('button', { name: '迭代筛选' })).toHaveTextContent('全部迭代')
  fireEvent.click(screen.getByRole('button', { name: '迭代筛选' }))
  expect(screen.getByRole('checkbox', { name: '20260831-20260904' })).toBeInTheDocument()
})

it('迭代刷新返回较旧快照时，不覆盖已经保存成功的最新任务状态', async () => {
  const id = latest.requirements[0].id
  latest.requirements[0].version = 5
  function StateProbe() {
    const { requirements, updateRequirement, initializing } = useAppStore()
    if (initializing) return null
    const item = requirements.find((entry) => entry.id === id)!
    return <><output>{item.statusId}:{item.version}:{item.iterationId}</output>
      <button onClick={() => void updateRequirement(id, { statusId: 'review' })}>提交验收</button></>
  }
  await act(async () => { render(<AppStoreProvider><StateProbe /></AppStoreProvider>) })
  rollover()
  const oldSnapshot = structuredClone(latest)
  let completeRefresh!: (value: typeof latest) => void
  vi.mocked(apiRequest).mockImplementation(async (path) => {
    if (path === '/iterations') return latest.iterations
    if (path === '/bootstrap') return new Promise((resolve) => { completeRefresh = resolve })
    if (path === `/requirements/${id}`) return { ...oldSnapshot.requirements[0], statusId: 'review', version: 7 }
    throw new Error(`Unexpected request: ${path}`)
  })
  await act(async () => { window.dispatchEvent(new Event('focus')) })
  await act(async () => { fireEvent.click(screen.getByText('提交验收')) })
  expect(screen.getByRole('status')).toHaveTextContent('review:7:it-next')
  await act(async () => { completeRefresh(oldSnapshot) })
  expect(screen.getByRole('status')).toHaveTextContent('review:7:it-next')
})
