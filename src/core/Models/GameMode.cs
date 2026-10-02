using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// One thing a game-mode session changed, and what it found there first.
///
/// Every entry carries the value that was in place before the session touched
/// it, because the whole restore path is "put <see cref="Before"/> back". A
/// control recorded without one is a change that can never be undone, so
/// <see cref="GameModeSession"/> refuses to build a session that contains one.
/// </summary>
public sealed class GameModeControl
{
    /// <summary>Stable machine name: power-plan, notifications, background-apps, service, priority.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>What was written: scheme GUID, value name, service name, or process name.</summary>
    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty;

    /// <summary>The line a human reads under "Game mode active".</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Value found before the change. Null only when the setting did not exist.</summary>
    [JsonPropertyName("before")]
    public string? Before { get; init; }

    /// <summary>False when absence was the previous state — restore then deletes rather than writes.</summary>
    [JsonPropertyName("beforeExisted")]
    public bool BeforeExisted { get; init; }

    /// <summary>Value written by the session.</summary>
    [JsonPropertyName("after")]
    public string? After { get; init; }

    /// <summary>Whether the write took effect. A control that did not apply is reported, not hidden.</summary>
    [JsonPropertyName("applied")]
    public bool Applied { get; set; }

    /// <summary>Why it did not apply, when <see cref="Applied"/> is false.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>
    /// Whether stop can put the previous value back. False only for a change
    /// whose lifetime is bound to something else — a process priority belongs
    /// to a running process and disappears with it, so there is nothing left
    /// to restore once the game exits.
    /// </summary>
    [JsonPropertyName("restorable")]
    public bool Restorable { get; init; } = true;
}

/// <summary>
/// A running temporary optimisation session, and everything needed to undo it.
/// </summary>
public sealed class GameModeSession
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Game the session was started for, if one was named.</summary>
    [JsonPropertyName("gamePath")]
    public string? GamePath { get; init; }

    /// <summary>
    /// Process name the session is tied to. Status reports whether it is still
    /// running so the caller can decide to stop; nothing here watches it,
    /// because a CLI that blocks is one the desktop app cannot talk to.
    /// </summary>
    [JsonPropertyName("ownerProcess")]
    public string? OwnerProcess { get; init; }

    [JsonPropertyName("controls")]
    public List<GameModeControl> Controls { get; init; } = new();

    /// <summary>Controls that were asked for but could not be recorded with a previous value.</summary>
    [JsonIgnore]
    public IEnumerable<GameModeControl> AppliedControls => Controls.Where(c => c.Applied);
}

/// <summary>Answer to <c>game-mode status</c>.</summary>
public sealed class GameModeStatus
{
    [JsonPropertyName("active")]
    public bool Active { get; init; }

    [JsonPropertyName("session")]
    public GameModeSession? Session { get; init; }

    /// <summary>Whether the process the session is waiting on is still alive; null when none was named.</summary>
    [JsonPropertyName("ownerRunning")]
    public bool? OwnerRunning { get; init; }

    [JsonPropertyName("startedAt")]
    public DateTime? StartedAt { get; init; }

    [JsonPropertyName("controls")]
    public List<GameModeControl> Controls { get; init; } = new();
}

/// <summary>Where a game was found, and which probe found it.</summary>
public sealed class GameEntry
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("installPath")]
    public string? InstallPath { get; init; }

    /// <summary>Launcher that owns it, or "manual" for a folder the user added.</summary>
    [JsonPropertyName("launcher")]
    public string Launcher { get; init; } = string.Empty;

    /// <summary>
    /// How it was identified: <c>manifest</c> when a launcher published an
    /// authoritative record, <c>candidate</c> when it was found by walking a
    /// folder for an executable. The two are never presented the same way.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("folder")]
    public string? Folder { get; init; }
}

/// <summary>A launcher probe and what it turned up.</summary>
public sealed class LauncherInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("detected")]
    public bool Detected { get; init; }

    /// <summary>The probe that matched — registry key or path — or null when nothing did.</summary>
    [JsonPropertyName("evidence")]
    public string? Evidence { get; init; }

    /// <summary>Library folders known to this launcher.</summary>
    [JsonPropertyName("libraries")]
    public List<string> Libraries { get; init; } = new();

    [JsonPropertyName("installPath")]
    public string? InstallPath { get; init; }
}

/// <summary>Answer to <c>game-mode detect</c>.</summary>
public sealed class GameDetection
{
    [JsonPropertyName("launchers")]
    public List<LauncherInfo> Launchers { get; init; } = new();

    [JsonPropertyName("games")]
    public List<GameEntry> Games { get; init; } = new();

    /// <summary>Folders the user has added by hand.</summary>
    [JsonPropertyName("folders")]
    public List<string> Folders { get; init; } = new();

    /// <summary>Anything that could not be checked, said rather than passed over.</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; init; } = new();
}

/// <summary>A named set of controls, stored against a game.</summary>
public sealed class GameModePreset
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("gamePath")]
    public string? GamePath { get; init; }

    [JsonPropertyName("plan")]
    public string? Plan { get; init; }

    [JsonPropertyName("services")]
    public List<string> Services { get; init; } = new();

    [JsonPropertyName("notifications")]
    public bool Notifications { get; init; }

    [JsonPropertyName("backgroundApps")]
    public bool BackgroundApps { get; init; }

    [JsonPropertyName("priorityProcess")]
    public string? PriorityProcess { get; init; }
}

/// <summary>
/// Answer to <c>game-mode start</c> and <c>game-mode stop</c>.
///
/// One shape for both, because both are the same kind of report: what was
/// attempted, what took, and what did not. A partial run is a
/// <c>Success</c> of <c>false</c> with the controls that failed still in the
/// list — never a success with the failures left out.
/// </summary>
public sealed class GameModeResult
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>Every control attempted, applied or not.</summary>
    [JsonPropertyName("controls")]
    public List<GameModeControl> Controls { get; init; } = new();

    [JsonPropertyName("status")]
    public GameModeStatus? Status { get; init; }
}
