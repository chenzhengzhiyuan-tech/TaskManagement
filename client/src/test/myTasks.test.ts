import { expect, it } from 'vitest'
import { isMyTask } from '../components/RequirementFilters'

it('我的任务包含自己处理以及自己待验收，但不包含其他验收状态', () => {
  expect(isMyTask({ assigneeId: 'me', reviewerId: 'other', statusId: 'completed' }, 'me')).toBe(true)
  expect(isMyTask({ assigneeId: 'other', reviewerId: 'me', statusId: 'review' }, 'me')).toBe(true)
  expect(isMyTask({ assigneeId: 'other', reviewerId: 'me', statusId: 'todo' }, 'me')).toBe(false)
  expect(isMyTask({ assigneeId: 'other', reviewerId: 'me', statusId: 'completed' }, 'me')).toBe(false)
  expect(isMyTask({ assigneeId: 'other', reviewerId: 'other', statusId: 'review' }, 'me')).toBe(false)
})
