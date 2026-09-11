import { expect, it } from 'vitest'
import { parseClipboard } from '../batchDraft'

it('解析单列标题和 Excel 带引号的多行单元格', () => {
  expect(parseClipboard('任务一\r\n任务二\r\n')).toEqual([['任务一'], ['任务二']])
  expect(parseClipboard('标题\t描述\n任务一\t"第一段\n第二段\t内容"\n任务二\t"带""引号"""')).toEqual([['标题','描述'],['任务一','第一段\n第二段\t内容'],['任务二','带"引号"']])
  expect(() => parseClipboard('标题\t"未闭合')).toThrow()
})
