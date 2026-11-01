using Shouldly;

namespace Hexalith.Works.IntegrationTests;

/// <summary>Only the exact empty SDK owner lock may be exclusively held while every persisted payload remains byte-checked.</summary>
public sealed class WorksCapturedConfigurationFileTests
{
    [Theory]
    [InlineData("held-owner-lock")]
    [InlineData("stopped-file")]
    [InlineData("unlocked-running-owner")]
    [InlineData("locked-ordinary-file")]
    [InlineData("nonempty-owner")]
    [InlineData("nonempty-capture")]
    [InlineData("changed-payload")]
    [InlineData("missing")]
    [InlineData("mode")]
    public async Task ExactLockLifetimeDoesNotHideUnreadableOrChangedConfiguration(string fault)
    {
        string root = Directory.CreateTempSubdirectory("works-captured-config-").FullName;
        try
        {
            string path = Path.Combine(root, "owner.lock");
            byte[] expected = fault is "stopped-file" or "changed-payload" ? [1, 2, 3] : fault == "nonempty-capture" ? [1] : [];
            File.WriteAllBytes(path, fault == "changed-payload" ? [9] : fault == "nonempty-owner" ? [1] : expected);
            UnixFileMode? mode = OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, mode!.Value);
            if (fault == "missing") File.Delete(path);
            if (fault == "mode" && !OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead);
            bool held = fault is "held-owner-lock" or "locked-ordinary-file";
            using FileStream? owner = held ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
            bool runningLock = fault is not ("stopped-file" or "locked-ordinary-file" or "changed-payload");
            Task Verify() => WorksCapturedConfigurationFile.VerifyAsync(path, expected, mode, runningLock, TestContext.Current.CancellationToken);
            if (fault is "held-owner-lock" or "stopped-file") await Verify().ConfigureAwait(true);
            else await Should.ThrowAsync<Exception>(Verify).ConfigureAwait(true);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ArbitraryIoErrorsNeverProveExclusiveOwnership()
    {
        WorksCapturedConfigurationFile.IsSharingViolation(new IOException("unknown")).ShouldBeFalse();
        WorksCapturedConfigurationFile.IsSharingViolation(new IOException("access", 13)).ShouldBeFalse();
        WorksCapturedConfigurationFile.IsSharingViolation(new FileNotFoundException()).ShouldBeFalse();
    }
}
