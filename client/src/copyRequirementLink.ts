export function requirementLink(id: string) {
  const url = new URL(window.location.pathname, window.location.origin)
  url.searchParams.set('requirement', id)
  return url.href
}

export async function copyRequirementLink(id: string) {
  return copyText(requirementLink(id))
}

export async function copyText(link: string) {
  if (window.isSecureContext && navigator.clipboard?.writeText) {
    try { await navigator.clipboard.writeText(link); return } catch { /* Fall back for restricted clipboard permissions. */ }
  }
  // The production intranet uses HTTP, where Clipboard API is unavailable.
  const input = document.createElement('textarea')
  input.value = link
  input.style.cssText = 'position:fixed;left:-9999px;top:0'
  const previousFocus = document.activeElement
  document.body.appendChild(input)
  try {
    input.focus(); input.select()
    if (!document.execCommand('copy')) throw new Error('复制失败，请检查浏览器剪贴板权限')
  } finally {
    input.remove()
    if (previousFocus instanceof HTMLElement) previousFocus.focus()
  }
}
