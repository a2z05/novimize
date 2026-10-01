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
  /** Where the change landed — `registry:HKCU\\...::Value`, `service:X`, … */
  target?: string
  previousValue?: string | null
  newValue?: string | null
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
  /** Held back by the planner before anything was attempted. */
  tweaksBlocked?: number
  tweaksFailed: number
  /** Present when the run was planned: what ran, in what order, and why not. */
  plan?: BatchPlan
  results?: ApplyTweakResult[]
}

// --- batch plan ---

export type PlanAction =
  | 'Apply'
  | 'MissingDependency'
  | 'Conflict'
  | 'BlockedByDependency'
  | 'DependencyCycle'

export interface PlanEntry {
  tweakId: string
  action: PlanAction
  reason: string
  blockedBy: string[]
  order: number
}

export interface TweakConflict {
  a: string
  b: string
  resource: string
  reason: string
}

export interface MissingDependency {
  tweakId: string
  requiredId: string
  /** False when the dependency is not merely out of this run but absent entirely. */
  requiredExists: boolean
}

/** What a run would do, computed before anything touches the system. */
export interface BatchPlan {
  entries: PlanEntry[]
  missingDependencies: MissingDependency[]
  conflicts: TweakConflict[]
  orderedTweakIds: string[]
  requestedCount: number
  applicableCount: number
  blockedCount: number
  hasIssues: boolean
}

export interface ConfirmTweakItem {
  id: string
  name: string
  description: string
  risk: string
  method: string
  targetValue: string
  defaultValue: string
  /**
   * What the scan found on this machine, when known. Shown in the confirm
   * modal instead of the catalogue default, because "1 → 0" is only useful if
   * the left-hand side is what the user actually has.
   */
  currentValue?: string | null
  registryKey?: string | null
  registryValue?: string | null
  serviceName?: string | null
  isSpecial: boolean
}

/**
 * Outcome of an apply run, as shown in the UI's result card.
 * `needAdmin`, `skipped` and `blocked` are kept apart from `fail` on purpose:
 * a tweak refused for lack of elevation, a measurement that changes nothing,
 * and a tweak held back by the planner before any command ran are none of
 * them broken tweaks, and reporting them as failures among the real ones
 * makes the real ones harder to see.
 */
export interface ApplySummary {
  ok: number
  fail: number
  skipped: number
  needAdmin: number
  blocked: number
  errors: string[]
}

export type TallyBucket = keyof Omit<ApplySummary, 'errors'>

/** Bucket a CLI result status lands in. */
export function tallyStatus(status: string): TallyBucket {
  if (status === 'Success' || status === 'AlreadyApplied') return 'ok'
  if (status === 'RequiresElevation') return 'needAdmin'
  if (status === 'Skipped') return 'skipped'
  if (status === 'Blocked') return 'blocked'
  return 'fail'
}
