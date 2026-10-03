using WinOpt.Engine.Services;
using WinOpt.Engine.Tasks;
using Xunit;

namespace WinOpt.Core.Tests;

/// <summary>
/// The two guards that decide whether a write is attempted at all: which
/// services are refused outright, and whether a task's identity survives a
/// round trip through an id a URL or a command line can carry.
/// </summary>
public class ServicesAndTasksTests
{
    [Theory]
    [InlineData("WinDefend")]
    [InlineData("windefend")]
    [InlineData("mpssvc")]
    [InlineData("EventLog")]
    [InlineData("RpcSs")]
    [InlineData("Dhcp")]
    [InlineData("Schedule")]
    public void ProtectedServices_RefuseCaseInsensitivelyWithAReason(string name)
    {
        var refused = ServiceManager.IsProtected(name, out var reason);

        Assert.True(refused);
        Assert.NotEmpty(reason);
        // The reason has to say which rule applied, not just "no".
        Assert.Contains(" ", reason);
    }

    [Theory]
    [InlineData("Spooler")]
    [InlineData("DiagTrack")]
    [InlineData("WSearch")]
    [InlineData("SomeThirdParty")]
    public void ProtectedServices_LeavesUserServiceableOnesAlone(string name)
    {
        Assert.False(ServiceManager.IsProtected(name, out var reason));
        Assert.Empty(reason);
    }

    /// <summary>
    /// The list exists so that a critical service cannot be reached by any
    /// path, so every entry on it has to carry the sentence shown to the user.
    /// </summary>
    [Fact]
    public void EveryProtectedService_ExplainsWhy()
    {
        Assert.NotEmpty(ServiceManager.ProtectedServices);
        foreach (var (name, reason) in ServiceManager.ProtectedServices)
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.True(reason.Length > 20, $"'{name}' has no useful reason: '{reason}'");
        }
    }

    [Theory]
    [InlineData("\\Microsoft\\Windows\\Defender\\Scheduled Scan")]
    [InlineData("\\")]
    [InlineData("\\Updater\\Weekly")]
    [InlineData("\\Odd\\Name with spaces\\task")]
    public void TaskId_RoundTripsTheFolderAndNameExactly(string path)
    {
        var id = TaskManager.Encode(path);

        // Base64url: no '+' or '/' to be read as a separator by anything
        // that passes this around.
        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('+', id);
        Assert.Equal(path, TaskManager.Decode(id));
    }

    [Fact]
    public void TaskId_CollidesForDifferentTasksOnlyWhenThePairIsTheSame()
    {
        var a = TaskManager.Encode("\\A\\DoThing");
        var b = TaskManager.Encode("\\B\\DoThing");
        var c = TaskManager.Encode("\\A\\DoThing");

        Assert.NotEqual(a, b);
        Assert.Equal(a, c);
    }

    [Fact]
    public void TaskId_DecodeOfSomethingThatIsNotAnIdComesBackUnchanged()
    {
        // A caller that passes a raw path instead of an id must not silently
        // receive a different string back.
        var raw = "\\Microsoft\\Windows\\Defender\\Scheduled Scan";

        Assert.Equal(raw, TaskManager.Decode(raw));
    }
}
