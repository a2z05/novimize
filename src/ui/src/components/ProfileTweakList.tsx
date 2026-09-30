import { useEffect, useState } from 'react'
import { invokeJson } from '../hooks/useTauri'
import type { TweakDef } from '../types'
import RiskBadge from './RiskBadge'
import { Loader2, Zap } from 'lucide-react'

interface ProfileTweakListProps {
  profileId: string
  onApply?: (profileId: string) => void
  applying?: boolean
}

export default function ProfileTweakList({ profileId, onApply, applying }: ProfileTweakListProps) {
  const [tweaks, setTweaks] = useState<TweakDef[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    setLoading(true)
    invokeJson<TweakDef[]>('list_tweaks', { profile: profileId })
      .then(data => setTweaks(data || []))
      .catch(() => setTweaks([]))
      .finally(() => setLoading(false))
  }, [profileId])

  if (loading) {
    return (
      <div className="flex items-center justify-center py-6">
        <Loader2 size={18} className="animate-spin text-[var(--color-primary)]" />
        <span className="ml-2 text-[12px] text-[var(--color-text-muted)]">Loading tweaks...</span>
      </div>
    )
  }

  if (tweaks.length === 0) {
    return (
      <div className="text-center py-6 text-[12px] text-[var(--color-text-muted)]">
        No tweaks found for this profile.
      </div>
    )
  }

  // Risk distribution
  const riskCounts: Record<string, number> = {}
  tweaks.forEach(t => { riskCounts[t.risk] = (riskCounts[t.risk] || 0) + 1 })

  const riskColor: Record<string, string> = {
    Safe: '#22C55E',
    Recommended: '#3B82F6',
    Optional: '#8888A0',
    Experimental: '#F59E0B',
    Risky: '#EF4444',
    Dangerous: '#EF4444',
  }

  return (
    <div className="mt-3 pt-3 border-t border-[var(--color-border-subtle)]">
      {/* Stats row */}
      <div className="flex items-center gap-4 mb-3">
        <span className="text-[11px] font-semibold text-[var(--color-text-muted)]">
          {tweaks.length} tweak{tweaks.length !== 1 ? 's' : ''}
        </span>

        {/* Risk bar */}
        <div className="flex-1">
          <div className="risk-bar">
            {Object.entries(riskCounts).map(([risk, count]) => (
              <div
                key={risk}
                className="risk-bar-segment"
                style={{
                  width: `${(count / tweaks.length) * 100}%`,
                  background: riskColor[risk] || '#8888A0',
                }}
                title={`${count} ${risk}`}
              />
            ))}
          </div>
        </div>

        {/* Risk count chips */}
        <div className="flex items-center gap-1.5">
          {Object.entries(riskCounts).map(([risk, count]) => (
            <span key={risk} className="text-[9px] font-semibold" style={{ color: riskColor[risk] }}>
              {count} {risk.charAt(0)}
            </span>
          ))}
        </div>
      </div>

      {/* Tweak list */}
      <div className="max-h-[250px] overflow-y-auto rounded-lg border border-[var(--color-border-subtle)] divide-y divide-[var(--color-border-subtle)]">
        {tweaks.map((tweak, i) => (
          <div
            key={tweak.id}
            className="flex items-center gap-2.5 px-3 py-2 hover:bg-[rgba(255,255,255,0.02)] transition-colors"
            style={{ animation: `slideUp 200ms ease-out ${i * 25}ms both` }}
          >
            <div className="w-1.5 h-1.5 rounded-full flex-shrink-0"
                 style={{ background: riskColor[tweak.risk] || '#8888A0' }} />
            <span className="text-[12px] font-medium flex-1 truncate">{tweak.name}</span>
            <RiskBadge risk={tweak.risk} />
            <div className="evidence-dots flex-shrink-0">
              {[1, 2, 3, 4, 5].map(i => (
                <div
                  key={i}
                  className="evidence-dot"
                  style={{
                    background: i <= tweak.evidence
                      ? 'linear-gradient(135deg, #6366F1, #A855F7)'
                      : 'var(--color-border)',
                  }}
                />
              ))}
            </div>
          </div>
        ))}
      </div>

      {/* Apply button */}
      {onApply && (
        <div className="mt-3 flex justify-end">
          <button
            onClick={() => onApply(profileId)}
            disabled={applying}
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
