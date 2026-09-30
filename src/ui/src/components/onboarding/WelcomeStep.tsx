import { Rocket } from 'lucide-react'

interface WelcomeStepProps {
  onNext: () => void
}

export default function WelcomeStep({ onNext }: WelcomeStepProps) {
  return (
    <div className="flex flex-col items-center text-center max-w-lg mx-auto">
      {/* Logo */}
      <div
        className="mb-8"
        style={{
          animation: 'scaleIn 500ms cubic-bezier(0.16,1,0.3,1) both',
        }}
      >
        <img
          src="/novimize-icon.png"
          alt="Novimize"
          className="w-24 h-24 rounded-3xl"
          style={{
            boxShadow: '0 0 60px rgba(99,102,241,0.15)',
          }}
        />
      </div>

      {/* Heading */}
      <h1
        className="text-3xl font-bold tracking-tight mb-3"
        style={{ animation: 'slideUp 500ms 100ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        Welcome to{' '}
        <span
          style={{
            background: 'linear-gradient(135deg, #00D4FF, #2563EB)',
            WebkitBackgroundClip: 'text',
            WebkitTextFillColor: 'transparent',
          }}
        >
          Novimize
        </span>
      </h1>

      <p
        className="text-[15px] text-[var(--color-text-muted)] leading-relaxed mb-10 max-w-md"
        style={{ animation: 'slideUp 500ms 200ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        The local-first PC optimizer that gives you full control. Let's set up
        your system in just a few steps.
      </p>

      {/* CTA */}
      <button
        onClick={onNext}
        className="btn btn-primary px-8 py-3 text-[14px] gap-2.5"
        style={{ animation: 'slideUp 500ms 300ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        <Rocket size={16} />
        Get Started
      </button>

      {/* Privacy note */}
      <p
        className="text-[11px] text-[var(--color-text-muted)] mt-6 opacity-60"
        style={{ animation: 'slideUp 500ms 400ms cubic-bezier(0.16,1,0.3,1) both' }}
      >
        100% local — no data leaves your PC
      </p>
    </div>
  )
}
