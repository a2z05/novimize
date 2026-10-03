import { useCallback, useEffect, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  CheckCircle2,
  Clock,
  Download,
  ExternalLink,
  Loader2,
  RefreshCw,
  RotateCcw,
  Search,
  ShieldCheck,
} from 'lucide-react'
import type { WindowsUpdateChange, WindowsUpdateStatus } from '../types'

function errMsg(err: unknown): string {
  if (typeof err === 'string') return err
  if (err instanceof Error) return err.message
  return 'Something went wrong.'
}

function when(iso: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime())
    ? '—'
    : d.toLocaleString(undefined, {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      })
}

function stateTone(state: string): string {
  if (state === 'Pending reboot') return 'var(--color-warning)'
  if (state === 'Installed') return 'var(--color-success)'
  if (state === 'Service stopped') return 'var(--color-danger)'
  return 'var(--color-text-muted)'
}

type Pending = 'restart'

export default function Updates() {
  const [status, setStatus] = useState<WindowsUpdateStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<WindowsUpdateChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [preview, setPreview] = useState<WindowsUpdateChange | null>(null)

  const load = useCallback(
    async () => setStatus(await invokeJson<WindowsUpdateStatus>('update_action', { action: 'status' })),
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

  async function scan() {
    const res = await run('scan', () =>
      invokeJson<WindowsUpdateStatus>('update_action', { action: 'scan' }),
    )
    if (res) {
      setStatus(res)
      setChange({
        action: 'scan',
        success: res.error === null,
        unchanged: false,
        message: res.error
          ? `The search reported: ${res.error}`
          : `${res.available.length} update(s) are waiting.`,
        affected: res.available.length,
        needsElevation: false,
        log: null,
        preview: [],
        restartRequired: false,
      })
    }
  }

  async function openSettings() {
    const res = await run('open', () =>
      invokeJson<WindowsUpdateChange>('update_action', { action: 'open' }),
    )
    if (res) setChange(res)
  }

  async function prepareRestart() {
    setPreview(null)
    const res = await run('prep:restart', () =>
      invokeJson<WindowsUpdateChange>('update_action', { action: 'restart', confirm: false }),
    )
    if (!res) return
    setPreview(res)
    setPending('restart')
  }

  async function confirmRestart() {
    setPending(null)
    const res = await run('restart', () =>
      invokeJson<WindowsUpdateChange>('update_action', { action: 'restart', confirm: true }),
    )
    if (res) setChange(res)
    await load()
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <ShieldCheck size={22} className="text-[var(--color-primary)]" />
            Windows Update
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            Where it stands, what is owed, and how to finish it — never how to switch it off
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
          <div className="text-[13px] break-words flex-1">{change.message}</div>
          <button className="btn btn-ghost btn-sm flex-shrink-0" onClick={() => setChange(null)}>
            Dismiss
          </button>
        </div>
      )}

      {status && (
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3 animate-slide-up stagger-1">
          <div className="card">
            <div className="text-[11px] uppercase tracking-wider text-[var(--color-text-muted)]">
              State
            </div>
            <div className="text-[17px] font-semibold" style={{ color: stateTone(status.state) }}>
              {status.state}
            </div>
            <div className="text-[11px] text-[var(--color-text-muted)] mt-1">
              {status.updateServiceRunning ? 'update service running' : 'update service stopped'}
            </div>
          </div>
          <div className="card">
            <div className="text-[11px] uppercase tracking-wider text-[var(--color-text-muted)]">
              Last search
            </div>
            <div className="text-[13px] font-medium">{when(status.lastSearchSuccess)}</div>
            <div className="text-[11px] text-[var(--color-text-muted)] mt-1">
              installed {when(status.lastInstallSuccess)}
            </div>
          </div>
          <div className="card">
            <div className="text-[11px] uppercase tracking-wider text-[var(--color-text-muted)]">
              Last boot
            </div>
            <div className="text-[13px] font-medium">{when(status.lastBoot)}</div>
          </div>
          <div className="card" style={status.pendingReboot ? { borderColor: 'rgba(245,158,11,0.5)' } : undefined}>
            <div className="text-[11px] uppercase tracking-wider text-[var(--color-text-muted)]">
              Restart
            </div>
            <div
              className="text-[17px] font-semibold"
              style={{ color: status.pendingReboot ? 'var(--color-warning)' : 'var(--color-success)' }}
            >
              {status.pendingReboot ? 'owed' : 'not owed'}
            </div>
            <div className="text-[11px] text-[var(--color-text-muted)] mt-1">
              {status.pendingReboot
                ? `${status.pendingRebootReasons.length} reason(s)`
                : 'nothing is waiting on one'}
            </div>
          </div>
        </div>
      )}

      {status?.pendingReboot && (
        <div className="card animate-slide-up stagger-2" style={{ borderColor: 'rgba(245,158,11,0.5)' }}>
          <div className="flex items-start gap-3">
            <Clock size={16} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />
            <div className="min-w-0 flex-1">
              <div className="text-[13px] font-semibold">This machine owes a restart</div>
              <ul className="mt-1 space-y-0.5">
                {status.pendingRebootReasons.map(r => (
                  <li key={r} className="text-[12px] text-[var(--color-text-muted)]">
                    · {r}
                  </li>
                ))}
              </ul>
            </div>
            <button
              className="btn btn-secondary btn-sm flex-shrink-0"
              onClick={() => void prepareRestart()}
              disabled={busy !== null}
            >
              {busy === 'prep:restart' ? <Loader2 size={13} className="animate-spin" /> : <RotateCcw size={13} />}
              Restart…
            </button>
          </div>
        </div>
      )}

      <div className="card animate-slide-up stagger-3">
        <div className="flex items-center gap-2 mb-3 flex-wrap">
          <Search size={14} className="text-[var(--color-primary)]" />
          <h3 className="text-[13px] font-semibold">Check for updates</h3>
          <div className="ml-auto flex gap-2">
            <button
              className="btn btn-secondary btn-sm"
              onClick={() => void scan()}
              disabled={busy !== null}
              title="Asks Microsoft what is waiting — a network round trip that can take a minute"
            >
              {busy === 'scan' ? <Loader2 size={13} className="animate-spin" /> : <Download size={13} />}
              Scan
            </button>
            <button
              className="btn btn-secondary btn-sm"
              onClick={() => void openSettings()}
              disabled={busy !== null}
            >
              <ExternalLink size={13} />
              Open Settings
            </button>
          </div>
        </div>

        <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
          A scan talks to Microsoft, so it is a button rather than something that happens when the
          page loads. Novimize never installs or hides an update — that is Windows' job, and its own
          dialog.
        </p>

        {status?.scanned && (
          <div className="rounded-lg border border-[var(--color-border)] px-3 py-2 mb-3">
            <div className="text-[13px] font-medium">
              {status.available.length} update(s) waiting
            </div>
            {status.scanNote && (
              <div className="text-[11px] text-[var(--color-warning)] mt-0.5">{status.scanNote}</div>
            )}
            <div className="mt-2 space-y-1 max-h-56 overflow-y-auto">
              {status.available.map(u => (
                <div key={u.id || u.title} className="text-[12px] flex items-start gap-2">
                  <span className="flex-1 min-w-0 break-words">{u.title}</span>
                  {u.sizeBytes !== null && (
                    <span className="text-[11px] text-[var(--color-text-muted)] flex-shrink-0">
                      {(u.sizeBytes / 1024 ** 2).toFixed(1)} MB
                    </span>
                  )}
                </div>
              ))}
              {status.available.length === 0 && (
                <div className="text-[12px] text-[var(--color-text-muted)]">
                  Nothing is waiting.
                </div>
              )}
            </div>
          </div>
        )}
      </div>

      {status && status.history.length > 0 && (
        <div className="card animate-slide-up stagger-4">
          <div className="flex items-center gap-2 mb-3">
            <RotateCcw size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">History</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">
              {status.history.length} most recent
            </span>
          </div>

          <div className="space-y-1 max-h-80 overflow-y-auto pr-1">
            {status.history.map((h, i) => (
              <div key={`${h.title}-${i}`} className="flex items-start gap-2 py-1">
                <span
                  className="w-2 h-2 rounded-full flex-shrink-0 mt-1"
                  style={{
                    background: h.succeeded ? 'var(--color-success)' : 'var(--color-danger)',
                  }}
                />
                <span className="text-[12px] flex-1 min-w-0 break-words">{h.title}</span>
                <span className="text-[11px] text-[var(--color-text-muted)] flex-shrink-0">
                  {h.when ? new Date(h.when).toLocaleDateString() : '—'}
                </span>
              </div>
            ))}
          </div>

          <p className="text-[11px] text-[var(--color-text-muted)] mt-3">
            Red means Windows reported a failure for that entry — including updates that failed and
            succeeded later, which is why the same title can appear more than once.
          </p>
        </div>
      )}

      {pending && preview && (
        <div className="modal-backdrop" onClick={() => setPending(null)}>
          <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
            <div className="flex items-start gap-3 mb-4">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
                style={{ background: 'rgba(245,158,11,0.15)' }}
              >
                <AlertTriangle size={19} className="text-[var(--color-warning)]" />
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">{preview.message}</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
                  The restart is in 60 seconds, not immediately.
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
                className="btn btn-danger"
                onClick={() => void confirmRestart()}
                disabled={busy !== null}
              >
                {busy !== null && <Loader2 size={14} className="animate-spin" />}
                Restart in 60s
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
