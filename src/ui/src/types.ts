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

// --- profile selection ---

/**
 * Why a tweak is not in a profile's default set. `None` means it is.
 * Mirrors the CLI's `ExclusionReason`; the enum is serialised as a string.
 */
export type ExclusionReason =
  | 'None'
  | 'LowEvidence'
  | 'HighRisk'
  | 'ExcludedById'
  | 'ExcludedCategory'
  | 'OutsideCategories'
  | 'SecurityBlocked'

/** One tweak a profile could apply, with the verdict on whether it does. */
export interface ProfileTweakVerdict {
  tweak: TweakDef
  inDefaultSet: boolean
  /**
   * True when the user can still opt in. Only tweaks that failed the evidence
   * or risk bar are offered; anything held by an explicit exclusion, an
   * excluded category, or the security guard is not a choice the profile
   * hands the user.
   */
  optInAvailable: boolean
  reason: ExclusionReason
  detail: string | null
}

/** What applying a profile actually does on this machine. */
export interface ProfileSelection {
  profileId: string
  maxRisk: string
  minEvidence: number
  /** Applied by the profile without the user asking for each one. */
  defaultSet: ProfileTweakVerdict[]
  /** Reachable, listed, not applied unless ticked. */
  optIn: ProfileTweakVerdict[]
  /** Outside the profile entirely — kept to explain an absence, not to offer it. */
  excluded: ProfileTweakVerdict[]
  /** True of the profile on this machine, not of any one tweak. */
  notices: string[]
  targetFormFactor: string | null
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

// --- Gaming Center ---

/**
 * How a game was identified. `manifest` is a launcher saying "this is mine";
 * `candidate` is a folder walk finding an executable. The two are never shown
 * the same way — the second is a guess and is labelled as one.
 */
export type GameSource = 'manifest' | 'candidate'

export interface GameEntry {
  name: string
  installPath: string | null
  launcher: string
  source: GameSource
  folder: string | null
}

export interface LauncherInfo {
  id: string
  name: string
  detected: boolean
  /** Which probe matched — `registry:...` or `filesystem:...` — so the answer can be checked. */
  evidence: string | null
  libraries: string[]
  installPath: string | null
}

export interface GameDetection {
  launchers: LauncherInfo[]
  games: GameEntry[]
  folders: string[]
  warnings: string[]
}

/** One thing a game-mode session changed, and what was in place before it. */
export interface GameModeControl {
  kind: string
  target: string
  description: string
  before: string | null
  beforeExisted: boolean
  after: string | null
  applied: boolean
  error: string | null
  restorable: boolean
}

export interface GameModeSession {
  id: string
  startedAt: string
  gamePath: string | null
  ownerProcess: string | null
  controls: GameModeControl[]
}

export interface GameModeStatus {
  active: boolean
  session: GameModeSession | null
  ownerRunning: boolean | null
  startedAt: string | null
  controls: GameModeControl[]
}

/** One shape for start and stop: what was attempted, what took, what did not. */
export interface GameModeResult {
  success: boolean
  message: string | null
  controls: GameModeControl[]
  status: GameModeStatus | null
}

export interface GameModePreset {
  name: string
  gamePath: string | null
  plan: string | null
  services: string[]
  notifications: boolean
  backgroundApps: boolean
  priorityProcess: string | null
}

// --- Defender exclusions ---

export interface DefenderExclusionState {
  paths: string[]
  processes: string[]
  elevationRequired: boolean
  /** False when the list could not be read at all — not the same as empty. */
  readable: boolean
  message: string | null
}

export interface ExclusionChange {
  action: string
  path: string
  success: boolean
  unchanged: boolean
  message: string | null
  before: string[]
  after?: string[]
}

// --- App Installer ---

/**
 * One app in the curated catalogue. Everything here is a fact someone wrote
 * down once; version and installed state are asked of winget when needed and
 * never stored here.
 */
export interface AppEntry {
  id: string
  name: string
  publisher: string | null
  description: string | null
  category: string
  homepage: string | null
  tags: string[]
  /** Where the project lives when the homepage is a store page. */
  github: string | null
  /** Finer grouping than the file it lives in — sections on one category. */
  subcategory: string | null
  license: string | null
  cost: string | null
  windows: string | null
  /** False when the tool has no winget package and only an official page. */
  winget: boolean
}

export interface AppCategory {
  id: string
  label: string
  description: string | null
}

/** A package the catalogue deliberately does not offer, and why. */
export interface CatalogueAbsence {
  name: string
  reason: string
}

export interface AppCatalogue {
  directory: string
  categories: AppCategory[]
  apps: AppEntry[]
  absent: CatalogueAbsence[]
}

/** A catalogue entry merged with what winget reported about this machine. */
export interface AppStatus {
  id: string
  name: string
  publisher: string | null
  description: string | null
  category: string
  homepage: string | null
  tags: string[]
  github: string | null
  subcategory: string | null
  license: string | null
  cost: string | null
  windows: string | null
  winget: boolean
  installed: boolean
  installedVersion: string | null
  availableVersion: string | null
  updateAvailable: boolean
  source: string | null
  outsideCatalogue: boolean
}

export interface AppStatusResult {
  available: boolean
  error: string | null
  version: string | null
  deep: boolean
  apps: AppStatus[]
}

/**
 * The authoritative answer for one package. The bulk pass cannot see copies
 * installed outside winget, so the modal asks again before offering Install.
 */
export interface InstalledCheck {
  id: string
  installed: boolean
  installedVersion: string | null
  availableVersion: string | null
  source: string | null
  authoritative: boolean
}

export interface WinGetSource {
  name: string
  argument: string
  explicit: boolean
}

export interface WinGetInfo {
  available: boolean
  version: string | null
  error: string | null
  sources: WinGetSource[]
}

export interface PackageDetail {
  id: string
  name: string
  version: string | null
  publisher: string | null
  publisherUrl: string | null
  homepage: string | null
  license: string | null
  description: string | null
  installerType: string | null
  installerUrl: string | null
  installerSha256: string | null
  releaseDate: string | null
  source: string | null
  error: string | null
  inCatalogue: boolean
  category: string | null
  catalogueName: string | null
  catalogueHomepage: string | null
  catalogueGithub: string | null
  subcategory: string | null
  catalogueLicense: string | null
  cost: string | null
  windows: string | null
  /** The catalogue's own verdict, not winget's: false means official page only. */
  winget: boolean
}

export interface AppSearchResult {
  id: string
  name: string
  version: string | null
  source: string | null
  installed: boolean
  installedVersion: string | null
  inCatalogue: boolean
  category: string | null
  /** False for Add/Remove Programs rows, whose "ID" is a registry path. */
  installable: boolean
}

export interface AppSearchResponse {
  query: string
  results: AppSearchResult[]
}

/** What one install / uninstall / upgrade did, with winget's own output kept. */
export interface AppChange {
  action: string
  id: string
  success: boolean
  unchanged: boolean
  message: string
  exitCode: number
  log: string
  scope: string
  needsElevation: boolean
}

/**
 * What an attempt to open a tool did. `matchedName` is the Start menu entry it
 * actually chose — the card name and the filed name differ often enough that
 * "opened something" is not the same claim as "opened this".
 */
export interface LaunchResult {
  id: string
  name: string
  success: boolean
  message: string
  matchedName: string | null
  matchedAppId: string | null
}
