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

// ── Blocker ─────────────────────────────────────────────────────────────────

/**
 * Why a rule exists. These stay separate because "ads" and "telemetry" and
 * "malware" are different claims with different costs when they turn out to
 * be wrong — and `Software` in particular is never presented as anything
 * other than an optional network endpoint rule.
 */
export type BlockCategory =
  | 'Ads'
  | 'Trackers'
  | 'Telemetry'
  | 'Malware'
  | 'Analytics'
  | 'Software'
  | 'Custom'

/** How much a rule is expected to break if it turns out to be wrong. */
export type BlockSeverity = 'Low' | 'Medium' | 'High'

/** Which mechanism enforces a rule. */
export type BlockKind = 'Hosts' | 'Firewall'

export interface BlockRule {
  /** Domain, IP, or the firewall rule name. Unique within its kind. */
  id: string
  kind: BlockKind
  /** The address or rule value as written on disk. */
  value: string
  category: BlockCategory
  /** What this blocks and why, in one sentence. */
  purpose: string | null
  /** Where it came from: a source id, "custom", or "import". */
  source: string | null
  sourceUrl: string | null
  severity: BlockSeverity
  enabled: boolean
  /**
   * False for a hosts line found outside the managed section. The page lists
   * those so the count is honest, and never offers a control for them.
   */
  managed: boolean
  /** For firewall rules: the executable, service, or address they apply to. */
  application: string | null
  addedAt: string | null
  updatedAt: string | null
}

/** A blocklist somebody else publishes, referred to but never shipped. */
export interface BlockSource {
  id: string
  name: string
  category: BlockCategory
  /** Where to fetch the list from. Shown before anything is downloaded. */
  url: string
  homepage: string | null
  license: string | null
  purpose: string | null
  /** What stops working if this list is applied. Null means "nothing known". */
  breakage: string | null
  format: string
  severity: BlockSeverity
  tags: string[]
}

/** A list as it currently stands on this machine, before any change. */
export interface BlockFetchResult {
  source: string
  url: string | null
  cachedAt: string | null
  bytes: number
  domains: number
  sha256: string | null
  /** Domains this fetch has that the applied copy does not. */
  added: number
  /** Domains the applied copy has that this fetch does not. */
  removed: number
  fetchedAt: string
  /** True when nothing has been applied yet, so there is no diff to show. */
  firstTime: boolean
}

/** What one write to the hosts file or the firewall did. */
export interface BlockChange {
  action: string
  success: boolean
  /** True when the machine was already in the requested state. */
  unchanged: boolean
  message: string
  /** How many managed rules the action touched or left behind. */
  affected: number
  /** Where the file stood before the first write, if a backup was taken. */
  backup: string | null
  /** Set when the operation could not proceed without administrator rights. */
  needsElevation: boolean
  log: string
}

/** One source that has been applied to the hosts file, and when. */
export interface AppliedSource {
  source: string
  name: string | null
  domains: number
  appliedAt: string
  updatedAt: string | null
  url: string | null
}

/** Everything the Blocker page reads in one pass. */
export interface BlockerStatus {
  hostsPath: string
  hostsExists: boolean
  /** True when markers were found but do not pair up; nothing can be written. */
  hostsMalformed: boolean
  managed: number
  enabled: number
  unmanaged: number
  backupExists: boolean
  backupPath: string | null
  writable: boolean
  firewallRules: number
  firewallReadable: boolean
  sources: BlockSource[]
  applied: AppliedSource[]
  rules: BlockRule[]
}

// ── Network ─────────────────────────────────────────────────────────────────

/** A public resolver somebody else runs. Latency is measured, never stored. */
export interface DnsProvider {
  id: string
  name: string
  ipv4: string[]
  ipv6: string[]
  /**
   * The RFC 8484 endpoint, or null when it could not be confirmed. Null means
   * plain DNS only for this provider — the page says so rather than guessing.
   */
  dohTemplate: string | null
  homepage: string | null
  note: string | null
  latencyMs: number | null
  packetLoss: number | null
}

export interface AdapterDns {
  name: string
  index: number
  description: string
  status: string
  mac: string
  ipv4: string[]
  ipv6: string[]
  dhcp4: boolean
  dhcp6: boolean
  changedByNovimize: boolean
  changedAt: string | null
  previous4: string[] | null
  previous6: string[] | null
  networkCategory: string | null
  gateway: string | null
}

export interface DohServer {
  address: string
  template: string | null
  allowFallback: boolean
  autoUpgrade: boolean
}

export interface DnsStatus {
  adapters: AdapterDns[]
  doh: DohServer[]
  providers: DnsProvider[]
  /** Which catalogue entry every resolver list matches, or "custom" / "none". */
  activeProvider: string
  ipv6Available: boolean
  /** False when there is no v6 default route, so v6 servers cannot be reached. */
  ipv6Reachable: boolean
  resolverInfo: string | null
  measured: boolean
}

/** What one network operation did, with the commands it ran or would run. */
export interface NetworkChange {
  action: string
  success: boolean
  unchanged: boolean
  message: string
  affected: number
  needsElevation: boolean
  log: string
  /** Command lines, in the order they run. Shown before the action. */
  preview: string[]
  restartRequired: boolean
}

export interface PingReport {
  host: string
  sent: number
  lost: number
  min: number | null
  max: number | null
  average: number | null
  times: number[]
  error: string | null
}

export interface TraceHop {
  hop: number
  host: string
  times: number[]
}

export interface TraceReport {
  host: string
  hops: TraceHop[]
  targetIp: string | null
  reached: boolean
  error: string | null
}

export interface LookupRecord {
  name: string
  type: string
  data: string
  ttl: number
}

export interface LookupReport {
  query: string
  type: string
  server: string | null
  records: LookupRecord[]
  elapsedMs: number
  error: string | null
}

export interface AdapterInfo {
  name: string
  description: string
  status: string
  mac: string
  kind: string
  physical: boolean
  ipv4: string[]
  ipv6: string[]
  gateway: string | null
  dhcp: string | null
  mtu: number | null
  metric: number | null
  networkCategory: string | null
  dns4: string | null
}

export interface RouteEntry {
  destination: string
  prefix: string
  nextHop: string
  interface: string
  metric: string
  protocol: string
  store: string
}

export interface NetworkProfileInfo {
  name: string
  interfaceAlias: string
  category: string
  ipv4Connectivity: string
  ipv6Connectivity: string
}

export interface TcpSnapshot {
  values: { key: string; value: string | null }[]
  congestionProvider: string | null
  rtt: number | null
  initialWindow: string | null
  error: string | null
}

/** Everything the Network page reads in one pass. */
export interface NetworkOverview {
  dns: DnsStatus
  adapters: AdapterInfo[]
  routes: RouteEntry[]
  profiles: NetworkProfileInfo[]
  tcp: TcpSnapshot
  gateway: string | null
  localIp: string | null
  mtu: number | null
  ipv6Available: boolean
  publicIp: string | null
  publicIpNote: string | null
  error: string | null
}

// ── Power ───────────────────────────────────────────────────────────────────

/** One power setting as the active plan has it. */
export interface PowerSetting {
  id: string
  name: string
  /** Already in its own unit — "30 min", "100%", "Enabled". */
  value: string
  /** The same value on battery, when the plan keeps two. */
  batteryValue: string | null
  unit: string
  note: string | null
  /** True when the plan does not expose this setting at all. */
  unavailable: boolean
  /** Set when another part of Novimize already manages this. */
  existingTweak: string | null
}

export interface PowerPlan {
  guid: string
  name: string
  active: boolean
  builtIn: boolean
}

export interface BatteryState {
  present: boolean
  percent: number | null
  status: string | null
  minutesRemaining: number | null
  onAc: boolean
}

export interface PowerStatus {
  plans: PowerPlan[]
  activePlan: string
  activePlanGuid: string
  settings: PowerSetting[]
  battery: BatteryState
  /** Chassis first, battery second — never guessed from a missing battery. */
  formFactor: string
  isLaptop: boolean
  previousPlanGuid: string | null
  previousPlanName: string | null
  ultimateAvailable: boolean
  error: string | null
}

/** What one power change did, with the powercfg lines it ran or would run. */
export interface PowerChange {
  action: string
  success: boolean
  unchanged: boolean
  message: string
  affected: number
  log: string | null
  preview: string[]
  restartRequired: boolean
}

// ── Startup ─────────────────────────────────────────────────────────────────

/** Where a startup entry lives. */
export type StartupKind =
  | 'Run'
  | 'StartupFolder'
  | 'CommonStartupFolder'
  | 'ScheduledTask'
  | 'StartupTask'

/**
 * One thing that runs at sign-in. Novimize never removes these: disabling
 * writes the flag Windows itself reads, so restoring is writing it back.
 */
export interface StartupItem {
  /** Stable key: the source, the hive, and the entry's own name. */
  id: string
  name: string
  publisher: string | null
  /** The command exactly as it is registered. */
  command: string
  /** The executable the command points at, when one could be found. */
  targetPath: string | null
  location: string
  kind: StartupKind
  hive: string
  taskPath: string | null
  enabled: boolean
  /** False when changing it will need administrator rights. */
  writable: boolean
  /** Broken | Heavy | Medium | Low | Unknown — a guess, labelled as one. */
  impact: string
  impactReason: string
  broken: boolean
}

export interface StartupStatus {
  items: StartupItem[]
  userStartupFolder: string
  commonStartupFolder: string
  enabledCount: number
  disabledCount: number
  brokenCount: number
  error: string | null
}

/** What flipping one startup entry did, with the write it performed. */
export interface StartupChange {
  action: string
  success: boolean
  unchanged: boolean
  message: string
  affected: number
  needsElevation: boolean
  log: string | null
  preview: string[]
}

// ── Services and scheduled tasks ────────────────────────────────────────────

export type ServiceStartMode = 'Boot' | 'System' | 'Automatic' | 'Manual' | 'Disabled'

/**
 * One Windows service. `protected` is a refusal, not a filter: the reason is
 * carried with it so the row can say which rule applied.
 */
export interface ServiceEntry {
  name: string
  displayName: string
  description: string
  publisher: string
  status: string
  startMode: ServiceStartMode
  path: string | null
  /** Services that must be running for this one to run. */
  requires: string[]
  /** Services that need this one running — the reason not to stop it. */
  dependentOn: string[]
  protected: boolean
  protectReason: string | null
  /** Where the start mode stood before Novimize first changed it. */
  originalStartMode: ServiceStartMode | null
}

export interface ServiceStatus {
  services: ServiceEntry[]
  running: number
  stopped: number
  protectedCount: number
  elevationKnown: boolean
  error: string | null
}

export interface ServiceChange {
  action: string
  success: boolean
  unchanged: boolean
  message: string
  affected: number
  needsElevation: boolean
  log: string | null
  preview: string[]
}

/** One scheduled task, with everything worth showing about it. */
export interface ScheduledTaskEntry {
  name: string
  path: string
  /** Base64url of the folder plus the name — the pair uniquely identifies a task. */
  id: string
  taskPath: string
  state: string
  enabled: boolean
  author: string
  description: string
  trigger: string
  command: string
  arguments: string
  workingDirectory: string
  lastRun: string | null
  nextRun: string | null
  /** HRESULT from the last run — 0x800710E0-style values do not fit an int. */
  lastResult: number
  systemTask: boolean
  changedByNovimize: boolean
  originalEnabled: boolean | null
}

export interface ScheduledTaskStatus {
  tasks: ScheduledTaskEntry[]
  enabled: number
  disabled: number
  changedByNovimize: number
  error: string | null
}

export interface ScheduledTaskChange {
  action: string
  success: boolean
  unchanged: boolean
  message: string
  affected: number
  needsElevation: boolean
  log: string | null
  preview: string[]
}
