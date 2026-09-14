import type { CommentMention } from './types'

// An edit inside a mention makes it plain text; edits before it shift the span.
export function adjustMentions(before: string, after: string, mentions: CommentMention[]) {
  let start = 0
  while (start < before.length && start < after.length && before[start] === after[start]) start++
  let oldEnd = before.length, newEnd = after.length
  while (oldEnd > start && newEnd > start && before[oldEnd - 1] === after[newEnd - 1]) { oldEnd--; newEnd-- }
  const delta = after.length - before.length
  return mentions.flatMap(mention => {
    if (mention.start + mention.length <= start) return [mention]
    if (mention.start >= oldEnd) return [{ ...mention, start: mention.start + delta }]
    return []
  }).filter(mention => after.slice(mention.start, mention.start + mention.length) === `@${mention.name}`)
}
