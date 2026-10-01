import { useEffect, useMemo, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import type { ExclusionReason, ProfileSelection, ProfileTweakVerdict } from '../types'
import RiskBadge from './RiskBadge'
import { AlertTriangle, Check, ChevronDown, ChevronRight, Loader2, Zap } from 'lucide-react'

interface ProfileTweakListProps {
  profileId: string
  /**
   * Fired with the opt-in ids the user ticked. The default set is not passed:
   * the caller already knows it, and passing a second list of what will be
   * applied is how the two went out of sync the first time.
   */
  onApply?: (profileId: string, optIns: string[]) => void
  applying?: boolean
}

/** Human wording for a bar a tweak failed, so the row reads as a sentence. */
const reasonLabel: Record<ExclusionReason, string> = {
  None: 'Applied',
  LowEvidence: 'Below the evidence bar',
  HighRisk: 'Above the risk ceiling',
  ExcludedById: 'Excluded by name',
  ExcludedCategory: 'Excluded category',
  OutsideCategories: 'Not in this profile’s categories',
  SecurityBlocked: 'Blocked by the security guard',
}

const riskColor: Record<string, string> = {
  Safe: '#22C55E',
  Recommended: '#3B82F6',
  Optional: '#8888A0',
  Experimental: '#F59E0B',
  Risky: '#EF4444',
  Dangerous: '#EF4444',
}

export default function ProfileTweakList({ profileId, onApply, applying }: ProfileTweakListProps) {
  const [selection, setSelection] = useState<ProfileSelection | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  /** Ids the user has ticked out of "available if you want them". */
  const [optIns, setOptIns] = useState<Set<string>>(new Set())
  const [showExcluded, setShowExcluded] = useState(false)

  useEffect(() => {
    // Cancelled on unmount and on a profile switch, so a slow answer for the
    // profile the user just left cannot overwrite the one they are looking at.
    let cancelled = false
    setLoading(true)
    setError(null)
    setOptIns(new Set())
    setShowExcluded(false)
    invokeJson<ProfileSelection>('profile_selector', { profile: profileId })
      .then(sel => { if (!cancelled) setSelection(sel ?? null) })
      .catch(err => { if (!cancelled) setError(String((err as Error)?.message || err)) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [profileId])

  const groupedExcluded = useMemo(() => {
    if (!selection) return []
    const groups = new Map<ExclusionReason, ProfileTweakVerdict[]>()
    for (const v of selection.excluded) {
      const list = groups.get(v.reason)
      if (list) list.push(v)
      else groups.set(v.reason, [v])
    }
    return [...groups.entries()].map(([reason, items]) => ({
      reason,
      items,
      detail: items.find(v => v.detail)?.detail ?? '',
    }))
  }, [selection])

  // What the risk bar is measuring: the default set plus whatever has been
  // ticked. Ticking a risky opt-in widens the bar immediately, so the cost of
  // the choice is visible at the moment it is made, not after Apply.
  const active = useMemo(() => {
    if (!selection) return []
    return [...selection.defaultSet, ...selection.optIn.filter(v => optIns.has(v.tweak.id))]
  }, [selection, optIns])

  function toggle(id: string) {
    setOptIns(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center py-6">
        <Loader2 size={18} className="animate-spin text-[var(--color-primary)]" />
        <span className="ml-2 text-[12px] text-[var(--color-text-muted)]">Loading tweaks...</span>
      </div>
    )
  }

  if (error || !selection) {
    return (
      <div className="text-center py-6 text-[12px] text-[var(--color-danger)]">
        {error || 'Could not work out what this profile would do.'}
      </div>
    )
  }

  if (selection.defaultSet.length === 0 && selection.optIn.length === 0) {
    return (
      <div className="text-center py-6 text-[12px] text-[var(--color-text-muted)]">
        No tweaks found for this profile.
      </div>
    )
  }

  const riskCounts: Record<string, number> = {}
  active.forEach(v => { riskCounts[v.tweak.risk] = (riskCounts[v.tweak.risk] || 0) + 1 })

  const rowStyle = (i: number) => ({ animation: `slideUp 200ms ease-out ${i * 25}ms both` })

  return (
    <div className="mt-3 pt-3 border-t border-[var(--color-border-subtle)]">
      {/* Notices: true of the profile on this machine, not of any one tweak. */}
      {selection.notices.length > 0 && (
        <div className="mb-3 space-y-1.5">
          {selection.notices.map((n, i) => (
            <div
              key={i}
              className="flex items-start gap-2 rounded-lg px-2.5 py-2 text-[11px] leading-relaxed"
              style={{ background: 'rgba(168,85,247,0.08)', border: '1px solid rgba(168,85,247,0.25)' }}
            >
              <AlertTriangle size={12} className="text-[#A855F7] flex-shrink-0 mt-0.5" aria-hidden="true" />
              <span className="text-[var(--color-text)]">{n}</span>
            </div>
          ))}
        </div>
      )}

      {/* Stats row — measures what will actually run, opt-ins included. */}
      <div className="flex items-center gap-4 mb-3">
        <span className="text-[11px] font-semibold text-[var(--color-text-muted)]">
          {active.length} applied
          {optIns.size > 0 && <span className="text-[#F59E0B]"> · {optIns.size} chosen</span>}
        </span>

        <div className="flex-1">
          <div className="risk-bar">
            {active.length === 0 ? (
              <div className="risk-bar-segment" style={{ width: '100%', background: 'var(--color-border)' }} />
            ) : (
              Object.entries(riskCounts).map(([risk, count]) => (
                <div
                  key={risk}
                  className="risk-bar-segment"
                  style={{
                    width: `${(count / active.length) * 100}%`,
                    background: riskColor[risk] || '#8888A0',
                  }}
                  title={`${count} ${risk}`}
                />
              ))
            )}
          </div>
        </div>

        <div className="flex items-center gap-1.5">
          {Object.entries(riskCounts).map(([risk, count]) => (
            <span key={risk} className="text-[9px] font-semibold" style={{ color: riskColor[risk] }}>
              {count} {risk.charAt(0)}
            </span>
          ))}
        </div>
      </div>

      {/* Applied by default */}
      <h5 className="text-[10px] text-[var(--color-success)] uppercase tracking-wider font-semibold mb-1.5">
        Applies by default ({selection.defaultSet.length})
      </h5>
      <div className="max-h-[220px] overflow-y-auto rounded-lg border border-[var(--color-border-subtle)] divide-y divide-[var(--color-border-subtle)]">
        {selection.defaultSet.length === 0 && (
          <div className="px-3 py-3 text-[11px] text-[var(--color-text-muted)]">
            Nothing — this profile only reaches tweaks it wants your say on first.
          </div>
        )}
        {selection.defaultSet.map((v, i) => (
          <div
            key={v.tweak.id}
            className="flex items-center gap-2.5 px-3 py-2 hover:bg-[rgba(255,255,255,0.02)] transition-colors"
            style={rowStyle(i)}
            title={v.tweak.description}
          >
            <div className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                 style={{ background: riskColor[v.tweak.risk] || '#8888A0' }} />
            <span className="text-[12px] font-medium flex-1 truncate">{v.tweak.name}</span>
            <RiskBadge risk={v.tweak.risk} />
            <EvidenceDots value={v.tweak.evidence} />
          </div>
        ))}
      </div>

      {/* Available if you want them */}
      {selection.optIn.length > 0 && (
        <div className="mt-4">
          <div className="flex items-baseline justify-between mb-1.5">
            <h5 className="text-[10px] text-[var(--color-warning)] uppercase tracking-wider font-semibold">
              Available if you want them ({selection.optIn.length})
            </h5>
            <span className="text-[9px] text-[var(--color-text-muted)]">not applied unless ticked</span>
          </div>
          <div className="max-h-[220px] overflow-y-auto rounded-lg border border-[var(--color-border-subtle)] divide-y divide-[var(--color-border-subtle)]">
            {selection.optIn.map((v, i) => {
              const checked = optIns.has(v.tweak.id)
              return (
                <div
                  key={v.tweak.id}
                  className={`flex items-start gap-2.5 px-3 py-2 transition-colors cursor-pointer ${
                    checked ? 'bg-[rgba(99,102,241,0.07)]' : 'hover:bg-[rgba(255,255,255,0.02)]'
                  }`}
                  style={rowStyle(i)}
                  onClick={() => toggle(v.tweak.id)}
                >
                  <span
                    className={`mt-0.5 w-3.5 h-3.5 rounded flex-shrink-0 flex items-center justify-center border transition-colors ${
                      checked ? 'bg-[var(--color-primary)] border-[var(--color-primary)]' : 'border-[var(--color-border)]'
                    }`}
                    role="checkbox"
                    aria-checked={checked}
                    aria-label={`${checked ? 'Remove' : 'Add'} ${v.tweak.name}`}
                  >
                    {checked && <Check size={9} className="text-white" strokeWidth={3} />}
                  </span>

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2.5">
                      <span className="text-[12px] font-medium flex-1 truncate">{v.tweak.name}</span>
                      <RiskBadge risk={v.tweak.risk} />
                      <EvidenceDots value={v.tweak.evidence} />
                    </div>
                    {/* The bar it failed, in words. Without this the section
                        is a list of things the app decided not to do, which is
                        the same silence the split exists to remove. */}
                    <p className="text-[10px] text-[var(--color-text-muted)] mt-0.5 leading-snug">
                      {v.detail || reasonLabel[v.reason]}
                    </p>
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      )}

      {/* Not part of this profile — grouped, because one row per tweak would
          bury the exclusions that were deliberate under thirty identical ones. */}
      {selection.excluded.length > 0 && (
        <div className="mt-4">
          <button
            onClick={() => setShowExcluded(!showExcluded)}
            className="flex items-center gap-1.5 text-[10px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold hover:text-[var(--color-text)] transition-colors"
            aria-expanded={showExcluded}
          >
            {showExcluded ? <ChevronDown size={11} /> : <ChevronRight size={11} />}
            Not part of this profile ({selection.excluded.length})
          </button>

          {showExcluded && (
            <div className="mt-2 space-y-1.5">
              {groupedExcluded.map(g => (
                <div
                  key={g.reason}
                  className="rounded-lg border border-[var(--color-border-subtle)] px-3 py-2"
                >
                  <div className="flex items-baseline gap-2">
                    <span className="text-[11px] font-semibold text-[var(--color-text-muted)]">
                      {reasonLabel[g.reason]}
                    </span>
                    <span className="text-[10px] text-[var(--color-text-muted)]">{g.items.length}</span>
                  </div>
                  {g.detail && (
                    <p className="text-[10px] text-[var(--color-text-muted)] mt-0.5 leading-snug">{g.detail}</p>
                  )}
                  <div className="flex flex-wrap gap-1 mt-1.5">
                    {g.items.map(v => (
                      <span key={v.tweak.id} className="category-tag opacity-70" title={v.tweak.name}>
                        {v.tweak.id}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Apply button */}
      {onApply && (
        <div className="mt-3 flex items-center justify-between gap-3">
          <span className="text-[10px] text-[var(--color-text-muted)]">
            {optIns.size > 0
              ? `${selection.defaultSet.length} by default + ${optIns.size} you chose`
              : `Applies ${selection.defaultSet.length} tweak${selection.defaultSet.length !== 1 ? 's' : ''}`}
          </span>
          <button
            onClick={() => onApply(profileId, [...optIns])}
            disabled={applying || active.length === 0}
            className="btn btn-sm btn-primary"
          >
            {applying ? (
              <>
                <div className="w-3.5 h-3.5 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                Applying...
              </>
            ) : (
              <>
                <Zap size={12} />
                Apply This Profile
              </>
            )}
          </button>
        </div>
      )}
    </div>
  )
}

function EvidenceDots({ value }: { value: number }) {
  return (
    <div className="evidence-dots flex-shrink-0">
      {[1, 2, 3, 4, 5].map(i => (
        <div
          key={i}
          className="evidence-dot"
          style={{
            background: i <= value ? 'linear-gradient(135deg, #6366F1, #A855F7)' : 'var(--color-border)',
          }}
        />
      ))}
    </div>
  )
}
