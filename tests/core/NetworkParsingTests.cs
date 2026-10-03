using WinOpt.Core.Models;
using WinOpt.Engine.Network;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The three parsers here read console output that Windows prints in the
/// user's own language. They are tested against the shapes that actually
/// appear — stars, "&lt;1 ms", blank lines, localized prose around numbers that
/// are not localized — because a parse that only works on English output is
/// a parse that fails on most machines.
/// </summary>
public class NetworkParsingTests
{
    [Fact]
    public void ParsePing_ReadsTheSummaryBlock()
    {
        const string output = """
            Pinging 1.1.1.1 with 32 bytes of data:
            Reply from 1.1.1.1: bytes=32 time=68ms TTL=57
            Reply from 1.1.1.1: bytes=32 time=70ms TTL=57
            Reply from 1.1.1.1: bytes=32 time=67ms TTL=57

            Ping statistics for 1.1.1.1:
                Packets: Sent = 3, Received = 3, Lost = 0 (0% loss),
            Approximate round trip times in milli-seconds:
                Minimum = 67ms, Maximum = 70ms, Average = 68ms
            """;

        var report = DnsManager.ParsePing("1.1.1.1", output);

        Assert.Equal(3, report.Sent);
        Assert.Equal(0, report.Lost);
        Assert.Equal(67, report.Min);
        Assert.Equal(70, report.Max);
        Assert.Equal(68, report.Average);
        Assert.Equal(3, report.Times.Count);
        Assert.Null(report.Error);
        Assert.Equal(0, report.LossPercent);
    }

    [Fact]
    public void ParsePing_ReadsLossWhenOnlyThePercentageIsPrinted()
    {
        // A localized build that prints no "Lost =" still prints the percent.
        const string output = """
            Packets: Sent = 4, Received = 3, Lost = 1 (25% loss),
            """;

        var report = DnsManager.ParsePing("8.8.8.8", output);

        Assert.Equal(4, report.Sent);
        Assert.Equal(1, report.Lost);
        Assert.Equal(25, report.LossPercent);
    }

    [Fact]
    public void ParsePing_WithNoAnswerAtAllReportsAnErrorNotZeroPackets()
    {
        const string output = """
            Ping request could not find host nope.test. Please check the name and try again.
            """;

        var report = DnsManager.ParsePing("nope.test", output);

        Assert.Equal(0, report.Sent);
        Assert.NotNull(report.Error);
        Assert.Contains("nope.test", report.Error);
    }

    [Fact]
    public void ParsePing_CountsStarsAsTimeoutsRatherThanAsTimes()
    {
        const string output = """
            Pinging 10.0.0.1 with 32 bytes of data:
            Request timed out.
            Reply from 10.0.0.1: bytes=32 time=2ms TTL=64
            Request timed out.
            Reply from 10.0.0.1: bytes=32 time=3ms TTL=64

            Ping statistics for 10.0.0.1:
                Packets: Sent = 4, Received = 2, Lost = 2 (50% loss),
            """;

        var report = DnsManager.ParsePing("10.0.0.1", output);

        Assert.Equal(4, report.Sent);
        Assert.Equal(2, report.Lost);
        Assert.Equal(new[] { 2, 3 }, report.Times);
        Assert.Equal(2, report.Average);
    }

    [Fact]
    public void ParseTracert_ReadsHopsTimingsAndAddresses()
    {
        const string output = """
            Tracing route to 1.1.1.1 over maximum 30 hops:

              1    <1 ms    <1 ms    <1 ms  192.168.1.1
              2     9 ms    10 ms     9 ms  10.40.0.1
              3     *        *        *     请求超时。
              4    68 ms    67 ms    69 ms  1.1.1.1

            Trace complete.
            """;

        var report = NetToolbox.ParseTracert("1.1.1.1", output);

        Assert.Equal(4, report.Hops.Count);
        Assert.Equal("192.168.1.1", report.Hops[0].Host);
        // "<1 ms" is under a millisecond; recording it as zero keeps the
        // average meaningful instead of inventing a millisecond.
        Assert.Equal(new[] { 0, 0, 0 }, report.Hops[0].Times);
        Assert.Equal("10.40.0.1", report.Hops[1].Host);
        Assert.Equal(9, report.Hops[1].Times[0]);
        Assert.Equal("*", report.Hops[2].Host);
        Assert.Empty(report.Hops[2].Times);
        Assert.Equal("1.1.1.1", report.Hops[3].Host);
        Assert.Null(report.Error);
    }

    [Fact]
    public void ParseTracert_WithNothingButAHeaderReportsAnError()
    {
        const string output = """
            Unable to resolve target name nope.test: Host not found.
            """;

        var report = NetToolbox.ParseTracert("nope.test", output);

        Assert.Empty(report.Hops);
        Assert.NotNull(report.Error);
    }

    [Fact]
    public void ParseTracert_IgnoresLinesThatAreNotHops()
    {
        const string output = """
            Tracing route to example.com [93.184.216.34]
            over maximum 30 hops:

              1     1 ms     1 ms     1 ms  192.168.1.1

            Trace complete.
            """;

        var report = NetToolbox.ParseTracert("example.com", output);

        var hop = Assert.Single(report.Hops);
        Assert.Equal(1, hop.Hop);
        Assert.Equal("192.168.1.1", hop.Host);
    }

    [Theory]
    [InlineData("ipconfig /flushdns", new[] { "ipconfig", "/flushdns" })]
    [InlineData("powershell -Command \"Restart-NetAdapter -Name 'Wi Fi'\"",
        new[] { "powershell", "-Command", "Restart-NetAdapter -Name 'Wi Fi'" })]
    [InlineData("netsh int ip reset", new[] { "netsh", "int", "ip", "reset" })]
    public void SplitCommandLine_KeepsQuotedArgumentsTogether(string command, string[] expected)
    {
        Assert.Equal(expected, NetToolbox.SplitCommandLine(command));
    }

    /// <summary>
    /// The preview is also the command that runs, so it has to survive being
    /// split back apart. "Wi Fi" is the common adapter name that breaks this.
    /// </summary>
    [Theory]
    [InlineData("Ethernet")]
    [InlineData("Wi Fi")]
    [InlineData("vEthernet (Default Switch)")]
    public void PSRestart_SurvivesBeingSplitBackIntoArguments(string adapter)
    {
        var parts = NetToolbox.SplitCommandLine(NetToolbox.PSRestart(adapter));

        Assert.Equal("powershell", parts[0]);
        Assert.Equal("-Command", parts[3]);
        Assert.Equal(NetToolbox.RestartBody(adapter), parts[4]);
        Assert.Contains($"-Name '{adapter}'", parts[4]);
    }

    /// <summary>
    /// The answer that decides whether the page says "you are on Cloudflare"
    /// or "you are on something we do not recognise". Every branch matters:
    /// a partial match must not be reported as the provider.
    /// </summary>
    [Theory]
    [InlineData("1.1.1.1", "", "cloudflare")]
    [InlineData("1.1.1.1,1.0.0.1", "2606:4700:4700::1111", "cloudflare")]
    [InlineData("8.8.8.8", "", "google")]
    [InlineData("9.9.9.9", "", "quad9")]
    [InlineData("94.140.14.14", "", "adguard")]
    [InlineData("45.90.28.0", "", "nextdns")]
    [InlineData("", "", "none")]
    // Half of one provider and half of another is not either of them.
    [InlineData("1.1.1.1,8.8.8.8", "", "custom")]
    // A single foreign address on an otherwise matching list is "custom".
    [InlineData("1.1.1.1,203.0.113.9", "", "custom")]
    public void ActiveProvider_OnlyClaimsAMatchWhenEveryAddressBelongs(
        string ipv4Csv, string ipv6Csv, string expected)
    {
        var adapters = new[]
        {
            new AdapterDns
            {
                Name = "Ethernet",
                Ipv4 = ipv4Csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                Ipv6 = ipv6Csv.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            },
        };

        Assert.Equal(expected, DnsManager.ActiveProvider(adapters));
    }

    [Fact]
    public void ActiveProvider_ReportsNoneWhenNoAdapterHasAResolver()
    {
        var adapters = new[]
        {
            new AdapterDns { Name = "Ethernet" },
            new AdapterDns { Name = "Wi-Fi" },
        };

        Assert.Equal("none", DnsManager.ActiveProvider(adapters));
    }

    /// <summary>
    /// Windows prints the withdrawn fec0::/10 anycast servers on any adapter
    /// with no IPv6 DNS. Showing them would read as configuration that is not
    /// there, and letting them reach the provider match would turn a machine
    /// that is plainly on Cloudflare into "custom".
    /// </summary>
    [Theory]
    [InlineData("fec0:0:0:ffff::1", true)]
    [InlineData("FEC0::1", true)]
    [InlineData("2606:4700:4700::1111", false)]
    [InlineData("2001:4860:4860::8888", false)]
    [InlineData("fe80::1", false)]
    public void IsDeprecatedSiteLocal_FindsOnlyTheWithdrawnPrefix(string address, bool expected)
    {
        Assert.Equal(expected, DnsManager.IsDeprecatedSiteLocal(address));
    }

    [Fact]
    public void ActiveProvider_SeesThroughAnAdapterWithNoRealResolver()
    {
        // The shape of a real machine: one adapter on Cloudflare, one
        // disconnected and empty, one virtual with nothing of its own.
        var adapters = new[]
        {
            new AdapterDns { Name = "Ethernet 8" },
            new AdapterDns { Name = "Ethernet", Ipv4 = new() { "1.1.1.1", "1.0.0.1" } },
            new AdapterDns { Name = "vEthernet" },
        };

        Assert.Equal("cloudflare", DnsManager.ActiveProvider(adapters));
    }

    /// <summary>
    /// "Disconnected" contains the word "Connected". Matching on the substring
    /// put a dead NIC in the change list, and the user would have been shown a
    /// command for an adapter that has nothing to configure. Naming an adapter
    /// explicitly still takes it — setting DNS before the cable is plugged in
    /// is a normal thing to want — but the default set stays live-only.
    /// </summary>
    [Fact]
    public void Targets_TakesOnlyAdaptersThatAreUpUnlessOneWasNamed()
    {
        var status = new DnsStatus
        {
            Adapters = new()
            {
                new AdapterDns { Name = "Ethernet 8", Status = "Disconnected", Index = 10 },
                new AdapterDns { Name = "Ethernet", Status = "Up", Index = 20 },
                new AdapterDns { Name = "Wi-Fi", Status = "Disabled", Index = 30 },
            },
        };

        var only = Assert.Single(DnsManager.Targets(status, null));
        Assert.Equal(20, only.Index);

        Assert.Equal(10, Assert.Single(DnsManager.Targets(status, "Ethernet 8")).Index);
        Assert.Equal(30, Assert.Single(DnsManager.Targets(status, "Wi-Fi")).Index);
        Assert.Empty(DnsManager.Targets(status, "no-such-adapter"));
    }

    /// <summary>
    /// The catalogue is the one place a resolver address is written down, so
    /// a typo there would be a typo on every machine. Each entry must carry
    /// both families unless it has a documented reason not to, and must never
    /// ship a DoH template for a server Windows does not know.
    /// </summary>
    [Fact]
    public void Providers_CarryBothFamiliesOrSayWhyNot()
    {
        var providers = DnsManager.Providers;

        Assert.Equal(5, providers.Count);
        Assert.Equal(providers.Count, providers.Select(p => p.Id).Distinct().Count());

        foreach (var provider in providers)
        {
            Assert.NotEmpty(provider.Ipv4);
            Assert.All(provider.Ipv4, a => Assert.Matches(@"^\d{1,3}(\.\d{1,3}){3}$", a));
            Assert.All(provider.Ipv6, a => Assert.Contains(':', a));

            // Only the three Windows itself lists may claim a DoH endpoint.
            if (provider.DohTemplate is not null)
                Assert.StartsWith("https://", provider.DohTemplate);
        }

        // Windows' own DoH table covers exactly these three.
        Assert.NotNull(providers.Single(p => p.Id == "cloudflare").DohTemplate);
        Assert.NotNull(providers.Single(p => p.Id == "google").DohTemplate);
        Assert.NotNull(providers.Single(p => p.Id == "quad9").DohTemplate);
        // The two it does not cover say so rather than guessing.
        Assert.Null(providers.Single(p => p.Id == "adguard").DohTemplate);
        Assert.Null(providers.Single(p => p.Id == "nextdns").DohTemplate);
    }
}
