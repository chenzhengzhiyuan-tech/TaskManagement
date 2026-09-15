const API_BASE = (import.meta.env.VITE_API_BASE_URL || '/api').replace(/\/$/, '')
const TOKEN_KEY = 'g43-api-token-v1'

export class ApiError extends Error {
  status: number
  body?: unknown
  constructor(status: number, message: string, body?: unknown) { super(message); this.status = status; this.body = body }
}

export function getApiToken() { return localStorage.getItem(TOKEN_KEY) }
export function setApiToken(token: string | null) { if (token) localStorage.setItem(TOKEN_KEY, token); else localStorage.removeItem(TOKEN_KEY) }

async function parseError(response: Response) {
  try {
    const body = await response.json() as { message?: string; detail?: string; title?: string }
    return { message: body.message || body.detail || body.title || `请求失败（${response.status}）`, body }
  } catch { return { message: `请求失败（${response.status}）`, body: undefined } }
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const token = getApiToken()
  if (token) headers.set('Authorization', `Bearer ${token}`)
  if (init.body && !(init.body instanceof Blob) && !(init.body instanceof FormData) && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const response = await fetch(`${API_BASE}${path}`, { ...init, headers })
  if (!response.ok) { const error = await parseError(response); throw new ApiError(response.status, error.message, error.body) }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return text.trim() ? JSON.parse(text) as T : undefined as T
}

export async function apiBlob(path: string): Promise<Blob> {
  const headers = new Headers(); const token = getApiToken(); if (token) headers.set('Authorization', `Bearer ${token}`)
  const response = await fetch(`${API_BASE}${path}`, { headers })
  if (!response.ok) { const error = await parseError(response); throw new ApiError(response.status, error.message, error.body) }
  return response.blob()
}

