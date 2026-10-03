using WinOpt.Engine.Diagnostics;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The three exports. They have to survive being read back — an HTML file
/// that only opens in the browser it was made in, or a JSON document a script
/// cannot parse, is not an export.
/// </summary>
public class HealthReportTests
{
    private static HealthReport Sample() => new()
    {
        GeneratedAt = new DateTimeOffset(2026, 10, 3, 9, 41, 5, TimeSpan.Zero),
        OverallStatus = HealthStatus.Warning,
        Checks =
        {
            new HealthCheck { Name = "Defender", Details = "on, real-time protection enabled", Status = HealthStatus.Ok },
            new HealthCheck { Name = "Disk Space (C:)", Details = "16.8GB free (7.1%)", Status = HealthStatus.Warning },
            new HealthCheck { Name = "Windows Update", Details = "Service stopped", Status = HealthStatus.Critical },
            new HealthCheck { Name = "Odd <name> & \"quote\"", Details = "<script>alert(1)</script>", Status = HealthStatus.Ok },
        },
    };

    [Theory]
    [InlineData("json")]
    [InlineData("txt")]
    [InlineData("html")]
    public void Render_AcceptsTheThreeFormatsTheBriefAsksFor(string format)
    {
        var text = HealthReportWriter.Render(Sample(), format);

        Assert.False(string.IsNullOrWhiteSpace(text));
        // The JSON is the data itself with no banner on it; the other two
        // carry the title a person reads.
        Assert.Contains("Defender", text);
        if (format != "json") Assert.Contains("Novimize", text);
    }

    [Fact]
    public void Formats_AreExactlyJsonTxtAndHtml()
    {
        Assert.Equal(new[] { "json", "txt", "html" }, HealthReportWriter.Formats);
        Assert.Equal(".json", HealthReportWriter.ExtensionFor("json"));
        Assert.Equal(".txt", HealthReportWriter.ExtensionFor("txt"));
        Assert.Equal(".html", HealthReportWriter.ExtensionFor("html"));
        // The extension and the renderer have to agree, or the file is named
        // for one format and holds another.
        Assert.Equal(".html", HealthReportWriter.ExtensionFor("htm"));
    }

    [Fact]
    public void Json_ParsesBackAndCarriesEveryCheck()
    {
        var report = Sample();

        var round = System.Text.Json.JsonSerializer.Deserialize<HealthReportJson>(
            HealthReportWriter.ToJson(report),
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });

        Assert.NotNull(round);
        Assert.Equal(4, round!.Checks.Count);
        Assert.Equal(HealthStatus.Warning, round.OverallStatus);
        Assert.Equal(report.GeneratedAt, round.GeneratedAt);
        // The two checks that are not Ok are the ones the warnings list.
        Assert.Equal(2, round.Warnings.Count);
    }

    [Fact]
    public void Text_ShowsTheStatusOfEachCheckInWords()
    {
        var text = HealthReportWriter.ToText(Sample());

        Assert.Contains("Overall    Warning", text);
        Assert.Contains("CRIT", text);
        Assert.Contains("warn", text);
        Assert.Contains("Warnings", text);
        Assert.Contains("Service stopped", text);
    }

    [Fact]
    public void Html_EscapesAnythingThatCouldBeReadAsMarkup()
    {
        var html = HealthReportWriter.ToHtml(Sample());

        // The check named with angle brackets and a script tag must arrive as
        // text, not as markup — a report is a file somebody opens.
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;name&gt;", html);
        Assert.Contains("<!doctype html>", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void Write_PicksAPathUnderTheHealthFolderWhenNoneIsGiven()
    {
        var path = HealthReportWriter
            .WriteAsync(Sample(), "txt", output: null)
            .GetAwaiter().GetResult();

        try
        {
            Assert.EndsWith(".txt", path);
            Assert.Contains($"{Path.DirectorySeparatorChar}health{Path.DirectorySeparatorChar}", path);
            Assert.True(File.Exists(path));
            Assert.Contains("Defender", File.ReadAllText(path));
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Write_HonoursAnExplicitPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"novimize-health-{Guid.NewGuid():N}.json");
        try
        {
            var written = HealthReportWriter.WriteAsync(Sample(), "json", path).GetAwaiter().GetResult();

            Assert.Equal(path, written);
            Assert.True(File.Exists(path));
            System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp */ }
        }
    }

    /// <summary>The shape the JSON has to have for a script reading it back.</summary>
    private sealed class HealthReportJson
    {
        public DateTimeOffset GeneratedAt { get; init; }
        public HealthStatus OverallStatus { get; init; }
        public List<HealthCheck> Checks { get; init; } = new();
        public List<string> Warnings { get; init; } = new();
    }
}
