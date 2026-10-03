import { useCallback, useEffect, useMemo, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  Loader2,
  Lock,
  RefreshCw,
  RotateCcw,
  Search,
  Settings2,
} from 'lucide-react'
import type { ServiceChange, ServiceEntry, ServiceStatus } from '../types'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

const MODES = ['manual', 'automatic', 'disabled']

type Filter = 'all' | 'running' | 'stopped' | 'protected' | 'changed'
type Pending = { service: ServiceEntry; action: string }

export default function Services() {
  const [status, setStatus] = useState<ServiceStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<ServiceChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [preview, setPreview] = useState<ServiceChange | null>(null)
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<Filter>('all')

  const load = useCallback(
    async () => setStatus(await invokeJson<ServiceStatus>('services_action', { action: 'status' })),
    [],
  )

  const run = useCallback(async <T,>(key: string, fn: () => Promise<T>): Promise<T | null> => {
    setBusy(key)
    setError(null)
    try {
      return await fn()
    } catch (err) {
      setError(errMsg(err))
      return null
    } finally {
      setBusy(null)
    }
  }, [])

  useEffect(() => {
    void run('load', load)
  }, [run, load])

  const rows = useMemo(() => {
    const q = query.trim().toLowerCase()
    return (status?.services ?? []).filter(s => {
      if (filter === 'running' && s.status !== 'Running') return false
      if (filter === 'stopped' && s.status !== 'Stopped') return false
      if (filter === 'protected' && !s.protected) return false
      if (filter === 'changed' && !s.originalStartMode) return false
      if (!q) return true
      return (
        s.name.toLowerCase().includes(q) ||
        s.displayName.toLowerCase().includes(q) ||
        s.publisher.toLowerCase().includes(q)
      )
    })
  }, [status, query, filter])

  /**
   * Ask what the change would run before showing the dialog. The refusal for
   * a protected service arrives here too, so the dialog can explain the rule
   * instead of the write failing after the click.
   */
  async function prepare(service: ServiceEntry, action: string) {
    setPreview(null)
    const res = await run(`prep:${service.name}:${action}`, () =>
      invokeJson<ServiceChange>('services_action', { action, name: service.name, confirm: false }),
    )
    if (!res) return
    setPreview(res)
    setPending({ service, action })
  }

  async function confirm() {
    if (!pending) return
    const p = pending
    setPending(null)
    const res = await run(`do:${p.service.name}`, () =>
      invokeJson<ServiceChange>('services_action', {
        action: p.action,
        name: p.service.name,
        confirm: true,
      }),
    )
    if (res) setChange(res)
    await load()
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Settings2 size={22} className="text-[var(--color-primary)]" />
            Services
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            What is running, what needs what, and what will not be touched
          </p>
        </div>
        <button
          className="btn btn-secondary btn-sm"
          onClick={() => void run('load', load)}
          disabled={busy !== null}
        >
          {busy === 'load' ? <Loader2 size={14} className="animate-spin" /> : <RefreshCw size={14} />}
          Refresh
        </button>
      </div>

      {error && (
        <div className="card flex items-start gap-3 border-[var(--color-danger)] animate-slide-up stagger-1">
          <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
          <div className="text-[13px] text-[var(--color-danger)] break-words flex-1">{error}</div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setError(null)}>
            Dismiss
          </button>
        </div>
      )}

      {change && (
        <div
          className="card flex items-start gap-3 animate-slide-up stagger-1"
          style={{ borderColor: change.success ? 'rgba(34,197,94,0.35)' : 'rgba(239,68,68,0.4)' }}
        >
          {change.success ? (
            <CheckCircle2 size={16} className="text-[var(--color-success)] mt-0.5 flex-shrink-0" />
          ) : (
            <AlertTriangle size={16} className="text-[var(--color-danger)] mt-0.5 flex-shrink-0" />
          )}
          <div className="min-w-0 flex-1">
            <div className="text-[13px] break-words">{change.message}</div>
            {change.needsElevation && (
              <div className="text-[12px] text-[var(--color-warning)] mt-1">
                Administrator rights are required, so nothing was changed.
              </div>
            )}
          </div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setChange(null)}>
            Dismiss
          </button>
        </div>
      )}

      {status && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3 animate-slide-up stagger-1">
          <Stat value={status.services.length} label="services" />
          <Stat value={status.running} label="running" />
          <Stat value={status.stopped} label="stopped" />
          <Stat value={status.protectedCount} label="untouchable" tone="var(--color-warning)" />
        </div>
      )}

      {status && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-center gap-2 mb-3 flex-wrap">
            <h3 className="text-[13px] font-semibold">Services</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">{rows.length}</span>
            <div className="ml-auto flex items-center gap-2">
              <div className="relative">
                <Search
                  size={13}
                  className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]"
                />
                <input
                  className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md pl-8 pr-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors w-48"
                  placeholder="Name or publisher"
                  value={query}
                  onChange={e => setQuery(e.target.value)}
                />
              </div>
              <select
                className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)]"
                value={filter}
                onChange={e => setFilter(e.target.value as Filter)}
              >
                <option value="all">everything</option>
                <option value="running">running</option>
                <option value="stopped">stopped</option>
                <option value="protected">untouchable</option>
                <option value="changed">changed by Novimize</option>
              </select>
            </div>
          </div>

          <div className="space-y-1 max-h-[32rem] overflow-y-auto pr-1">
            {rows.map(s => (
              <div
                key={s.name}
                className="rounded-lg border px-3 py-2"
                style={{
                  borderColor: s.protected ? 'rgba(245,158,11,0.35)' : 'var(--color-border)',
                }}
              >
                <div className="flex items-center gap-2 flex-wrap">
                  <span
                    className="w-2 h-2 rounded-full flex-shrink-0"
                    style={{
                      background:
                        s.status === 'Running' ? 'var(--color-success)' : 'var(--color-text-muted)',
                      opacity: s.status === 'Running' ? 1 : 0.35,
                    }}
                  />
                  <span className="text-[13px] font-medium truncate">{s.displayName || s.name}</span>
                  <span className="text-[11px] font-mono text-[var(--color-text-muted)]">
                    {s.name}
                  </span>
                  <span className="badge badge-optional">{s.startMode}</span>
                  {s.protected && (
                    <span className="badge badge-experimental flex items-center gap-1">
                      <Lock size={9} /> untouchable
                    </span>
                  )}
                  {s.originalStartMode && (
                    <span className="badge badge-recommended">was {s.originalStartMode}</span>
                  )}

                  <div className="ml-auto flex items-center gap-1">
                    {!s.protected && (
                      <>
                        <button
                          className="btn btn-ghost btn-sm"
                          onClick={() => void prepare(s, s.status === 'Running' ? 'stop' : 'start')}
                          disabled={busy !== null}
                        >
                          {s.status === 'Running' ? 'Stop' : 'Start'}
                        </button>
                        <button
                          className="btn btn-ghost btn-sm"
                          onClick={() => void prepare(s, 'restart')}
                          disabled={busy !== null || s.status !== 'Running'}
                          title="Stop and start again"
                        >
                          Restart
                        </button>
                        <select
                          className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-1.5 py-1 text-[11px] outline-none focus:border-[var(--color-primary)]"
                          value=""
                          disabled={busy !== null}
                          onChange={e => {
                            if (e.target.value) void prepare(s, e.target.value)
                          }}
                        >
                          <option value="">startup…</option>
                          {MODES.filter(m => m !== s.startMode.toLowerCase()).map(m => (
                            <option key={m} value={m}>{m}</option>
                          ))}
                        </select>
                        {s.originalStartMode && (
                          <button
                            className="btn btn-ghost btn-sm"
                            onClick={() => void prepare(s, 'restore')}
                            disabled={busy !== null}
                            title={`Put the start mode back to ${s.originalStartMode}`}
                          >
                            <RotateCcw size={13} />
                          </button>
                        )}
                      </>
                    )}
                  </div>
                </div>

                {s.protected && s.protectReason && (
                  <div className="text-[11px] text-[var(--color-warning)] mt-1">{s.protectReason}</div>
                )}
                {!s.protected && s.dependentOn.length > 0 && (
                  <div className="text-[11px] text-[var(--color-text-muted)] mt-1">
                    Required by {s.dependentOn.slice(0, 5).join(', ')}
                    {s.dependentOn.length > 5 && ` and ${s.dependentOn.length - 5} more`}
                  </div>
                )}
                {s.publisher && (
                  <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5 truncate">
                    {s.publisher}
                    {s.path && <span className="font-mono"> · {s.path}</span>}
                  </div>
                )}
              </div>
            ))}
            {rows.length === 0 && (
              <div className="text-[13px] text-[var(--color-text-muted)] py-4 text-center">
                Nothing matches.
              </div>
            )}
          </div>

          <p className="text-[11px] text-[var(--color-text-muted)] mt-3">
            No mass-disable exists here. Each service is changed on its own, protected ones are
            refused with the reason, and a service other services are waiting on names them before
            anything runs.
          </p>
        </div>
      )}

      {pending && preview && (
        <div className="modal-backdrop" onClick={() => setPending(null)}>
          <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
            <div className="flex items-start gap-3 mb-4">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
                style={{
                  background: preview.success ? 'rgba(59,130,246,0.15)' : 'rgba(239,68,68,0.15)',
                }}
              >
                <Settings2
                  size={19}
                  className={preview.success ? 'text-[var(--color-primary)]' : 'text-[var(--color-danger)]'}
                />
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">{preview.message}</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
                  {pending.service.name} · {pending.action}
                </p>
              </div>
              <button
                className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] p-1"
                onClick={() => setPending(null)}
              >
                ×
              </button>
            </div>

            <div className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-3 py-2.5 mb-4">
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                Commands
              </div>
              <div className="font-mono text-[11px] space-y-1 break-all">
                {preview.preview.map(line => <div key={line}>{line}</div>)}
              </div>
              {!preview.success && (
                <div className="text-[12px] text-[var(--color-danger)] mt-2">{preview.message}</div>
              )}
            </div>

            <div className="flex justify-end gap-2">
              <button className="btn btn-secondary" onClick={() => setPending(null)}>
                Cancel
              </button>
              <button
                className="btn btn-primary"
                onClick={() => void confirm()}
                disabled={busy !== null || !preview.success}
              >
                {busy !== null && <Loader2 size={14} className="animate-spin" />}
                Do it
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function Stat({ value, label, tone }: { value: number; label: string; tone?: string }) {
  return (
    <div className="rounded-lg bg-[var(--color-bg-elevated)] border border-[var(--color-border)] py-2.5 text-center">
      <div className="text-[18px] font-semibold leading-none" style={tone ? { color: tone } : undefined}>
        {value}
      </div>
      <div className="text-[10px] text-[var(--color-text-muted)] mt-1 uppercase tracking-wider">
        {label}
      </div>
    </div>
  )
}
