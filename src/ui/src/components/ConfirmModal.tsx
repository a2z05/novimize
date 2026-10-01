import { useState, useEffect, useRef, useCallback } from 'react'
import { X, AlertTriangle, Shield, Camera, Zap, ChevronDown, ChevronRight, Link2, GitBranch, Loader2 } from 'lucide-react'
import RiskBadge from './RiskBadge'
import type { ConfirmTweakItem, BatchPlan, PlanEntry, PlanAction } from '../types'

interface ConfirmModalProps {
  open: boolean
  title: string
  subtitle: string
  tweaks: ConfirmTweakItem[]
  onConfirm: () => void
  onCancel: () => void
  applying?: boolean
  applyProgress?: { current: number; total: number }
  /**
   * What the batch planner decided before anything was attempted. Undefined
   * while the check is still running; null when there was nothing to plan.
   */
  plan?: BatchPlan | null
  /** Resolve a name for a tweak that is not in this run — a missing dependency. */
  resolveName?: (tweakId: string) => string
  /** Guided fix: pull a missing dependency into the run. */
  onIncludeTweak?: (tweakId: string) => void
  /** Guided fix: keep one side of a conflict, which drops the other. */
  onKeepConflict?: (keepId: string, dropId: string) => void
}

const actionLabels: Record<PlanAction, string> = {
  Apply: 'Will run',
  MissingDependency: 'Missing dependency',
  Conflict: 'Conflicts',
  BlockedByDependency: 'Dependency held back',
  DependencyCycle: 'Circular dependency',
}

function riskColor(risk: string): string {
  switch (risk) {
    case 'Safe': return '#22C55E'
    case 'Recommended': return '#3B82F6'
    case 'Optional': return '#8888A0'
    case 'Experimental': return '#F59E0B'
    case 'Risky': return '#EF4444'
    case 'Dangerous': return '#EF4444'
    default: return '#8888A0'
  }
}

export default function ConfirmModal({
  open,
  title,
  subtitle,
  tweaks,
  onConfirm,
  onCancel,
  applying = false,
  applyProgress,
  plan,
  resolveName,
  onIncludeTweak,
  onKeepConflict,
}: ConfirmModalProps) {
  const [expanded, setExpanded] = useState(true)
  const contentRef = useRef<HTMLDivElement>(null)
  const confirmBtnRef = useRef<HTMLButtonElement>(null)
  const cancelBtnRef = useRef<HTMLButtonElement>(null)

  // Escape key handler
  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if (e.key === 'Escape' && !applying) {
      e.stopPropagation()
      onCancel()
    }
  }, [onCancel, applying])

  // Lock body scroll when modal is open
  useEffect(() => {
    if (open) {
      document.body.style.overflow = 'hidden'
      document.addEventListener('keydown', handleKeyDown, true)
      requestAnimationFrame(() => {
        confirmBtnRef.current?.focus()
      })
    }
    return () => {
      document.body.style.overflow = ''
      document.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [open, handleKeyDown])

  if (!open) return null

  const riskCounts: Record<string, number> = {}
  tweaks.forEach(t => { riskCounts[t.risk] = (riskCounts[t.risk] || 0) + 1 })
  const totalRisks = tweaks.length
  const specialTweaks = tweaks.filter(t => t.isSpecial)

  return (
    <div
      className="modal-backdrop"
      onClick={onCancel}
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-modal-title"
      aria-describedby="confirm-modal-desc"
    >
      <div
        ref={contentRef}
        className="modal-content animate-scale-in"
        onClick={e => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start justify-between mb-4">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl flex items-center justify-center"
                 style={{ background: 'linear-gradient(135deg, rgba(99,102,241,0.2), rgba(168,85,247,0.2))' }}>
              <Zap size={20} className="text-[var(--color-primary)]" aria-hidden="true" />
            </div>
            <div>
              <h2 id="confirm-modal-title" className="text-base font-bold">{title}</h2>
              <p id="confirm-modal-desc" className="text-[12px] text-[var(--color-text-muted)]">{subtitle}</p>
            </div>
          </div>
          <button
            onClick={onCancel}
            disabled={applying}
            className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] transition-colors p-1 rounded-md"
            aria-label="Close dialog"
          >
            <X size={18} />
          </button>
        </div>

        {/* Risk Summary Bar */}
        <div className="mb-4">
          <div className="flex items-center justify-between mb-1.5">
            <span className="text-[11px] text-[var(--color-text-muted)] font-semibold uppercase tracking-wider">Risk Summary</span>
            <span className="text-[11px] text-[var(--color-text-muted)]">
              {Object.entries(riskCounts).map(([risk, count]) => (
                <span key={risk}>
                  {count} {risk}{count > 1 ? 's' : ''}
                  {risk !== Object.keys(riskCounts).pop() && ' · '}
                </span>
              ))}
            </span>
          </div>
          <div className="risk-bar" role="img" aria-label={`Risk distribution: ${Object.entries(riskCounts).map(([r, c]) => `${c} ${r}`).join(', ')}`}>
            {Object.entries(riskCounts).map(([risk, count]) => (
              <div
                key={risk}
                className="risk-bar-segment"
                style={{
                  width: `${(count / totalRisks) * 100}%`,
                  background: riskColor(risk),
                }}
              />
            ))}
          </div>
        </div>

        {/* Special Changes Warning */}
        {specialTweaks.length > 0 && (
          <div className="mb-4 rounded-lg border border-[rgba(239,68,68,0.3)] bg-[rgba(239,68,68,0.05)] px-4 py-3" role="alert">
            <div className="flex items-center gap-2 mb-1">
              <AlertTriangle size={14} className="text-[var(--color-danger)]" aria-hidden="true" />
              <span className="text-[12px] font-bold text-[var(--color-danger)]">
                {specialTweaks.length} Special Change{specialTweaks.length > 1 ? 's' : ''} Detected
              </span>
            </div>
            <p className="text-[11px] text-[var(--color-text-muted)] ml-5">
              These tweaks affect system-level behavior and may require a restart. They are highlighted in the list below.
            </p>
          </div>
        )}

        {/* Planner verdict — what will run, and why anything will not */}
        {plan === undefined && (
          <div className="mb-4 flex items-center gap-2 text-[11px] text-[var(--color-text-muted)]">
            <Loader2 size={12} className="animate-spin" aria-hidden="true" />
            Checking these tweaks against each other...
          </div>
        )}

        {plan?.hasIssues && (
          <div
            className="mb-4 rounded-lg border border-[rgba(245,158,11,0.3)] bg-[rgba(245,158,11,0.05)] px-4 py-3"
            role="alert"
          >
            <div className="flex items-center justify-between gap-2 mb-1.5">
              <div className="flex items-center gap-2">
                <GitBranch size={14} className="text-[var(--color-warning)]" aria-hidden="true" />
                <span className="text-[12px] font-bold text-[var(--color-warning)]">
                  {plan.blockedCount} of {plan.requestedCount} will be held back
                </span>
              </div>
              <span className="text-[11px] text-[var(--color-text-muted)]">
                {plan.applicableCount} will apply
              </span>
            </div>
            <p className="text-[11px] text-[var(--color-text-muted)] mb-2">
              These would fight each other or are waiting on a tweak that is not in this run.
              Nothing is applied for them — pick a side and the rest runs normally.
            </p>

            <div className="space-y-1.5">
              {plan.entries
                .filter((e: PlanEntry) => e.action !== 'Apply')
                .map((entry: PlanEntry) => {
                  const missing = plan.missingDependencies.filter(m => m.tweakId === entry.tweakId)
                  const conflict = plan.conflicts.find(
                    c => c.a === entry.tweakId || c.b === entry.tweakId,
                  )
                  const nameOf = (id: string) => {
                    const local = tweaks.find(t => t.id === id)
                    return local?.name ?? resolveName?.(id) ?? id
                  }

                  return (
                    <div
                      key={entry.tweakId}
                      className="rounded-md border border-[rgba(245,158,11,0.2)] bg-[rgba(0,0,0,0.15)] px-3 py-2"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="text-[11px] font-semibold">{nameOf(entry.tweakId)}</span>
                        <span className="text-[10px] uppercase tracking-wider text-[var(--color-warning)] font-bold">
                          {actionLabels[entry.action]}
                        </span>
                      </div>
                      <p className="text-[11px] text-[var(--color-text-muted)] mt-0.5">
                        {entry.reason}
                      </p>

                      {/* Guided fix: a dependency that exists but was not picked. */}
                      {entry.action === 'MissingDependency' &&
                        onIncludeTweak &&
                        missing.some(m => m.requiredExists) &&
                        missing
                          .filter(m => m.requiredExists)
                          .map(m => (
                            <button
                              key={m.requiredId}
                              onClick={() => onIncludeTweak(m.requiredId)}
                              className="btn btn-sm mt-1.5 gap-1 text-[11px]"
                              style={{ border: '1px solid rgba(99,102,241,0.4)' }}
                            >
                              <Link2 size={11} aria-hidden="true" />
                              Include {nameOf(m.requiredId)}
                            </button>
                          ))}

                      {/* Guided fix: pick which side of a conflict wins. */}
                      {entry.action === 'Conflict' && conflict && onKeepConflict && (
                        <div className="flex flex-wrap gap-1.5 mt-1.5">
                          <button
                            onClick={() => onKeepConflict(conflict.a, conflict.b)}
                            className="btn btn-sm text-[11px]"
                            style={{ border: '1px solid rgba(99,102,241,0.4)' }}
                          >
                            Keep {nameOf(conflict.a)}
                          </button>
                          <button
                            onClick={() => onKeepConflict(conflict.b, conflict.a)}
                            className="btn btn-sm text-[11px]"
                            style={{ border: '1px solid rgba(99,102,241,0.4)' }}
                          >
                            Keep {nameOf(conflict.b)}
                          </button>
                        </div>
                      )}
                    </div>
                  )
                })}
            </div>
          </div>
        )}

        {plan && !plan.hasIssues && (
          <div className="mb-4 flex items-center gap-2 text-[11px] text-[var(--color-success)]">
            <GitBranch size={12} aria-hidden="true" />
            No conflicts — all {plan.applicableCount} will apply in a safe order.
          </div>
        )}

        {/* Changes List */}
        <div className="mb-4">
          <button
            onClick={() => setExpanded(!expanded)}
            className="flex items-center gap-2 mb-2 text-[11px] text-[var(--color-text-muted)] font-semibold uppercase tracking-wider hover:text-[var(--color-text)] transition-colors"
            aria-expanded={expanded}
          >
            {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
            Changes to be applied ({tweaks.length})
          </button>

          {expanded && (
            <div className="max-h-[300px] overflow-y-auto rounded-lg border border-[var(--color-border-subtle)] divide-y divide-[var(--color-border-subtle)] animate-fade-in">
              {tweaks.map(tweak => (
                <div
                  key={tweak.id}
                  className={`px-3 py-2.5 transition-colors ${
                    tweak.isSpecial
                      ? 'bg-[rgba(239,68,68,0.05)] border-l-2 border-l-[var(--color-danger)]'
                      : 'bg-[var(--color-bg-elevated)]'
                  }`}
                >
                  <div className="flex items-center gap-2 mb-0.5">
                    <span className="text-[12px] font-semibold">{tweak.name}</span>
                    <RiskBadge risk={tweak.risk} />
                    {tweak.isSpecial && (
                      <span className="special-badge" role="status">
                        <AlertTriangle size={9} aria-hidden="true" />
                        Special
                      </span>
                    )}
                  </div>
                  <div className="flex items-center gap-2 text-[10px] text-[var(--color-text-muted)] font-mono">
                    <span>{tweak.method}:</span>
                    {tweak.registryKey && (
                      <span className="truncate">{tweak.registryKey}</span>
                    )}
                    {tweak.serviceName && (
                      <span>Service: {tweak.serviceName}</span>
                    )}
                  </div>
                  <div className="flex items-center gap-1.5 mt-1 text-[10px] font-mono">
                    {/* Prefer what the scan found: the catalogue default is a
                        guess about this machine, the scan is a measurement.
                        A null here means the scan ran and could not read the
                        value — showing the default as if it had would be a
                        claim about this machine that nobody made. */}
                    <span className="text-[var(--color-text-muted)]">
                      {tweak.currentValue === null
                        ? '?'
                        : (tweak.currentValue ?? tweak.defaultValue) || '—'}
                    </span>
                    <span className="text-[var(--color-primary)]" aria-hidden="true">→</span>
                    <span className="text-[var(--color-text)]">{tweak.targetValue}</span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Snapshot notice */}
        <div className="flex items-center gap-2 mb-5 px-3 py-2 rounded-lg bg-[rgba(34,197,94,0.05)] border border-[rgba(34,197,94,0.15)]">
          <Camera size={14} className="text-[var(--color-success)]" aria-hidden="true" />
          <span className="text-[11px] text-[var(--color-success)] font-medium">
            A snapshot will be created automatically for rollback.
          </span>
        </div>

        {/* Progress */}
        {applying && applyProgress && (
          <div className="mb-4" role="progressbar" aria-valuenow={applyProgress.current} aria-valuemin={0} aria-valuemax={applyProgress.total}>
            <div className="flex items-center justify-between mb-1">
              <span className="text-[11px] text-[var(--color-text-muted)]">Applying...</span>
              <span className="text-[11px] text-[var(--color-primary)] font-semibold">
                {applyProgress.current}/{applyProgress.total}
              </span>
            </div>
            <div className="progress-bar">
              <div
                className="progress-bar-fill"
                style={{ width: `${(applyProgress.current / applyProgress.total) * 100}%` }}
              />
            </div>
          </div>
        )}

        {/* Actions */}
        <div className="flex items-center justify-end gap-3">
          <button
            ref={cancelBtnRef}
            onClick={onCancel}
            disabled={applying}
            className="btn btn-secondary"
          >
            Cancel
          </button>
          <button
            ref={confirmBtnRef}
            onClick={onConfirm}
            disabled={applying}
            className="btn btn-primary"
          >
            {applying ? (
              <>
                <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" aria-hidden="true" />
                Applying...
              </>
            ) : (
              <>
                <Shield size={14} aria-hidden="true" />
                {plan?.hasIssues
                  ? `Confirm & Apply ${plan.applicableCount}`
                  : 'Confirm & Apply'}
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  )
}
