import { useCallback, useEffect, useMemo, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Clock,
  Loader2,
  Play,
  RefreshCw,
  RotateCcw,
  Search,
  Timer,
} from 'lucide-react'
import type { ScheduledTaskChange, ScheduledTaskEntry, ScheduledTaskStatus } from '../types'

function errMsg(err: unknown): string {
  // §29: never a bare "something went wrong". The string branch is the
  // common one — the CLI already names the action in what it says — and the
  // other two have to say what happened and where it happened.
  if (typeof err === 'string') return err
  if (err instanceof Error) return `${err.name}: ${err.message}`
  return 'Cause: the command returned a result this version cannot read. Affected component: this page.'
}

function when(iso: string | null): string {
  if (!iso) return 'never'
  const d = new Date(iso)
  return Number.isNaN(d.getTime())
    ? 'never'
    : d.toLocaleString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      })
}

/** The last run's HRESULT, which is the only thing that says why it failed. */
function result(code: number): string {
  if (code === 0) return '0x00000000 ok'
  if (code === 267011) return '0x00041300 not run yet'
  if (code === 267009) return '0x000413FE currently running'
  return `0x${(code >>> 0).toString(16).toUpperCase().padStart(8, '0')}`
}

type Filter = 'all' | 'enabled' | 'disabled' | 'changed' | 'mine'
type Pending = { task: ScheduledTaskEntry; action: string }

export default function Tasks() {
  const [status, setStatus] = useState<ScheduledTaskStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<ScheduledTaskChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [preview, setPreview] = useState<ScheduledTaskChange | null>(null)
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [open, setOpen] = useState<string | null>(null)

  const load = useCallback(
    async () => setStatus(await invokeJson<ScheduledTaskStatus>('tasks_action', { action: 'status' })),
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
    return (status?.tasks ?? []).filter(t => {
      if (filter === 'enabled' && !t.enabled) return false
      if (filter === 'disabled' && t.enabled) return false
      if (filter === 'changed' && !t.changedByNovimize) return false
      if (filter === 'mine' && t.systemTask) return false
      if (!q) return true
      return (
        t.name.toLowerCase().includes(q) ||
        t.path.toLowerCase().includes(q) ||
        t.command.toLowerCase().includes(q)
      )
    })
  }, [status, query, filter])

  async function prepare(task: ScheduledTaskEntry, action: string) {
    setPreview(null)
    const res = await run(`prep:${task.id}:${action}`, () =>
      invokeJson<ScheduledTaskChange>('tasks_action', { action, id: task.id, confirm: false }),
    )
    if (!res) return
    setPreview(res)
    setPending({ task, action })
  }

  async function confirm() {
    if (!pending) return
    const p = pending
    setPending(null)
    const res = await run(`do:${p.task.id}`, () =>
      invokeJson<ScheduledTaskChange>('tasks_action', {
        action: p.action,
        id: p.task.id,
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
            <Timer size={22} className="text-[var(--color-primary)]" />
            Scheduled Tasks
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            What Windows runs on its own schedule — disabled, never deleted
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
          <Stat value={status.tasks.length} label="tasks" />
          <Stat value={status.enabled} label="enabled" />
          <Stat value={status.disabled} label="disabled" />
          <Stat value={status.changedByNovimize} label="changed by us" tone="var(--color-primary)" />
        </div>
      )}

      {status && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-center gap-2 mb-3 flex-wrap">
            <h3 className="text-[13px] font-semibold">Tasks</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">{rows.length}</span>
            <div className="ml-auto flex items-center gap-2">
              <div className="relative">
                <Search
                  size={13}
                  className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]"
                />
                <input
                  className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md pl-8 pr-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors w-48"
                  placeholder="Name, path or command"
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
                <option value="enabled">enabled</option>
                <option value="disabled">disabled</option>
                <option value="changed">changed by Novimize</option>
                <option value="mine">not Microsoft's</option>
              </select>
            </div>
          </div>

          <div className="space-y-1 max-h-[34rem] overflow-y-auto pr-1">
            {rows.map(t => {
              const expanded = open === t.id
              return (
                <div
                  key={t.id}
                  className="rounded-lg border px-3 py-2"
                  style={{
                    borderColor: t.changedByNovimize
                      ? 'rgba(99,102,241,0.4)'
                      : 'var(--color-border)',
                  }}
                >
                  <div className="flex items-center gap-2 flex-wrap">
                    <button
                      className="btn btn-ghost btn-sm px-1 flex-shrink-0"
                      onClick={() => setOpen(expanded ? null : t.id)}
                      title="Show what it runs and when"
                    >
                      {expanded ? <ChevronDown size={13} /> : <ChevronRight size={13} />}
                    </button>
                    <span
                      className="w-2 h-2 rounded-full flex-shrink-0"
                      style={{
                        background: t.enabled ? 'var(--color-success)' : 'var(--color-text-muted)',
                        opacity: t.enabled ? 1 : 0.35,
                      }}
                    />
                    <span className="text-[13px] font-medium truncate">{t.name}</span>
                    <span className="text-[11px] text-[var(--color-text-muted)] truncate">
                      {t.trigger}
                    </span>
                    {t.systemTask && <span className="badge badge-optional">Microsoft</span>}
                    {t.changedByNovimize && (
                      <span className="badge badge-recommended">
                        was {t.originalEnabled ? 'enabled' : 'disabled'}
                      </span>
                    )}

                    <div className="ml-auto flex items-center gap-1">
                      <button
                        className="btn btn-ghost btn-sm"
                        onClick={() => void prepare(t, t.enabled ? 'disable' : 'enable')}
                        disabled={busy !== null}
                      >
                        {t.enabled ? 'Disable' : 'Enable'}
                      </button>
                      <button
                        className="btn btn-ghost btn-sm"
                        onClick={() => void prepare(t, 'run')}
                        disabled={busy !== null}
                        title="Start it now, outside its schedule"
                      >
                        <Play size={12} />
                        Run
                      </button>
                      {t.changedByNovimize && (
                        <button
                          className="btn btn-ghost btn-sm"
                          onClick={() => void prepare(t, 'restore')}
                          disabled={busy !== null}
                          title="Put it back the way it was"
                        >
                          <RotateCcw size={13} />
                        </button>
                      )}
                    </div>
                  </div>

                  <div className="text-[11px] font-mono text-[var(--color-text-muted)] mt-1 break-all">
                    {t.path}
                  </div>

                  <div className="flex flex-wrap gap-x-4 gap-y-0.5 text-[11px] text-[var(--color-text-muted)] mt-1">
                    <span className="flex items-center gap-1">
                      <Clock size={11} /> last {when(t.lastRun)} · {result(t.lastResult)}
                    </span>
                    <span>next {when(t.nextRun)}</span>
                    <span>state {t.state}</span>
                  </div>

                  {expanded && (
                    <div className="mt-2 rounded-md bg-[var(--color-bg-elevated)] border border-[var(--color-border)] px-3 py-2 space-y-1 text-[11px]">
                      <div className="font-mono break-all">
                        {(t.command || 'no command') + (t.arguments ? ' ' + t.arguments : '')}
                      </div>
                      {t.workingDirectory && (
                        <div className="font-mono break-all text-[var(--color-text-muted)]">
                          in {t.workingDirectory}
                        </div>
                      )}
                      {t.author && <div>author {t.author}</div>}
                      {t.description && (
                        <div className="text-[var(--color-text-muted)]">{t.description}</div>
                      )}
                    </div>
                  )}
                </div>
              )
            })}
            {rows.length === 0 && (
              <div className="text-[13px] text-[var(--color-text-muted)] py-4 text-center">
                Nothing matches.
              </div>
            )}
          </div>

          <p className="text-[11px] text-[var(--color-text-muted)] mt-3">
            Nothing here is deleted. Disabling a task leaves it registered and turns it back on with
            one click; the state it had before Novimize touched it is remembered so Restore means the
            state the machine was actually in.
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
                <Timer
                  size={19}
                  className={preview.success ? 'text-[var(--color-primary)]' : 'text-[var(--color-danger)]'}
                />
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">{preview.message}</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5 break-all">
                  {pending.task.path}
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
