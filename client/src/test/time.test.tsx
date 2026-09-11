import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { initialData } from '../data'
import { chinaDateKey, dashboardDate, formatDate, greeting, isOverdue } from '../utils'
import { useClock } from '../useClock'

afterEach(() => vi.useRealTimers())

describe('北京时间', () => {
  it('按中国日期而不是旧种子日期或UTC显示工作台与超期状态', () => {
    const now = new Date('2026-08-30T16:00:00Z')
    expect(chinaDateKey(now)).toBe('2026-08-31')
    expect(dashboardDate(now)).toBe('MONDAY · 2026.08.31')
    expect(dashboardDate(new Date('2026-08-31T16:00:00Z'))).toBe('TUESDAY · 2026.09.01')
    expect(formatDate('2026-08-30T16:00:00Z', true)).toContain('08/31')
    const requirement = { ...initialData.requirements[0], dueDate: '2026-08-30', statusId: 'todo' }
    expect(isOverdue(requirement, initialData.statuses, now)).toBe(true)
    expect(isOverdue({ ...requirement, dueDate: '2026-08-31' }, initialData.statuses, now)).toBe(false)
    expect(isOverdue({ ...requirement, statusId: 'completed' }, initialData.statuses, now)).toBe(false)
    expect(greeting(new Date('2026-08-31T01:00:00Z'))).toBe('上午好')
    expect(greeting(new Date('2026-08-31T06:00:00Z'))).toBe('下午好')
    expect(greeting(new Date('2026-08-31T12:00:00Z'))).toBe('晚上好')
  })

  it('页面持续打开时跨北京时间午夜自动更新', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-31T15:59:59Z'))
    function Clock() { return <span>{dashboardDate(useClock())}</span> }
    render(<Clock />)
    expect(screen.getByText('MONDAY · 2026.08.31')).toBeInTheDocument()
    act(() => vi.advanceTimersByTime(1000))
    expect(screen.getByText('TUESDAY · 2026.09.01')).toBeInTheDocument()
  })
})
