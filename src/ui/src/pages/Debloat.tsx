import { useCallback, useEffect, useMemo, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  Download,
  Loader2,
  Lock,
  PackageX,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldAlert,
  Trash2,
} from 'lucide-react'
import type { DebloatChange, DebloatPackage, DebloatStatus, DebloatVerdict } from '../types'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

const VERDICT_ORDER: DebloatVerdict[] = ['Safe', 'Keep', 'Unknown', 'Protected'] as DebloatVerdict[]

const VERDICT_LABEL: Record<string, string> = {
  Safe: 'safe to remove',
  Keep: 'removable, but noticed',
  Protected: 'not offered',
  Unknown: 'nobody has said',
}

function verdictBadge(v: string): string {
  switch (v) {
    case 'Safe': return 'badge badge-safe'
    case 'Keep': return 'badge badge-experimental'
    case 'Protected': return 'badge badge-risky'
    default: return 'badge badge-optional'
  }
}

type Filter = 'all' | 'removable' | 'refused'
type Pending = { pkg: DebloatPackage; action: 'remove' | 'restore'; allUsers: boolean }

export default function Debloat() {
  const [status, setStatus] = useState<DebloatStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<DebloatChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [preview, setPreview] = useState<DebloatChange | null>(null)
  const [query, setQuery] = useState('')
  const [filter, setFilter] = useState<Filter>('all')
  const [allUsers, setAllUsers] = useState(false)

  const load = useCallback(
    async () =>
      setStatus(await invokeJson<DebloatStatus>('debloat_action', { action: 'status', allUsers })),
    [allUsers],
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
    return (status?.packages ?? []).filter(p => {
      if (filter === 'removable' && !p.removable) return false
      if (filter === 'refused' && p.removable) return false
      if (!q) return true
      return (
        p.name.toLowerCase().includes(q) ||
        p.publisher.toLowerCase().includes(q)
      )
    })
  }, [status, query, filter])

  const grouped = useMemo(
    () =>
      VERDICT_ORDER.map(v => ({
        verdict: v,
        items: rows.filter(r => r.verdict === v),
      })).filter(g => g.items.length > 0),
    [rows],
  )

  async function prepare(pkg: DebloatPackage, action: 'remove' | 'restore', forAll: boolean) {
    setPreview(null)
    const res = await run(`prep:${pkg.name}:${action}`, () =>
      invokeJson<DebloatChange>('debloat_action', {
        action,
        name: pkg.name,
        allUsers: forAll,
        confirm: false,
      }),
    )
    if (!res) return
    setPreview(res)
    setPending({ pkg, action, allUsers: forAll })
  }

  async function confirm() {
    if (!pending) return
    const p = pending
    setPending(null)
    const res = await run(`do:${p.pkg.name}`, () =>
      invokeJson<DebloatChange>('debloat_action', {
        action: p.action,
        name: p.pkg.name,
        allUsers: p.allUsers,
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
            <PackageX size={22} className="text-[var(--color-warning)]" />
            Debloat
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            Installed Store packages, what is safe to remove, and how it comes back
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
            {change.restartRequired && (
              <div className="text-[12px] text-[var(--color-warning)] mt-1">
                A restart finishes this.
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
          <Stat value={status.packages.length} label="packages" />
          <Stat value={status.removable} label="removable" tone="var(--color-success)" />
          <Stat value={status.protectedCount} label="refused" tone="var(--color-warning)" />
          <Stat value={status.frameworks} label="frameworks" />
        </div>
      )}

      {status && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-start gap-2">
            <ShieldAlert size={14} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />
            <div className="text-[12px] text-[var(--color-text-muted)] leading-relaxed">
              Frameworks, packages Windows marks non-removable, and anything another package is
              waiting on are refused by the engine — that is a fact about this machine. The verdicts
              below are an opinion, held in{' '}
              <span className="font-mono break-all">{status.policyPath}</span> ({status.policyEntries}{' '}
              entries) and editable there. Neither can override the other.
            </div>
          </div>
          <label className="flex items-center gap-2 mt-3 text-[12px]">
            <input
              type="checkbox"
              checked={allUsers}
              onChange={e => setAllUsers(e.target.checked)}
            />
            Act for every user, not just this one
            <span className="text-[var(--color-text-muted)]">
              — removing for everyone needs administrator rights
            </span>
          </label>
        </div>
      )}

      {status && (
        <div className="card animate-slide-up stagger-3">
          <div className="flex items-center gap-2 mb-3 flex-wrap">
            <h3 className="text-[13px] font-semibold">Packages</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">{rows.length}</span>
            <div className="ml-auto flex items-center gap-2">
              <div className="relative">
                <Search
                  size={13}
                  className="absolute left-2.5 top-1/2 -translate-y-1/2 text-[var(--color-text-muted)]"
                />
                <input
                  className="bg-[var(--color-bg-elevated)] border border-[var(--color-border)] rounded-md pl-8 pr-2 py-1.5 text-[12px] outline-none focus:border-[var(--color-primary)] transition-colors w-48"
                  placeholder="Package or publisher"
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
                <option value="removable">removable</option>
                <option value="refused">refused</option>
              </select>
            </div>
          </div>

          {grouped.map(group => (
            <div key={group.verdict} className="mb-4 last:mb-0">
              <div className="flex items-center gap-2 mb-1.5">
                <span className={verdictBadge(group.verdict)}>{group.verdict}</span>
                <span className="text-[11px] text-[var(--color-text-muted)]">
                  {VERDICT_LABEL[group.verdict] ?? group.verdict} · {group.items.length}
                </span>
              </div>

              <div className="space-y-1 max-h-80 overflow-y-auto pr-1">
                {group.items.map(p => (
                  <div key={p.fullName || p.name} className="rounded-lg border border-[var(--color-border)] px-3 py-2">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-[13px] font-medium truncate">{p.name}</span>
                      <span className="text-[11px] text-[var(--color-text-muted)]">{p.version}</span>
                      <span className="badge badge-optional">{p.scope}</span>
                      {p.provisioned && <span className="badge badge-safe">provisioned</span>}

                      <div className="ml-auto flex items-center gap-1">
                        {p.removable ? (
                          <button
                            className="btn btn-ghost btn-sm"
                            onClick={() => void prepare(p, 'remove', allUsers)}
                            disabled={busy !== null}
                          >
                            {busy === `prep:${p.name}:remove` ? (
                              <Loader2 size={13} className="animate-spin" />
                            ) : (
                              <Trash2 size={13} />
                            )}
                            Remove…
                          </button>
                        ) : (
                          <span className="flex items-center gap-1 text-[11px] text-[var(--color-warning)]">
                            <Lock size={11} /> refused
                          </span>
                        )}
                        <button
                          className="btn btn-ghost btn-sm"
                          onClick={() => void prepare(p, 'restore', allUsers)}
                          disabled={busy !== null}
                          title="Register the package again from its install location"
                        >
                          {busy === `prep:${p.name}:restore` ? (
                            <Loader2 size={13} className="animate-spin" />
                          ) : (
                            <RotateCcw size={13} />
                          )}
                        </button>
                      </div>
                    </div>

                    {(p.refusalReason || p.reason) && (
                      <div
                        className="text-[11px] mt-0.5"
                        style={{
                          color: p.refusalReason ? 'var(--color-warning)' : 'var(--color-text-muted)',
                        }}
                      >
                        {p.refusalReason ?? p.reason}
                      </div>
                    )}
                    {p.dependedOnBy.length > 0 && (
                      <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">
                        required by {p.dependedOnBy.slice(0, 4).join(', ')}
                        {p.dependedOnBy.length > 4 && ` and ${p.dependedOnBy.length - 4} more`}
                      </div>
                    )}
                    <div className="text-[11px] text-[var(--color-text-muted)] truncate">
                      {p.publisher}
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
                {preview.success ? (
                  <Download size={19} className="text-[var(--color-primary)]" />
                ) : (
                  <AlertTriangle size={19} className="text-[var(--color-danger)]" />
                )}
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">{preview.message}</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5 break-all">
                  {pending.pkg.name}
                  {pending.pkg.publisher && ` · ${pending.pkg.publisher}`}
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
                  Commands
                </div>
                <div className="font-mono text-[11px] space-y-1 break-all">
                  {preview.preview.map(line => <div key={line}>{line}</div>)}
                </div>
              </div>
              <div className="text-[11px] text-[var(--color-text-muted)]">
                {pending.pkg.provisioned
                  ? 'A provisioned copy exists, so this can be registered again without a download.'
                  : 'A provisioned copy could not be confirmed — reading those needs administrator rights.'}
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
                {pending.action === 'remove' ? 'Remove it' : 'Put it back'}
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
