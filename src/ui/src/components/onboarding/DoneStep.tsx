import { useNavigate } from 'react-router-dom'
import { CheckCircle2, ArrowRight, ScanSearch, Shield, RotateCcw } from 'lucide-react'

interface DoneStepProps {
  onComplete: () => void
}

const FEATURES = [
  { icon: ScanSearch, label: 'Scan', desc: 'Detect tweaks', color: '#6366F1' },
  { icon: Shield, label: 'Optimize', desc: 'Apply safely', color: '#22C55E' },
  { icon: RotateCcw, label: 'Rollback', desc: 'Undo anytime', color: '#F59E0B' },
]

export default function DoneStep({ onComplete }: DoneStepProps) {
  const navigate = useNavigate()

  function handleOpen() {
    onComplete()
    navigate('/')
  }

  return (
    <div className="flex flex-col items-center text-center max-w-lg mx-auto">
      {/* Success icon */}
      <div
        className="w-20 h-20 rounded-full flex items-center justify-center mb-6"
        style={{
          background: 'linear-gradient(135deg, rgba(34,197,94,0.15), rgba(16,185,129,0.15))',
          border: '1px solid rgba(34,197,94,0.3)',
          boxShadow: '0 0 60px rgba(34,197,94,0.15)',
          animation: 'scaleIn 500ms cubic-bezier(0.16,1,0.3,1) both',
        }}
      >
        <CheckCircle2 size={36} className="text-[var(--color-success)]" />
      </div>

      <h2
        className="text-2xl font-bold tracking-tight mb-2"
        style={{ animation: 'slideUp 400ms 100ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        You're All Set!
      </h2>

      <p
        className="text-[14px] text-[var(--color-text-muted)] leading-relaxed mb-8 max-w-sm"
        style={{ animation: 'slideUp 400ms 200ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        Novimize is configured and ready. You can scan, optimize, and rollback
        changes anytime from the dashboard.
      </p>

      {/* Feature highlights */}
      <div
        className="w-full grid grid-cols-3 gap-3 mb-8"
        style={{ animation: 'slideUp 400ms 300ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        {FEATURES.map((item, i) => (
          <div
            key={item.label}
            className="rounded-xl px-3 py-4 text-center"
            style={{
              background: 'var(--color-bg-card)',
              border: '1px solid var(--color-border)',
              animation: `slideUp 400ms ${300 + i * 80}ms cubic-bezier(0.16,1,0.3,1) both`,
            }}
          >
            <item.icon size={18} style={{ color: item.color }} className="mx-auto mb-1.5" />
            <div className="text-[13px] font-semibold">{item.label}</div>
            <div className="text-[10px] text-[var(--color-text-muted)] mt-0.5">{item.desc}</div>
          </div>
        ))}
      </div>

      <button
        onClick={handleOpen}
        className="btn btn-primary px-8 py-3 text-[14px] gap-2.5"
        style={{ animation: 'slideUp 400ms 500ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        Open Dashboard
        <ArrowRight size={16} />
      </button>
    </div>
  )
}
