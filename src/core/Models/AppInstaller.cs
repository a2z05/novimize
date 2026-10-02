using System.Text.Json.Serialization;

namespace WinOpt.Core.Models;

/// <summary>
/// One app in the curated catalogue.
///
/// The catalogue is structured data on disk, not a table in a React file:
/// adding an app is editing a JSON file, and nothing in the UI needs to
/// know it happened. Only facts that are stable enough to write down live
/// here — anything that moves (version, installed state, publisher's current
/// description) is asked of winget when it is needed.
///
/// A record, because the loader stamps the category on from the file name and
/// does it by copying rather than by mutating shared state.
/// </summary>
public sealed record AppEntry
{
    /// <summary>Winget's package ID, e.g. <c>7zip.7zip</c>. The identity of the entry.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Lowercase category slug; the files are named after it.</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = new();
}

/// <summary>
/// The catalogue entry as the UI sees it, merged with what winget reported
/// about this machine. One row, so the card never has to join two lists.
/// </summary>
public sealed class AppStatus
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Publisher { get; init; }
    public string? Description { get; init; }
    public string Category { get; init; } = string.Empty;
    public string? Homepage { get; init; }
    public List<string> Tags { get; init; } = new();

    public bool Installed { get; init; }

    /// <summary>What winget says is installed. Not the catalogue — the machine.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>Set only when winget listed a newer version than the one installed.</summary>
    public string? AvailableVersion { get; init; }

    public bool UpdateAvailable => Installed && !string.IsNullOrWhiteSpace(AvailableVersion);

    /// <summary>The winget source the installed copy came from, when winget said.</summary>
    public string? Source { get; init; }

    /// <summary>Not in the catalogue but installed — offered for upgrade, not for install.</summary>
    public bool OutsideCatalogue { get; init; }
}

/// <summary>One row of a <c>winget list</c> / <c>winget search</c> table.</summary>
public sealed class InstalledPackage
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    /// <summary>Newer version winget knows about; null when there is none.</summary>
    [JsonPropertyName("available")]
    public string? Available { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>Whether winget is usable here, and what it is pointed at.</summary>
public sealed class WinGetInfo
{
    [JsonPropertyName("available")]
    public bool Available { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>Why it is not usable, when it is not. Null when everything is fine.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("sources")]
    public List<WinGetSource> Sources { get; init; } = new();
}

public sealed class WinGetSource
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("argument")]
    public string Argument { get; init; } = string.Empty;

    [JsonPropertyName("explicit")]
    public bool Explicit { get; init; }
}

/// <summary>
/// Everything <c>winget show</c> said about one package — fetched on demand
/// rather than guessed, because this is where "is this the real publisher"
/// gets answered. A record because the parser fills in everything but the ID
/// and the caller stamps that on by copy.
/// </summary>
public sealed record PackageDetail
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; init; }

    [JsonPropertyName("publisherUrl")]
    public string? PublisherUrl { get; init; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("installerType")]
    public string? InstallerType { get; init; }

    [JsonPropertyName("installerUrl")]
    public string? InstallerUrl { get; init; }

    /// <summary>The installer's hash as winget published it. Shown so the download
    /// can be checked rather than taken on trust.</summary>
    [JsonPropertyName("installerSha256")]
    public string? InstallerSha256 { get; init; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>Where an install is allowed to land — the answer changes what it costs.</summary>
public enum InstallScope
{
    /// <summary>Winget picks whatever the package manifest defaults to.</summary>
    Any,

    /// <summary>Current user only. No administrator rights.</summary>
    User,

    /// <summary>Everyone. Administrator rights.</summary>
    Machine,
}

/// <summary>
/// What one install / uninstall / upgrade did, with the command's own output
/// kept rather than summarised away — a failed installer says why in its log,
/// and that is the only place the reason usually survives.
/// </summary>
public sealed class AppChange
{
    [JsonPropertyName("action")]
    public string Action { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>True when the operation had nothing to do — already installed, not installed.</summary>
    [JsonPropertyName("unchanged")]
    public bool Unchanged { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>Winget's exit codes are HRESULTs, wider than an int — kept as a
    /// long so 0x8A150014 arrives as 2316632084 rather than as a negative.</summary>
    [JsonPropertyName("exitCode")]
    public long ExitCode { get; init; }

    /// <summary>stdout and stderr of the winget call, verbatim.</summary>
    [JsonPropertyName("log")]
    public string Log { get; init; } = string.Empty;

    /// <summary>The scope the install asked for, so the log can be read in context.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "any";

    /// <summary>Set when the operation refused because it needs administrator
    /// rights rather than because the package was wrong. The UI offers an
    /// elevated retry only when this is true — otherwise "try again as admin"
    /// is advice that cannot help.</summary>
    [JsonPropertyName("needsElevation")]
    public bool NeedsElevation { get; init; }
}
