import { useCallback, useEffect, useMemo, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  ChevronRight,
  FolderOpen,
  Loader2,
  Power,
  RefreshCw,
  Search,
  ToggleLeft,
  ToggleRight,
} from 'lucide-react'
import type { StartupChange, StartupItem, StartupKind, StartupStatus } from '../types'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

const KIND_LABEL: Record<StartupKind, string> = {
  Run: 'Registry (Run)',
  StartupFolder: 'Startup folder (you)',
  CommonStartupFolder: 'Startup folder (everyone)',
  ScheduledTask: 'Scheduled task at logon',
  StartupTask: 'Store app startup task',
}

const KIND_ORDER: StartupKind[] = [
  'Run',
  'StartupFolder',
  'CommonStartupFolder',
  'ScheduledTask',
  'StartupTask',
]

function impactBadge(impact: string): string {
  switch (impact) {
    case 'Broken': return 'badge badge-dangerous'
    case 'Heavy': return 'badge badge-risky'
    case 'Medium': return 'badge badge-experimental'
    case 'Low': return 'badge badge-safe'
    default: return 'badge badge-optional'
  }
}

type Pending = { item: StartupItem; enabled: boolean }

export default function Startup() {
  const [status, setStatus] = useState<StartupStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<StartupChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [filter, setFilter] = useState('')
  const [only, setOnly] = useState<'all' | 'enabled' | 'disabled' | 'broken'>('all')

  const load = useCallback(
    async () => setStatus(await invokeJson<StartupStatus>('startup_action', { action: 'status' })),
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
    const items = status?.items ?? []
    const q = filter.trim().toLowerCase()
    return items.filter(i => {
      if (only === 'enabled' && !i.enabled) return false
      if (only === 'disabled' && i.enabled) return false
      if (only === 'broken' && !i.broken) return false
      if (!q) return true
      return (
        i.name.toLowerCase().includes(q) ||
        i.command.toLowerCase().includes(q) ||
        (i.publisher ?? '').toLowerCase().includes(q)
      )
    })
  }, [status, filter, only])

  async function flip(item: StartupItem, enabled: boolean) {
    const res = await run(`${enabled ? 'on' : 'off'}:${item.id}`, () =>
      invokeJson<StartupChange>('startup_action', {
        action: enabled ? 'enable' : 'disable',
        id: item.id,
        confirm: true,
      }),
    )
    if (res) setChange(res)
    await load()
  }

  async function open(item: StartupItem) {
    const res = await run(`open:${item.id}`, () =>
      invokeJson<StartupChange>('startup_action', { action: 'open', id: item.id }),
    )
    if (res) setChange(res)
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Power size={22} className="text-[var(--color-primary)]" />
            Startup
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            Everything that runs when you sign in — turned off, never deleted
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
          <Stat value={status.items.length} label="entries" />
          <Stat value={status.enabledCount} label="enabled" />
          <Stat value={status.disabledCount} label="disabled" />
          <Stat value={status.brokenCount} label="broken" danger={status.brokenCount > 0} />
        </div>
      )}

      {status && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-center gap-2 mb-3 flex-wrap">
            <FolderOpen size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Where these live</h3>
          </div>
          <div className="text-[11px] font-mono text-[var(--color-text-muted)] space-y-0.5 break-all">
            <div>{status.userStartupFolder}</div>
            <div>{status.commonStartupFolder}</div>
          </div>
          <p className="text-[12px] text-[var(--color-text-muted)] mt-2 leading-relaxed">
            Disabling writes the <span className="font-mono">StartupApproved</span> flag — the same
            byte Task Manager writes — so restoring is writing it back. A Run key is never edited and
            nothing is ever removed: a program that expects to find itself at logon does not fail
            quietly when it is gone, it just stops working one day.
          </p>
        </div>
      )}

      {status && (
        <div className="card animate-slide-up stagger-3">
          <div className="flex items-center gap-2 mb-3 flex-wrap">
            <h3 className="text-[13px] font-semibold">Entries</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">{rows.length}</span>
            <div className="ml-auto flex items-center gap-2">
              <div className="relative">
                <Search
                  size={13}
                  className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]"
                />
                <input
                  className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md pl-8 pr-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors w-44"
                  placeholder="Filter"
                  value={filter}
                  onChange={e => setFilter(e.target.value)}
                />
              </div>
              <select
                className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md px-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)]"
                value={only}
                onChange={e => setOnly(e.target.value as typeof only)}
              >
                <option value="all">everything</option>
                <option value="enabled">enabled</option>
                <option value="disabled">disabled</option>
                <option value="broken">broken</option>
              </select>
            </div>
          </div>

          {KIND_ORDER.filter(kind => rows.some(r => r.kind === kind)).map(kind => (
            <div key={kind} className="mb-4 last:mb-0">
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1.5">
                {KIND_LABEL[kind]}
              </div>
              <div className="space-y-1">
                {rows
                  .filter(r => r.kind === kind)
                  .map(item => (
                    <div
                      key={item.id}
                      className="rounded-lg border border-[var(--color-border)] px-3 py-2"
                      style={item.broken ? { borderColor: 'rgba(239,68,68,0.4)' } : undefined}
                    >
                      <div className="flex items-center gap-2 flex-wrap">
                        <button
                          className="btn btn-ghost btn-sm px-1.5 flex-shrink-0"
                          onClick={() => void setPending({ item, enabled: !item.enabled })}
                          disabled={busy !== null || !item.writable}
                          title={
                            item.writable
                              ? item.enabled
                                ? 'Stop this running at sign-in'
                                : 'Let this run at sign-in again'
                              : 'Registered for every user — needs administrator rights'
                          }
                        >
                          {item.enabled ? <ToggleRight size={16} className="text-[var(--color-success)]" /> : <ToggleLeft size={16} className="text-[var(--color-text-muted)]" />}
                        </button>
                        <span
                          className="text-[13px] font-medium truncate"
                          style={{ opacity: item.enabled ? 1 : 0.55 }}
                          title={item.name}
                        >
                          {item.name}
                        </span>
                        {item.publisher && (
                          <span className="text-[11px] text-[var(--color-text-muted)] truncate">
                            {item.publisher}
                          </span>
                        )}
                        <span className={`${impactBadge(item.impact)} flex-shrink-0`}>
                          {item.impact}
                        </span>
                        {!item.writable && <span className="badge badge-experimental">admin</span>}
                        <div className="ml-auto flex gap-1">
                          <button
                            className="btn btn-ghost btn-sm"
                            onClick={() => void open(item)}
                            disabled={busy !== null}
                            title="Show it in Explorer"
                          >
                            <FolderOpen size={13} />
                          </button>
                        </div>
                      </div>
                      <div className="text-[11px] font-mono text-[var(--color-text-muted)] mt-1 break-all">
                        {item.command}
                      </div>
                      <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">
                        {item.impactReason}
                      </div>
                    </div>
                  ))}
              </div>
            </div>
          ))}

          {rows.length === 0 && (
            <div className="text-[13px] text-[var(--color-text-muted)] py-4 text-center">
              Nothing matches.
            </div>
          )}
        </div>
      )}

      {pending && (
        <div className="modal-backdrop" onClick={() => setPending(null)}>
          <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
            <div className="flex items-start gap-3 mb-4">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
                style={{
                  background: pending.enabled ? 'rgba(34,197,94,0.15)' : 'rgba(59,130,246,0.15)',
                }}
              >
                <Power size={19} className="text-[var(--color-primary)]" />
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">
                  {pending.enabled ? 'Let this run at sign-in?' : 'Stop this running at sign-in?'}
                </h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
                  Nothing is deleted. The entry stays exactly where it is.
                </p>
              </div>
              <button
                className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] p-1"
                onClick={() => setPending(null)}
              >
                ×
              </button>
            </div>

            <div className="rounded-lg border border-[var(--color-border)] bg-[var(--color-bg-elevated)] px-3 py-2.5 mb-4 space-y-2">
              <div>
                <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)]">
                  Entry
                </div>
                <div className="text-[13px] font-medium">{pending.item.name}</div>
                <div className="text-[11px] font-mono text-[var(--color-text-muted)] break-all">
                  {pending.item.command}
                </div>
              </div>
              <div>
                <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)]">
                  What is written
                </div>
                <div className="font-mono text-[11px] break-all">
                  {pending.item.kind === 'ScheduledTask'
                    ? `${pending.enabled ? 'Enable' : 'Disable'}-ScheduledTask -TaskName '${pending.item.name}' -TaskPath '${pending.item.taskPath ?? '\\'}'`
                    : `StartupApproved\\${pending.item.kind === 'Run' ? 'Run' : 'StartupFolder'}[${pending.item.name}] = ${pending.enabled ? '0x02 (enabled)' : '0x03 (disabled)'}`}
                </div>
              </div>
              <div className="text-[11px] text-[var(--color-text-muted)]">{pending.item.impactReason}</div>
            </div>

            <div className="flex justify-end gap-2">
              <button className="btn btn-secondary" onClick={() => setPending(null)}>
                Cancel
              </button>
              <button
                className="btn btn-primary"
                onClick={() => {
                  const p = pending
                  setPending(null)
                  void flip(p.item, p.enabled)
                }}
                disabled={busy !== null}
              >
                {busy !== null && <Loader2 size={14} className="animate-spin" />}
                <ChevronRight size={14} />
                {pending.enabled ? 'Enable it' : 'Disable it'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function Stat({ value, label, danger }: { value: number; label: string; danger?: boolean }) {
  return (
    <div className="rounded-lg bg-[var(--color-bg-elevated)] border border-[var(--color-border)] py-2.5 text-center">
      <div
        className="text-[18px] font-semibold leading-none"
        style={danger ? { color: 'var(--color-danger)' } : undefined}
      >
        {value}
      </div>
      <div className="text-[10px] text-[var(--color-text-muted)] mt-1 uppercase tracking-wider">
        {label}
      </div>
    </div>
  )
}
