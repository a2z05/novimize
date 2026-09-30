import { useState, useCallback } from 'react'
import { invoke } from '@tauri-apps/api/core'

export function useTauriCommand<T = string>(command: string) {
  const [data, setData] = useState<T | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const execute = useCallback(async (...args: any[]) => {
    setLoading(true)
    setError(null)
    try {
      const result = await invoke<T>(command, ...args)
      setData(result)
      return result
    } catch (err: any) {
      const msg = typeof err === 'string' ? err : err?.message || 'Unknown error'
      setError(msg)
      throw err
    } finally {
      setLoading(false)
    }
  }, [command])

  return { data, loading, error, execute, setData }
}

export async function invokeJson<T = any>(command: string, args?: Record<string, any>): Promise<T> {
  const raw = await invoke<string>(command, args || {})
  return JSON.parse(raw) as T
}
