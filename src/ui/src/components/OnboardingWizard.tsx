import { useState, useCallback, useEffect } from 'react'
import WelcomeStep from './onboarding/WelcomeStep'
import DetectStep from './onboarding/DetectStep'
import ProfileStep from './onboarding/ProfileStep'
import ScanStep from './onboarding/ScanStep'
import DoneStep from './onboarding/DoneStep'

const STEPS = ['welcome', 'detect', 'profile', 'scan', 'done'] as const
type Step = typeof STEPS[number]

const STEP_LABELS: Record<Step, string> = {
  welcome: 'Welcome',
  detect: 'Detect',
  profile: 'Profile',
  scan: 'Scan',
  done: 'Done',
}

interface SystemInfo {
  osCaption: string
  osVersion: string
  cpuName: string
  cpuCores: number
  cpuTier: number
  ramTotalGb: number
  ramTier: number
  primaryStorageType: number
  primaryStorageModel: string
  storageTier: number
  gpuName: string
  gpuVendor: string
  gpuRamMb: number
  gpuTier: number
  overallTier: number
  formFactor: number
  isLaptop: boolean
  activePowerPlan: string
}

interface OnboardingWizardProps {
  onComplete: () => void
}

export default function OnboardingWizard({ onComplete }: OnboardingWizardProps) {
  const [step, setStep] = useState<Step>('welcome')
  const [direction, setDirection] = useState<'forward' | 'backward'>('forward')
  const [systemInfo, setSystemInfo] = useState<SystemInfo | null>(null)
  const [selectedProfile, setSelectedProfile] = useState<string | null>(null)
  const [animating, setAnimating] = useState(false)

  const stepIndex = STEPS.indexOf(step)

  const goTo = useCallback((target: Step) => {
    if (animating) return
    const targetIdx = STEPS.indexOf(target)
    setDirection(targetIdx > stepIndex ? 'forward' : 'backward')
    setAnimating(true)
    setStep(target)
  }, [animating, stepIndex])

  useEffect(() => {
    if (animating) {
      const timer = setTimeout(() => setAnimating(false), 300)
      return () => clearTimeout(timer)
    }
  }, [animating, step])

  const goNext = useCallback(() => {
    if (stepIndex < STEPS.length - 1) goTo(STEPS[stepIndex + 1])
  }, [stepIndex, goTo])

  const goPrev = useCallback(() => {
    if (stepIndex > 0) goTo(STEPS[stepIndex - 1])
  }, [stepIndex, goTo])

  // Keyboard navigation
  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape' && step !== 'welcome' && step !== 'done') {
        goPrev()
      }
    }
    window.addEventListener('keydown', handleKey)
    return () => window.removeEventListener('keydown', handleKey)
  }, [step, goPrev])

  const renderStep = () => {
    switch (step) {
      case 'welcome':
        return <WelcomeStep onNext={goNext} />
      case 'detect':
        return <DetectStep onNext={goNext} systemInfo={systemInfo} setSystemInfo={setSystemInfo} />
      case 'profile':
        return <ProfileStep onNext={goNext} selectedProfile={selectedProfile} setSelectedProfile={setSelectedProfile} systemInfo={systemInfo} />
      case 'scan':
        return <ScanStep onNext={goNext} />
      case 'done':
        return <DoneStep onComplete={onComplete} />
    }
  }

  return (
    <div
      className="fixed inset-0 z-50 flex flex-col overflow-hidden"
      role="dialog"
      aria-label="Setup wizard"
      style={{ background: 'var(--color-bg)' }}
    >
      {/* Animated background mesh */}
      <div className="absolute inset-0 pointer-events-none overflow-hidden">
        <div
          className="absolute w-[600px] h-[600px] rounded-full opacity-[0.03]"
          style={{
            background: 'radial-gradient(circle, #6366F1 0%, transparent 70%)',
            top: '-200px',
            right: '-100px',
            animation: 'gradient-shift 20s ease infinite',
            backgroundSize: '200% 200%',
          }}
        />
        <div
          className="absolute w-[400px] h-[400px] rounded-full opacity-[0.03]"
          style={{
            background: 'radial-gradient(circle, #A855F7 0%, transparent 70%)',
            bottom: '-150px',
            left: '-50px',
            animation: 'gradient-shift 25s ease infinite reverse',
            backgroundSize: '200% 200%',
          }}
        />
      </div>

      {/* Step indicator */}
      <div className="relative z-10 flex items-center justify-center gap-2 pt-8 pb-4">
        {STEPS.map((s, i) => (
          <div key={s} className="flex items-center gap-2">
            <button
              onClick={() => {
                if (i < stepIndex) goTo(s)
              }}
              disabled={i >= stepIndex}
              className="flex items-center justify-center transition-all duration-300"
              aria-label={`Step ${i + 1}: ${STEP_LABELS[s]}`}
              aria-current={s === step ? 'step' : undefined}
            >
              <div
                className="w-7 h-7 rounded-full flex items-center justify-center text-[11px] font-bold transition-all duration-300"
                style={{
                  background: i < stepIndex
                    ? 'linear-gradient(135deg, #22C55E, #10B981)'
                    : i === stepIndex
                      ? 'linear-gradient(135deg, #6366F1, #7C3AED)'
                      : 'var(--color-bg-elevated)',
                  color: i <= stepIndex ? 'white' : 'var(--color-text-muted)',
                  boxShadow: i === stepIndex ? '0 0 16px rgba(99,102,241,0.3)' : 'none',
                  cursor: i < stepIndex ? 'pointer' : 'default',
                }}
              >
                {i < stepIndex ? '✓' : i + 1}
              </div>
            </button>
            {i < STEPS.length - 1 && (
              <div
                className="w-12 h-0.5 rounded-full transition-all duration-500"
                style={{
                  background: i < stepIndex
                    ? 'linear-gradient(90deg, #22C55E, #10B981)'
                    : 'var(--color-bg-elevated)',
                }}
              />
            )}
          </div>
        ))}
      </div>

      {/* Step label */}
      <div className="relative z-10 text-center mb-2">
        <span className="text-[10px] font-semibold uppercase tracking-widest text-[var(--color-text-muted)]">
          {STEP_LABELS[step]} {stepIndex + 1}/{STEPS.length}
        </span>
      </div>

      {/* Content area */}
      <div className="relative z-10 flex-1 flex items-center justify-center px-8 pb-8 overflow-y-auto">
        <div
          key={step}
          style={{
            animation: `${direction === 'forward' ? 'wizardSlideIn' : 'wizardSlideInRight'} 350ms cubic-bezier(0.16,1,0.3,1) both`,
          }}
        >
          {renderStep()}
        </div>
      </div>

      {/* Back button (hidden on first/last step) */}
      {step !== 'welcome' && step !== 'done' && (
        <button
          onClick={goPrev}
          className="absolute bottom-6 left-8 z-20 btn btn-ghost btn-sm text-[var(--color-text-muted)]"
          aria-label="Go back"
        >
          ← Back
        </button>
      )}
    </div>
  )
}
