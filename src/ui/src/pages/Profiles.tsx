import { useState } from 'react'
import { Check, ChevronDown, ChevronRight, AlertTriangle, Info, AlertOctagon, CheckCircle2 } from 'lucide-react'
import ProfileTweakList from '../components/ProfileTweakList'
import ConfirmModal from '../components/ConfirmModal'
import type { ConfirmTweakItem, ApplySummary, ApplySessionResult } from '../types'
import { tallyStatus } from '../types'
import { invokeJson } from '../hooks/useTauri'
import type { TweakDef } from '../types'

interface Profile {
  id: string
  name: string
  description: string
  icon: string
  minTier: string | null
  maxTier: string | null
  includeCategories: string[]
  excludeCategories: string[]
  maxRisk: string
  minEvidence: number
  allowAutoOptimize: boolean
}

const allProfiles: Profile[] = [
  {
    id: 'gaming', name: 'Gaming', icon: '🎮',
    description: 'Maximum gaming performance: aggressive CPU/GPU tuning, low latency network, Game Mode, reduced input lag',
    minTier: 'Mid', maxTier: null,
    includeCategories: ['cpu', 'gpu', 'network', 'gaming', 'visual-effects', 'power'],
    excludeCategories: ['privacy'],
    maxRisk: 'Optional', minEvidence: 3, allowAutoOptimize: false,
  },
  {
    id: 'potato-pc', name: 'Potato PC', icon: '🥔',
    description: 'Ultra-conservative optimization for very old/low-end hardware: maximum background reduction, minimal visual effects',
    minTier: null, maxTier: 'Mid',
    includeCategories: ['services', 'startup', 'visual-effects', 'cleanup', 'apps'],
    excludeCategories: [],
    maxRisk: 'Safe', minEvidence: 4, allowAutoOptimize: true,
  },
  {
    id: 'office', name: 'Office / Productivity', icon: '💼',
    description: 'Optimized for Office apps, browsers, and multitasking: snappy UI, fast boot, reliable updates',
    minTier: null, maxTier: null,
    includeCategories: ['services', 'startup', 'visual-effects', 'memory', 'cleanup'],
    excludeCategories: ['gaming'],
    maxRisk: 'Safe', minEvidence: 4, allowAutoOptimize: true,
  },
  {
    id: 'daily', name: 'Daily Driver', icon: '🖥️',
    description: 'Balanced optimization for everyday use: good performance without breaking anything',
    minTier: null, maxTier: null,
    includeCategories: ['cpu', 'gpu', 'memory', 'storage', 'network', 'services', 'startup', 'visual-effects', 'cleanup', 'power', 'privacy', 'gaming'],
    excludeCategories: [],
    maxRisk: 'Safe', minEvidence: 4, allowAutoOptimize: true,
  },
  {
    id: 'streaming', name: 'Streaming', icon: '📺',
    description: 'Optimized for OBS/streaming: CPU encoding priority, network upload focus, minimal background processes',
    minTier: 'Mid', maxTier: null,
    includeCategories: ['cpu', 'gpu', 'network', 'services', 'startup', 'power'],
    excludeCategories: [],
    maxRisk: 'Optional', minEvidence: 3, allowAutoOptimize: false,
  },
  {
    id: 'developer', name: 'Developer', icon: '👨‍💻',
    description: 'Optimized for development: Docker/WSL performance, fast builds, maximum RAM availability',
    minTier: null, maxTier: null,
    includeCategories: ['memory', 'storage', 'network', 'services', 'startup'],
    excludeCategories: ['privacy', 'cleanup'],
    maxRisk: 'Optional', minEvidence: 3, allowAutoOptimize: false,
  },
  {
    id: 'workstation', name: 'Workstation', icon: '🖥️',
    description: 'High-end workstation for CAD/rendering/VMs: maximum resources, no power throttling',
    minTier: 'High', maxTier: null,
    includeCategories: ['cpu', 'gpu', 'memory', 'storage', 'power', 'network'],
    excludeCategories: [],
    maxRisk: 'Optional', minEvidence: 3, allowAutoOptimize: false,
  },
  {
    id: 'battery-saver', name: 'Battery Saver', icon: '🔋',
    description: 'Maximum battery life: aggressive power saving, reduced performance, longer uptime',
    minTier: null, maxTier: null,
    includeCategories: ['power', 'services', 'startup', 'visual-effects'],
    excludeCategories: [],
    maxRisk: 'Safe', minEvidence: 4, allowAutoOptimize: true,
  },
]

const categoryColors: Record<string, string> = {
  cpu: '#6366F1',
  gpu: '#A855F7',
  memory: '#3B82F6',
  storage: '#22C55E',
  network: '#06B6D4',
  services: '#F59E0B',
  startup: '#EC4899',
  'visual-effects': '#8B5CF6',
  cleanup: '#10B981',
  power: '#EF4444',
  privacy: '#6B7280',
  gaming: '#F97316',
}

const riskColor: Record<string, string> = {
  Safe: '#22C55E',
  Recommended: '#3B82F6',
  Optional: '#8888A0',
  Experimental: '#F59E0B',
  Risky: '#EF4444',
}

export default function Profiles() {
  const [activeProfile, setActiveProfile] = useState<string>('daily')
  const [expandedProfile, setExpandedProfile] = useState<string | null>(null)
  const [autoSelect, setAutoSelect] = useState(true)
  const [showConfirm, setShowConfirm] = useState(false)
  const [applying, setApplying] = useState(false)
  const [applyTarget, setApplyTarget] = useState<string | null>(null)
  const [confirmTweaks, setConfirmTweaks] = useState<TweakDef[]>([])
  const [applyResult, setApplyResult] = useState<ApplySummary | null>(null)

  function toggleExpand(id: string) {
    if (expandedProfile === id) {
      setExpandedProfile(null)
    } else {
      setExpandedProfile(id)
      setActiveProfile(id)
    }
  }

  async function handleApplyProfile(profileId: string) {
    // Fetch profile tweaks up front — the modal must not appear with an empty
    // list, or the user confirms the profile without seeing what it changes.
    setApplyTarget(profileId)
    try {
      const tweaks = await invokeJson<TweakDef[]>('list_tweaks', { profile: profileId })
      setConfirmTweaks(tweaks || [])
    } catch {
      setConfirmTweaks([])
    }
    setShowConfirm(true)
  }

  async function handleConfirmApply() {
    if (!applyTarget) return
    setApplying(true)
    setShowConfirm(false)
    setApplyResult(null)
    try {
      const res = await invokeJson<ApplySessionResult>('apply_profile', {
        profileId: applyTarget,
        dryRun: false,
      })
      const summary: ApplySummary = { ok: 0, fail: 0, skipped: 0, needAdmin: 0, errors: [] }
      if (res?.results?.length) {
        for (const r of res.results) {
          const bucket = tallyStatus(r.status)
          summary[bucket]++
          if (bucket === 'fail') summary.errors.push(`${r.tweakId}: ${r.message || r.status}`)
        }
      } else {
        summary.ok = res?.tweaksSucceeded ?? 0
        summary.fail = res?.tweaksFailed ?? 0
        summary.skipped = res?.tweaksSkipped ?? 0
        summary.needAdmin = res?.tweaksNeedElevation ?? 0
      }
      setApplyResult(summary)
      setApplyTarget(null)
    } catch (err) {
      console.error('Profile apply failed:', err)
      setApplyResult({
        ok: 0,
        fail: 1,
        skipped: 0,
        needAdmin: 0,
        errors: [String((err as Error)?.message || err)],
      })
    } finally {
      setApplying(false)
    }
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="animate-slide-up">
        <h1 className="text-2xl font-bold tracking-tight">Profiles</h1>
        <p className="text-[13px] text-[var(--color-text-muted)] mt-0.5">
          Choose an optimization profile — click to see exactly what each profile does
        </p>
      </div>

      {/* Apply result feedback */}
      {applyResult && (
        <div
          className={`card animate-slide-up border ${
            applyResult.fail > 0 ? 'border-[rgba(239,68,68,0.3)]' : 'border-[rgba(34,197,94,0.3)]'
          }`}
          style={{
            background: applyResult.fail > 0 ? 'rgba(239,68,68,0.05)' : 'rgba(34,197,94,0.05)',
          }}
        >
          <div className="flex items-center gap-3">
            {applyResult.fail > 0 ? (
              <AlertOctagon size={16} className="text-[var(--color-danger)] flex-shrink-0" />
            ) : (
              <CheckCircle2 size={16} className="text-[var(--color-success)] flex-shrink-0" />
            )}
            <div className="flex-1 min-w-0">
              <div className="text-[13px] font-bold">
                {[
                  `${applyResult.ok} applied`,
                  applyResult.fail > 0 ? `${applyResult.fail} failed` : null,
                  applyResult.skipped > 0 ? `${applyResult.skipped} skipped` : null,
                  applyResult.needAdmin > 0 ? `${applyResult.needAdmin} need administrator` : null,
                ]
                  .filter(Boolean)
                  .join(' · ')}
              </div>
              {applyResult.needAdmin > 0 && (
                <div className="mt-1 text-[11px] text-[var(--color-warning)]">
                  Run Novimize as administrator to apply{' '}
                  {applyResult.needAdmin === 1 ? 'that tweak' : 'those tweaks'} — they are
                  unchanged, not broken.
                </div>
              )}
              {applyResult.errors.length > 0 && (
                <div className="mt-1 text-[10px] text-[var(--color-text-muted)] max-h-[60px] overflow-y-auto">
                  {applyResult.errors.slice(0, 5).map((e, i) => (
                    <div key={i}>{e}</div>
                  ))}
                  {applyResult.errors.length > 5 && <div>+{applyResult.errors.length - 5} more</div>}
                </div>
              )}
            </div>
            <button
              onClick={() => setApplyResult(null)}
              className="text-[var(--color-text-muted)] hover:text-[var(--color-text)] p-1 text-[10px]"
            >
              Dismiss
            </button>
          </div>
        </div>
      )}

      {/* Auto-select toggle */}
      <div className="card flex items-center justify-between animate-slide-up stagger-1">
        <div className="flex items-center gap-3">
          <div className="w-8 h-8 rounded-lg flex items-center justify-center"
               style={{ background: 'linear-gradient(135deg, rgba(99,102,241,0.15), rgba(168,85,247,0.15))' }}>
            <Info size={15} className="text-[var(--color-primary)]" />
          </div>
          <div>
            <div className="text-[13px] font-semibold">Auto-Select Profile</div>
            <div className="text-[11px] text-[var(--color-text-muted)]">
              Automatically choose the best profile based on your hardware
            </div>
          </div>
        </div>
        <div
          className={`toggle ${autoSelect ? 'active' : ''}`}
          onClick={() => setAutoSelect(!autoSelect)}
        />
      </div>

      {/* Profile cards */}
      <div className="space-y-3">
        {allProfiles.map((profile, i) => {
          const isActive = activeProfile === profile.id
          const isExpanded = expandedProfile === profile.id

          return (
            <div
              key={profile.id}
              className={`card p-0 overflow-hidden animate-slide-up transition-all duration-200 ${
                isActive ? 'ring-1 ring-[rgba(99,102,241,0.3)]' : ''
              }`}
              style={{ animationDelay: `${(i + 1) * 40}ms` }}
            >
              {/* Profile header */}
              <button
                onClick={() => toggleExpand(profile.id)}
                className="w-full flex items-center justify-between px-5 py-4 hover:bg-[rgba(255,255,255,0.02)] transition-colors"
              >
                <div className="flex items-center gap-4">
                  <span className="text-2xl">{profile.icon}</span>
                  <div className="text-left">
                    <div className="flex items-center gap-2">
                      <span className="text-[14px] font-bold">{profile.name}</span>
                      {isActive && (
                        <div className="w-5 h-5 rounded-full flex items-center justify-center"
                             style={{ background: 'linear-gradient(135deg, #6366F1, #7C3AED)' }}>
                          <Check size={11} className="text-white" />
                        </div>
                      )}
                      {!profile.allowAutoOptimize && (
                        <span className="special-badge">
                          <AlertTriangle size={9} />
                          Manual Only
                        </span>
                      )}
                    </div>
                    <p className="text-[12px] text-[var(--color-text-muted)] mt-0.5 max-w-xl">
                      {profile.description}
                    </p>
                  </div>
                </div>

                <div className="flex items-center gap-3">
                  {/* Quick stats */}
                  <div className="flex items-center gap-1.5">
                    {profile.includeCategories.slice(0, 4).map(cat => (
                      <span key={cat} className="w-2 h-2 rounded-full" style={{ background: categoryColors[cat] || '#888' }} title={cat} />
                    ))}
                    {profile.includeCategories.length > 4 && (
                      <span className="text-[9px] text-[var(--color-text-muted)]">+{profile.includeCategories.length - 4}</span>
                    )}
                  </div>

                  {isExpanded ? (
                    <ChevronDown size={16} className="text-[var(--color-text-muted)]" />
                  ) : (
                    <ChevronRight size={16} className="text-[var(--color-text-muted)]" />
                  )}
                </div>
              </button>

              {/* Expanded detail */}
              {isExpanded && (
                <div className="px-5 pb-5 border-t border-[var(--color-border-subtle)] animate-fade-in">
                  <div className="grid grid-cols-2 gap-4 mt-4">
                    {/* Left: Requirements & Categories */}
                    <div className="space-y-4">
                      {/* Requirements */}
                      <div>
                        <h4 className="text-[11px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-2">Requirements</h4>
                        <div className="grid grid-cols-2 gap-2">
                          {profile.minTier && (
                            <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                              <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider">Min Tier</div>
                              <div className="text-[12px] font-semibold">{profile.minTier}</div>
                            </div>
                          )}
                          {profile.maxTier && (
                            <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                              <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider">Max Tier</div>
                              <div className="text-[12px] font-semibold">{profile.maxTier}</div>
                            </div>
                          )}
                          <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                            <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider">Max Risk</div>
                            <div className="text-[12px] font-semibold flex items-center gap-1">
                              <span className="w-1.5 h-1.5 rounded-full" style={{ background: riskColor[profile.maxRisk] }} />
                              {profile.maxRisk}
                            </div>
                          </div>
                          <div className="rounded-lg bg-[var(--color-bg-elevated)] px-3 py-2">
                            <div className="text-[9px] text-[var(--color-text-muted)] uppercase tracking-wider">Min Evidence</div>
                            <div className="text-[12px] font-semibold">{profile.minEvidence}/5</div>
                          </div>
                        </div>
                      </div>

                      {/* Categories */}
                      <div>
                        <h4 className="text-[11px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-2">
                          Included Categories ({profile.includeCategories.length})
                        </h4>
                        <div className="flex flex-wrap gap-1.5">
                          {profile.includeCategories.map(cat => (
                            <span key={cat} className="category-tag" style={{ borderColor: `${categoryColors[cat]}30`, color: categoryColors[cat] }}>
                              {cat}
                            </span>
                          ))}
                        </div>
                      </div>

                      {/* Excluded */}
                      {profile.excludeCategories.length > 0 && (
                        <div>
                          <h4 className="text-[11px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-2">
                            Excluded Categories
                          </h4>
                          <div className="flex flex-wrap gap-1.5">
                            {profile.excludeCategories.map(cat => (
                              <span key={cat} className="category-tag line-through opacity-50">
                                {cat}
                              </span>
                            ))}
                          </div>
                        </div>
                      )}
                    </div>

                    {/* Right: Tweak List */}
                    <div>
                      <h4 className="text-[11px] text-[var(--color-text-muted)] uppercase tracking-wider font-semibold mb-2">
                        Tweaks This Profile Will Apply
                      </h4>
                      <ProfileTweakList
                        profileId={profile.id}
                        onApply={(id) => handleApplyProfile(id)}
                        applying={applying && applyTarget === profile.id}
                      />
                    </div>
                  </div>
                </div>
              )}
            </div>
          )
        })}
      </div>

      {/* Confirmation Modal */}
      {applyTarget && (
        <ProfileConfirmModalHelper
          profileId={applyTarget}
          profiles={allProfiles}
          tweaks={confirmTweaks}
          open={showConfirm}
          onConfirm={handleConfirmApply}
          onCancel={() => { setShowConfirm(false); setApplyTarget(null) }}
          applying={applying}
        />
      )}
    </div>
  )
}

// Helper component: turns an already-fetched profile tweak list into the
// confirmation dialog. The fetch itself lives in handleApplyProfile so the
// list the user approves is the list that gets applied.
function ProfileConfirmModalHelper({
  profileId,
  profiles,
  tweaks,
  open,
  onConfirm,
  onCancel,
  applying,
}: {
  profileId: string
  profiles: Profile[]
  tweaks: TweakDef[]
  open: boolean
  onConfirm: () => void
  onCancel: () => void
  applying: boolean
}) {
  const profile = profiles.find(p => p.id === profileId)

  const confirmItems: ConfirmTweakItem[] = tweaks.map(t => ({
    id: t.id,
    name: t.name,
    description: t.description,
    risk: t.risk,
    method: t.method,
    targetValue: t.targetValue,
    defaultValue: t.defaultValue,
    registryKey: t.apply?.registryKey,
    registryValue: t.apply?.registryValue,
    serviceName: t.apply?.serviceName,
    isSpecial: t.risk === 'Experimental' || t.risk === 'Risky' || t.risk === 'Dangerous',
  }))

  return (
    <ConfirmModal
      open={open}
      title={`${profile?.icon || ''} Apply "${profile?.name || profileId}" Profile`}
      subtitle={`You are about to apply ${tweaks.length} tweaks from this profile`}
      tweaks={confirmItems}
      onConfirm={onConfirm}
      onCancel={onCancel}
      applying={applying}
    />
  )
}
