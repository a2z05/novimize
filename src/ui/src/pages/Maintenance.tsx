import { useCallback, useEffect, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  HardDrive,
  Loader2,
  RefreshCw,
  RotateCcw,
  Shield,
  Wrench,
} from 'lucide-react'
import type { MaintenanceChange, MaintenanceStatus, MaintenanceTool } from '../types'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

function size(bytes: number | null): string | null {
  if (bytes === null) return null
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`
  if (bytes >= 1024 ** 2) return `${(bytes / 1024 ** 2).toFixed(1)} MB`
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} kB`
  return `${bytes} B`
}

type Pending = MaintenanceTool

export default function Maintenance() {
  const [status, setStatus] = useState<MaintenanceStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<MaintenanceChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [preview, setPreview] = useState<MaintenanceChange | null>(null)

  const load = useCallback(
    async () => setStatus(await invokeJson<MaintenanceStatus>('maint_action', { action: 'status' })),
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

  async function prepare(tool: MaintenanceTool) {
    setPreview(null)
    const res = await run(`prep:${tool.id}`, () =>
      invokeJson<MaintenanceChange>('maint_action', { action: 'run', id: tool.id, confirm: false }),
    )
    if (!res) return
    setPreview(res)
    setPending(tool)
  }

  async function confirm() {
    if (!pending) return
    const id = pending.id
    setPending(null)
    const res = await run(`do:${id}`, () =>
      invokeJson<MaintenanceChange>('maint_action', { action: 'run', id, confirm: true }),
    )
    if (res) setChange(res)
    await load()
  }

  const clearable = status?.tools.filter(t => t.available && !t.repairs) ?? []
  const repairs = status?.tools.filter(t => t.repairs) ?? []
  const aside = status?.tools.filter(t => !t.available) ?? []

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Wrench size={22} className="text-[var(--color-primary)]" />
            Maintenance
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            Clear caches, check the image, repair what is broken — each with its size and its commands
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
            {change.restartRequired && (
              <div className="text-[12px] text-[var(--color-warning)] mt-1">
                A restart finishes this.
              </div>
            )}
            {change.needsElevation && (
              <div className="text-[12px] text-[var(--color-warning)] mt-1">
                Administrator rights are required, so nothing was changed.
              </div>
            )}
            {change.log && (
              <details className="mt-2">
                <summary className="text-[11px] text-[var(--color-text-muted)] cursor-pointer">
                  What it printed
                </summary>
                <pre className="text-[10px] font-mono whitespace-pre-wrap break-all text-[var(--color-text-muted)] mt-1 max-h-48 overflow-y-auto">
                  {change.log}
                </pre>
              </details>
            )}
          </div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setChange(null)}>
            Dismiss
          </button>
        </div>
      )}

      {status && (
        <div className="grid grid-cols-2 md:grid-cols-3 gap-3 animate-slide-up stagger-1">
          <Stat
            value={size(status.reclaimableBytes) ?? '—'}
            label="reclaimable"
            tone="var(--color-success)"
          />
          <Stat value={status.repairCount} label="rewrite system files" tone="var(--color-warning)" />
          <Stat value={status.restartCount} label="want a restart" />
        </div>
      )}

      <div className="card animate-slide-up stagger-2">
        <div className="flex items-center gap-2 mb-1">
          <HardDrive size={14} className="text-[var(--color-primary)]" />
          <h3 className="text-[13px] font-semibold">Clear</h3>
        </div>
        <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
          These delete files Windows regenerates. Nothing here changes a setting.
        </p>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-2">
          {clearable.map(t => (
            <div key={t.id} className="rounded-lg border border-[var(--color-border)] px-3 py-2.5">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-[13px] font-medium">{t.name}</span>
                {t.bytes !== null && (
                  <span className="badge badge-safe">{size(t.bytes)} now</span>
                )}
                {t.restartRequired && <span className="badge badge-experimental">restart</span>}
                <button
                  className="ml-auto btn btn-secondary btn-sm"
                  onClick={() => void prepare(t)}
                  disabled={busy !== null}
                >
                  {busy === `prep:${t.id}` || busy === `do:${t.id}` ? (
                    <Loader2 size={13} className="animate-spin" />
                  ) : (
                    <RotateCcw size={13} />
                  )}
                  Run…
                </button>
              </div>
              <div className="text-[12px] text-[var(--color-text-muted)] mt-1">{t.what}</div>
              {t.deletes && (
                <div className="text-[11px] font-mono text-[var(--color-warning)] mt-1 break-all">
                  deletes {t.deletes}
                </div>
              )}
              {t.measuredNote && (
                <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">{t.measuredNote}</div>
              )}
              <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">takes {t.effort}</div>
            </div>
          ))}
        </div>
      </div>

      <div className="card animate-slide-up stagger-3">
        <div className="flex items-center gap-2 mb-1">
          <Shield size={14} className="text-[var(--color-warning)]" />
          <h3 className="text-[13px] font-semibold">Repair</h3>
        </div>
        <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
          These rewrite system files. They need administrator rights, they take a while, and the
          result is printed rather than summarised.
        </p>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-2">
          {repairs.map(t => (
            <div
              key={t.id}
              className="rounded-lg border px-3 py-2.5"
              style={{ borderColor: 'rgba(245,158,11,0.35)' }}
            >
              <div className="flex items-center gap-2 flex-wrap">
                <span className="text-[13px] font-medium">{t.name}</span>
                <span className="badge badge-risky">rewrites system files</span>
                <button
                  className="ml-auto btn btn-secondary btn-sm"
                  onClick={() => void prepare(t)}
                  disabled={busy !== null}
                >
                  {busy === `prep:${t.id}` || busy === `do:${t.id}` ? (
                    <Loader2 size={13} className="animate-spin" />
                  ) : (
                    <Wrench size={13} />
                  )}
                  Run…
                </button>
              </div>
              <div className="text-[12px] text-[var(--color-text-muted)] mt-1">{t.what}</div>
              <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">
                takes {t.effort}
                {t.restartRequired && ' · a restart finishes this'}
              </div>
            </div>
          ))}
        </div>

        {aside.length > 0 && (
          <div className="mt-4 pt-3 border-t border-[var(--color-border)]">
            {aside.map(t => (
              <div key={t.id} className="text-[12px] text-[var(--color-text-muted)] py-0.5">
                <span className="font-medium">{t.name}</span> — {t.unavailableReason ?? t.what}
              </div>
            ))}
          </div>
        )}
      </div>

      {pending && preview && (
        <div className="modal-backdrop" onClick={() => setPending(null)}>
          <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
            <div className="flex items-start gap-3 mb-4">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
                style={{
                  background: pending.repairs ? 'rgba(245,158,11,0.15)' : 'rgba(59,130,246,0.15)',
                }}
              >
                {pending.repairs ? (
                  <AlertTriangle size={19} className="text-[var(--color-warning)]" />
                ) : (
                  <Wrench size={19} className="text-[var(--color-primary)]" />
                )}
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">{preview.message}</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
                  takes {pending.effort}
                  {pending.restartRequired && ' · a restart finishes this'}
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

            {pending.restartRequired && (
              <div className="flex items-start gap-2 rounded-lg bg-amber-500/10 border border-amber-500/25 px-3 py-2.5 mb-4">
                <AlertTriangle size={15} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />
                <div className="text-[12px] text-[var(--color-warning)]">
                  A restart finishes this.
                </div>
              </div>
            )}

            <div className="flex justify-end gap-2">
              <button className="btn btn-secondary" onClick={() => setPending(null)}>
                Cancel
              </button>
              <button
                className={pending.repairs ? 'btn btn-danger' : 'btn btn-primary'}
                onClick={() => void confirm()}
                disabled={busy !== null}
              >
                {busy !== null && <Loader2 size={14} className="animate-spin" />}
                Run it
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function Stat({ value, label, tone }: { value: number | string; label: string; tone?: string }) {
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
