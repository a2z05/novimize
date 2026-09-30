export interface TweakDef {
  id: string
  name: string
  description: string
  category: string
  subcategory: string | null
  risk: string
  evidence: number
  method: string
  targetValue: string
  defaultValue: string
  profiles: string[]
  tags: string[]
  conflictsWith: string[]
  dependsOn: string[]
  detect: {
    command: string | null
    registryKey: string | null
    registryValue: string | null
    serviceName: string | null
    expectedApplied: string | null
    expectedDefault: string | null
    extractPattern: string | null
  }
  apply: {
    command: string | null
    registryKey: string | null
    registryValue: string | null
    registryData: string | null
    registryType: string | null
    serviceName: string | null
    serviceStartType: string | null
    serviceStop: boolean
    serviceStart: boolean
  }
}

export interface DetectionResult {
  tweakId: string
  state: string
  currentValue: string | null
  message: string | null
  detectionSucceeded: boolean
}

/** Per-tweak outcome of an apply run, as returned by the CLI's `--json`. */
export interface ApplyTweakResult {
  tweakId: string
  status: string
  message: string | null
  previousState: string | null
  currentState: string | null
  verified: boolean
}

/** Session totals of an apply run. `tweaksNeedElevation` is not a failure. */
export interface ApplySessionResult {
  sessionId?: string
  snapshotId?: string
  tweaksAttempted: number
  tweaksSucceeded: number
  tweaksSkipped?: number
  tweaksNeedElevation?: number
  tweaksFailed: number
  results?: ApplyTweakResult[]
}

export interface ConfirmTweakItem {
  id: string
  name: string
  description: string
  risk: string
  method: string
  targetValue: string
  defaultValue: string
  registryKey?: string | null
  registryValue?: string | null
  serviceName?: string | null
  isSpecial: boolean
}

/**
 * Outcome of an apply run, as shown in the UI's result card.
 * `needAdmin` and `skipped` are kept apart from `fail` on purpose: a tweak
 * refused for lack of elevation, or a measurement that changes nothing, is
 * not a broken tweak and should not be reported as one failure among the
 * real ones.
 */
export interface ApplySummary {
  ok: number
  fail: number
  skipped: number
  needAdmin: number
  errors: string[]
}

/** Bucket a CLI result status lands in. */
export function tallyStatus(
  status: string,
): keyof Pick<ApplySummary, 'ok' | 'fail' | 'skipped' | 'needAdmin'> {
  if (status === 'Success' || status === 'AlreadyApplied') return 'ok'
  if (status === 'RequiresElevation') return 'needAdmin'
  if (status === 'Skipped') return 'skipped'
  return 'fail'
}
