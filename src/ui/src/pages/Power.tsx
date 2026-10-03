import { useCallback, useEffect, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import {
  AlertTriangle,
  Battery,
  CheckCircle2,
  Cpu,
  Flame,
  Gauge,
  Loader2,
  Monitor,
  RefreshCw,
  RotateCcw,
  Zap,
} from 'lucide-react'
import type { PowerChange, PowerStatus } from '../types'

function errMsg(err: unknown): string {
  // §29: never a bare "something went wrong". The string branch is the
  // common one — the CLI already names the action in what it says — and the
  // other two have to say what happened and where it happened.
  if (typeof err === 'string') return err
  if (err instanceof Error) return `${err.name}: ${err.message}`
  return 'Cause: the command returned a result this version cannot read. Affected component: this page.'
}

type Pending = { kind: 'plan'; id: string } | { kind: 'novimize' } | { kind: 'revert' }

/**
 * What each plan costs, in the only terms that are honest here: arrows, not
 * numbers. These are descriptions of what the plan is for, not measurements —
 * the brief is explicit that this is informational and not a benchmark claim.
 */
const TRADEOFFS: Record<string, { performance: number; power: number; heat: number; blurb: string }> = {
  balanced: {
    performance: 0,
    power: 0,
    heat: 0,
    blurb: 'Windows default. Lets the CPU sit low when nothing is asking for it.',
  },
  high: {
    performance: 1,
    power: 1,
    heat: 1,
    blurb: 'Keeps the clock up and the latency down. Uses more at idle.',
  },
  ultimate: {
    performance: 2,
    power: 2,
    heat: 2,
    blurb: 'Removes the last of the power-state transitions. Loudest and hottest.',
  },
  novimize: {
    performance: 2,
    power: 2,
    heat: 2,
    blurb: 'Balanced as a base, with the floor, parking, boost and EPP set the way a desktop wants them.',
  },
}

function Arrows({ performance, power, heat }: { performance: number; power: number; heat: number }) {
  const cell = (n: number, tone: string) => (
    <span className="font-mono" style={{ color: tone }}>
      {n === 0 ? '→' : n === 1 ? '↑' : '↑↑'}
    </span>
  )
  return (
    <div className="flex gap-3 text-[12px]">
      <span>Performance {cell(performance, 'var(--color-success)')}</span>
      <span>Power {cell(power, 'var(--color-warning)')}</span>
      <span>Heat {cell(heat, 'var(--color-danger)')}</span>
    </div>
  )
}

export default function Power() {
  const [status, setStatus] = useState<PowerStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [change, setChange] = useState<PowerChange | null>(null)
  const [busy, setBusy] = useState<string | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [preview, setPreview] = useState<PowerChange | null>(null)

  const load = useCallback(
    async () => setStatus(await invokeJson<PowerStatus>('power_action', { action: 'status' })),
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

  /**
   * Ask the CLI what the switch would run, then show that. The preview is
   * produced by the same code that will execute it, so the dialog cannot
   * describe something different from what happens.
   */
  async function prepare(p: Pending) {
    setPreview(null)
    const res = await run(`prep:${p.kind}`, () => {
      switch (p.kind) {
        case 'plan':
          return invokeJson<PowerChange>('power_action', { action: 'plan', id: p.id, confirm: false })
        case 'novimize':
          return invokeJson<PowerChange>('power_action', { action: 'novimize', confirm: false })
        case 'revert':
          return invokeJson<PowerChange>('power_action', { action: 'revert', confirm: false })
      }
    })
    if (!res) return
    setPreview(res)
    setPending(p)
  }

  async function confirm() {
    if (!pending) return
    const p = pending
    setPending(null)
    const res = await run(`do:${p.kind}`, () => {
      switch (p.kind) {
        case 'plan':
          return invokeJson<PowerChange>('power_action', { action: 'plan', id: p.id, confirm: true })
        case 'novimize':
          return invokeJson<PowerChange>('power_action', { action: 'novimize', confirm: true })
        case 'revert':
          return invokeJson<PowerChange>('power_action', { action: 'revert', confirm: true })
      }
    })
    if (res) setChange(res)
    await load()
  }

  const laptop = status?.isLaptop ?? false

  /** Which catalogue plan this machine already has, so the button can say so. */
  function planGuid(kind: 'balanced' | 'high'): string | undefined {
    return status?.plans.find(p =>
      kind === 'balanced'
        ? p.guid.toLowerCase() === '381b4222-f694-41f0-9685-ff5bb260df2e'
        : p.guid.toLowerCase() === '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c',
    )?.guid
  }

  function ultimateGuid(): string | undefined {
    return status?.plans.find(p => p.name.toLowerCase().includes('ultimate'))?.guid
  }

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-3 animate-slide-up">
        <div>
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-2">
            <Gauge size={22} className="text-[var(--color-primary)]" />
            Power Center
          </h1>
          <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
            What the power plan is set to, what each setting costs, and the way back
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

      {/* ── Where things stand ───────────────────────────────────── */}
      {status && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 animate-slide-up stagger-1">
          <div className="card">
            <div className="flex items-center gap-2 mb-2">
              <Zap size={14} className="text-[var(--color-primary)]" />
              <h3 className="text-[13px] font-semibold">Active plan</h3>
            </div>
            <div className="text-[17px] font-semibold">{status.activePlan}</div>
            <div className="text-[11px] font-mono text-[var(--color-text-muted)] break-all mt-0.5">
              {status.activePlanGuid}
            </div>
            {status.previousPlanGuid && (
              <div className="mt-3 flex items-center gap-2">
                <span className="text-[11px] text-[var(--color-text-muted)]">
                  was {status.previousPlanName}
                </span>
                <button
                  className="btn btn-ghost btn-sm"
                  onClick={() => void prepare({ kind: 'revert' })}
                  disabled={busy !== null}
                >
                  <RotateCcw size={13} />
                  Put it back
                </button>
              </div>
            )}
          </div>

          <div className="card">
            <div className="flex items-center gap-2 mb-2">
              {laptop ? <Battery size={14} className="text-[var(--color-primary)]" /> : <Monitor size={14} className="text-[var(--color-primary)]" />}
              <h3 className="text-[13px] font-semibold">Machine</h3>
            </div>
            <div className="text-[17px] font-semibold">{status.formFactor}</div>
            <div className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
              {status.battery.present ? (
                <>
                  {status.battery.percent ?? '—'}% ·{' '}
                  {status.battery.minutesRemaining != null
                    ? `~${status.battery.minutesRemaining} min left · `
                    : ''}
                  {status.battery.onAc ? 'on mains' : 'on battery'}
                </>
              ) : (
                'No battery — always on mains.'
              )}
            </div>
          </div>

          <div className="card">
            <div className="flex items-center gap-2 mb-2">
              <Cpu size={14} className="text-[var(--color-primary)]" />
              <h3 className="text-[13px] font-semibold">Plans on this machine</h3>
            </div>
            <div className="text-[17px] font-semibold">{status.plans.length}</div>
            <div className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
              {status.plans.filter(p => p.name.toLowerCase().includes('ultimate')).length} of them are
              called “Ultimate Performance” — every copy has its own GUID.
            </div>
          </div>
        </div>
      )}

      {/* ── Plans ────────────────────────────────────────────────── */}
      {status && (
        <div className="card animate-slide-up stagger-2">
          <div className="flex items-center gap-2 mb-1">
            <Zap size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Switch plan</h3>
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
            The arrows describe what the plan is for. They are not a measurement, and nothing here
            claims a frame-rate number.
          </p>

          {laptop && (
            <div className="flex items-start gap-2 rounded-lg bg-amber-500/10 border border-amber-500/25 px-3 py-2.5 mb-3">
              <AlertTriangle size={15} className="text-[var(--color-warning)] mt-0.5 flex-shrink-0" />
              <div className="text-[12px] text-[var(--color-warning)]">
                This is a laptop. Ultimate Performance is never selected automatically here — it is
                offered, with the cost attached.
              </div>
            </div>
          )}

          <div className="space-y-2">
            {(['balanced', 'high', 'ultimate', 'novimize'] as const).map(kind => {
              const t = TRADEOFFS[kind]
              const label =
                kind === 'balanced'
                  ? 'Balanced'
                  : kind === 'high'
                    ? 'High performance'
                    : kind === 'ultimate'
                      ? 'Ultimate Performance'
                      : 'Novimize'
              const guid =
                kind === 'balanced'
                  ? planGuid('balanced')
                  : kind === 'high'
                    ? planGuid('high')
                    : kind === 'ultimate'
                      ? ultimateGuid()
                      : undefined
              const isActive =
                kind === 'novimize'
                  ? status.activePlan.toLowerCase() === 'novimize'
                  : !!guid && status.activePlanGuid.toLowerCase() === guid.toLowerCase()
              const unavailable = (kind === 'ultimate' && !guid) || (kind === 'novimize' && laptop)

              return (
                <div
                  key={kind}
                  className="flex flex-wrap items-center gap-x-3 gap-y-2 rounded-lg border px-3 py-2.5"
                  style={{
                    borderColor: isActive ? 'rgba(34,197,94,0.45)' : 'var(--color-border)',
                  }}
                >
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="text-[13px] font-semibold">{label}</span>
                      {isActive && <span className="badge badge-safe">active</span>}
                      {kind === 'ultimate' && !guid && (
                        <span className="badge badge-optional">not on this machine</span>
                      )}
                    </div>
                    <div className="text-[12px] text-[var(--color-text-muted)] mt-0.5">{t.blurb}</div>
                    <div className="mt-1.5">
                      <Arrows performance={t.performance} power={t.power} heat={t.heat} />
                    </div>
                  </div>
                  <button
                    className="btn btn-secondary btn-sm flex-shrink-0"
                    onClick={() => {
                      if (kind === 'novimize') void prepare({ kind: 'novimize' })
                      else if (guid) void prepare({ kind: 'plan', id: guid })
                    }}
                    disabled={busy !== null || unavailable || isActive}
                    title={
                      unavailable
                        ? kind === 'ultimate'
                          ? 'This machine has no Ultimate Performance plan'
                          : 'Not offered on a laptop — see the note above'
                        : isActive
                          ? 'Already active'
                          : ''
                    }
                  >
                    {isActive ? 'Active' : kind === 'novimize' ? 'Create and use…' : 'Use…'}
                  </button>
                </div>
              )
            })}
          </div>

          {status.plans.length > 0 && (
            <div className="mt-4 pt-3 border-t border-[var(--color-border)]">
              <div className="text-[11px] font-semibold tracking-widest uppercase text-[var(--color-text-muted)] mb-1">
                Everything Windows has
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 text-[12px]">
                {status.plans.map(p => (
                  <div key={p.guid} className="flex items-center gap-2 py-0.5 min-w-0">
                    <span
                      className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                      style={{
                        background: p.active ? 'var(--color-success)' : 'var(--color-text-muted)',
                        opacity: p.active ? 1 : 0.4,
                      }}
                    />
                    <span className="truncate flex-1">{p.name}</span>
                    {!p.active && (
                      <button
                        className="btn btn-ghost btn-sm flex-shrink-0"
                        onClick={() => void prepare({ kind: 'plan', id: p.guid })}
                        disabled={busy !== null}
                      >
                        Use
                      </button>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}

      {/* ── Settings ─────────────────────────────────────────────── */}
      {status && (
        <div className="card animate-slide-up stagger-3">
          <div className="flex items-center gap-2 mb-1">
            <Cpu size={14} className="text-[var(--color-primary)]" />
            <h3 className="text-[13px] font-semibold">Settings on the active plan</h3>
            <span className="text-[11px] text-[var(--color-text-muted)]">
              {status.settings.filter(s => !s.unavailable).length} of {status.settings.length} exposed
            </span>
          </div>
          <p className="text-[11px] text-[var(--color-text-muted)] mb-3">
            Read from <span className="font-mono">powercfg</span>, which answers for a setting
            whether or not the plan stores it — the registry would read a missing key as “not
            configured” when it means “inherits”.
          </p>

          <div className="space-y-2">
            {status.settings.map(s => (
              <div
                key={s.id}
                className="rounded-lg border border-[var(--color-border)] px-3 py-2"
                style={s.unavailable ? { opacity: 0.55 } : undefined}
              >
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="text-[13px] font-medium">{s.name}</span>
                  <span className="text-[13px] font-mono ml-auto">
                    {s.value}
                    {s.batteryValue && (
                      <span className="text-[var(--color-text-muted)]">
                        {' '}· {s.batteryValue} on battery
                      </span>
                    )}
                  </span>
                </div>
                {s.note && (
                  <div className="text-[11px] text-[var(--color-text-muted)] mt-0.5">{s.note}</div>
                )}
                {s.existingTweak && (
                  <div className="text-[11px] text-[var(--color-primary)] mt-0.5 font-mono">
                    also managed by the tweak {s.existingTweak}
                  </div>
                )}
              </div>
            ))}
          </div>

          <div className="flex items-start gap-2 mt-3 text-[11px] text-[var(--color-text-muted)]">
            <Flame size={13} className="mt-0.5 flex-shrink-0" />
            <div>
              Changing an individual setting belongs to the tweak catalogue, where each one has an
              evidence rating and a rollback. This page reads them so you can see what the plan you
              picked is actually doing.
            </div>
          </div>
        </div>
      )}

      {/* ── Confirmation ─────────────────────────────────────────── */}
      {pending && preview && (
        <div className="modal-backdrop" onClick={() => setPending(null)}>
          <div className="modal-content animate-scale-in" onClick={e => e.stopPropagation()}>
            <div className="flex items-start gap-3 mb-4">
              <div
                className="w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0"
                style={{ background: 'rgba(59,130,246,0.15)' }}
              >
                <Zap size={19} className="text-[var(--color-primary)]" />
              </div>
              <div className="min-w-0 flex-1">
                <h3 className="text-[15px] font-semibold">{preview.message}</h3>
                <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5">
                  Nothing has been run yet
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
                {preview.preview.length === 0 ? (
                  <div className="text-[var(--color-text-muted)]">Nothing to run.</div>
                ) : (
                  preview.preview.map(line => <div key={line}>{line}</div>)
                )}
              </div>
            </div>

            <div className="flex justify-end gap-2">
              <button className="btn btn-secondary" onClick={() => setPending(null)}>
                Cancel
              </button>
              <button
                className="btn btn-primary"
                onClick={() => void confirm()}
                disabled={busy !== null}
              >
                {busy !== null && <Loader2 size={14} className="animate-spin" />}
                Switch
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
