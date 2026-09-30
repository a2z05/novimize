import { useEffect, useState } from 'react'
import { Zap, Battery, Cpu, Monitor, Gamepad2, Loader2 } from 'lucide-react'
import { invokeJson } from '../../hooks/useTauri'

interface SystemInfo {
  overallTier: number
  isLaptop: boolean
  formFactor: number
  cpuTier: number
  gpuTier: number
}

interface Profile {
  id: string
  name: string
  description: string
  riskLevel: string
  tags: string[]
}

interface ProfileStepProps {
  onNext: () => void
  selectedProfile: string | null
  setSelectedProfile: (id: string) => void
  systemInfo: SystemInfo | null
}

const PROFILE_ICONS: Record<string, any> = {
  'gaming': Gamepad2,
  'daily-driver': Monitor,
  'office': Cpu,
  'battery-saver': Battery,
  'potato-pc': Zap,
}

const PROFILE_COLORS: Record<string, string> = {
  'gaming': '#A855F7',
  'daily-driver': '#6366F1',
  'office': '#3B82F6',
  'battery-saver': '#22C55E',
  'potato-pc': '#F59E0B',
}

function getRecommendedProfile(info: SystemInfo | null): string {
  if (!info) return 'daily-driver'
  if (info.isLaptop) return 'battery-saver'
  // overallTier: 1=Ultra, 2=High, 3=Mid, 4=Low, 5=VeryLow
  switch (info.overallTier) {
    case 1: return 'gaming'
    case 2: return 'daily-driver'
    case 3: return 'office'
    case 4: return 'potato-pc'
    case 5: return 'potato-pc'
    default: return 'daily-driver'
  }
}

export default function ProfileStep({ onNext, selectedProfile, setSelectedProfile, systemInfo }: ProfileStepProps) {
  const [profiles, setProfiles] = useState<Profile[]>([])
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    async function load() {
      try {
        const data = await invokeJson<Profile[]>('list_profiles')
        if (!cancelled) {
          setProfiles(data)
          // Auto-select recommended if none selected
          if (!selectedProfile && data.length > 0) {
            const rec = getRecommendedProfile(systemInfo)
            const match = data.find(p => p.id === rec) || data[0]
            setSelectedProfile(match.id)
          }
        }
      } catch {
        // Fallback profiles
        const fallback: Profile[] = [
          { id: 'gaming', name: 'Gaming', description: 'Maximum performance', riskLevel: 'optional', tags: ['Performance'] },
          { id: 'daily-driver', name: 'Daily Driver', description: 'Balanced performance', riskLevel: 'recommended', tags: ['Balanced'] },
          { id: 'office', name: 'Office', description: 'Productivity focused', riskLevel: 'safe', tags: ['Productivity'] },
          { id: 'battery-saver', name: 'Battery Saver', description: 'Maximize battery life', riskLevel: 'safe', tags: ['Battery'] },
          { id: 'potato-pc', name: 'Potato PC', description: 'Ultra lightweight', riskLevel: 'safe', tags: ['Lightweight'] },
        ]
        if (!cancelled) {
          setProfiles(fallback)
          if (!selectedProfile) setSelectedProfile(getRecommendedProfile(systemInfo))
        }
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    load()
    return () => { cancelled = true }
  }, [])

  const recommended = getRecommendedProfile(systemInfo)

  return (
    <div className="flex flex-col items-center max-w-lg mx-auto w-full">
      <h2
        className="text-2xl font-bold tracking-tight mb-1 text-center"
        style={{ animation: 'slideUp 400ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        Choose Your Profile
      </h2>
      <p
        className="text-[13px] text-[var(--color-text-muted)] mb-6 text-center"
        style={{ animation: 'slideUp 400ms 50ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        We recommend <span className="text-[var(--color-primary)] font-semibold">{recommended.replace('-', ' ')}</span> based on your hardware
      </p>

      {loading ? (
        <div className="flex items-center gap-2 py-8">
          <Loader2 size={18} className="animate-spin text-[var(--color-primary)]" />
          <span className="text-[13px] text-[var(--color-text-muted)]">Loading profiles...</span>
        </div>
      ) : (
        <div className="w-full space-y-2">
          {profiles.map((profile, i) => {
            const isSelected = selectedProfile === profile.id
            const isRec = profile.id === recommended
            const Icon = PROFILE_ICONS[profile.id] || Zap
            const color = PROFILE_COLORS[profile.id] || '#6366F1'
            return (
              <button
                key={profile.id}
                onClick={() => setSelectedProfile(profile.id)}
                className="w-full flex items-center gap-3 rounded-xl px-4 py-3 text-left transition-all duration-200"
                style={{
                  background: isSelected ? 'var(--color-bg-card)' : 'transparent',
                  border: `1px solid ${isSelected ? `${color}50` : 'var(--color-border-subtle)'}`,
                  boxShadow: isSelected ? `0 0 20px ${color}15` : 'none',
                  animation: `slideUp 400ms ${i * 60}ms cubic-bezier(0.16,1,0.3,1) both`,
                }}
              >
                <div
                  className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                  style={{ background: `${color}15` }}
                >
                  <Icon size={16} style={{ color }} />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    <span className="text-[13px] font-semibold">{profile.name}</span>
                    {isRec && (
                      <span className="text-[9px] font-bold uppercase tracking-wider px-1.5 py-0.5 rounded bg-[rgba(99,102,241,0.15)] text-[var(--color-primary)]">
                        Recommended
                      </span>
                    )}
                  </div>
                  <div className="text-[11px] text-[var(--color-text-muted)]">{profile.description}</div>
                </div>
                {/* Radio indicator */}
                <div
                  className="w-4 h-4 rounded-full border-2 flex items-center justify-center flex-shrink-0 transition-colors"
                  style={{
                    borderColor: isSelected ? color : 'var(--color-border)',
                  }}
                >
                  {isSelected && (
                    <div className="w-2 h-2 rounded-full" style={{ background: color, animation: 'scaleIn 200ms cubic-bezier(0.16,1,0.3,1) both' }} />
                  )}
                </div>
              </button>
            )
          })}
        </div>
      )}

      {!loading && (
        <button
          onClick={onNext}
          disabled={!selectedProfile}
          className="btn btn-primary px-8 py-3 text-[14px] mt-6"
          style={{ animation: 'slideUp 400ms 300ms cubic-bezier(0.16,1,0.3,1) both' }}
        >
          Continue
        </button>
      )}
    </div>
  )
}
