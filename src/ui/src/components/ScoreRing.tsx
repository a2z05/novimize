import { useEffect, useState } from 'react'

interface ScoreRingProps {
  score: number
  size?: number
  animated?: boolean
}

export default function ScoreRing({ score, size = 140, animated = true }: ScoreRingProps) {
  const [displayScore, setDisplayScore] = useState(animated ? 0 : score)
  const radius = (size - 14) / 2
  const circumference = 2 * Math.PI * radius
  const offset = circumference - (displayScore / 100) * circumference

  const color =
    score >= 70 ? '#22C55E' :
    score >= 40 ? '#F59E0B' :
    '#EF4444'

  const glowColor =
    score >= 70 ? 'rgba(34,197,94,0.25)' :
    score >= 40 ? 'rgba(245,158,11,0.25)' :
    'rgba(239,68,68,0.25)'

  useEffect(() => {
    if (!animated) { setDisplayScore(score); return }
    let frame: number
    const start = performance.now()
    const duration = 800
    const animate = (now: number) => {
      const elapsed = now - start
      const progress = Math.min(elapsed / duration, 1)
      // ease-out cubic
      const eased = 1 - Math.pow(1 - progress, 3)
      setDisplayScore(Math.round(eased * score))
      if (progress < 1) frame = requestAnimationFrame(animate)
    }
    frame = requestAnimationFrame(animate)
    return () => cancelAnimationFrame(frame)
  }, [score, animated])

  return (
    <div className="relative inline-flex items-center justify-center" style={{ width: size, height: size }}>
      {/* Glow effect behind */}
      <div className="absolute inset-0 rounded-full"
           style={{ boxShadow: `0 0 40px ${glowColor}, 0 0 80px ${glowColor}`, opacity: 0.6 }} />

      <svg width={size} height={size} className="-rotate-90 relative z-10">
        <defs>
          <linearGradient id="scoreGradient" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor={color} />
            <stop offset="100%" stopColor={score >= 70 ? '#10B981' : score >= 40 ? '#D97706' : '#DC2626'} />
          </linearGradient>
        </defs>
        {/* Track */}
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke="var(--color-border)"
          strokeWidth={7}
          opacity={0.5}
        />
        {/* Progress */}
        <circle
          cx={size / 2}
          cy={size / 2}
          r={radius}
          fill="none"
          stroke="url(#scoreGradient)"
          strokeWidth={7}
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          style={{ transition: animated ? 'stroke-dashoffset 800ms cubic-bezier(0.16,1,0.3,1)' : 'none' }}
        />
      </svg>

      {/* Center text */}
      <div className="absolute flex flex-col items-center z-20">
        <span className="font-bold tabular-nums" style={{ fontSize: size * 0.22, color, animation: 'countUp 400ms ease-out both' }}>
          {displayScore}
        </span>
        <span className="text-[10px] text-[var(--color-text-muted)] uppercase tracking-[0.12em] font-semibold -mt-0.5">
          score
        </span>
      </div>
    </div>
  )
}
