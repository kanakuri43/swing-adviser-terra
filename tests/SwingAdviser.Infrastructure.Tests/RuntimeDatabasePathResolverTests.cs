using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using SwingAdviser.Infrastructure.Persistence;

namespace SwingAdviser.Infrastructure.Tests;

public class RuntimeDatabasePathResolverTests
{
    [Fact]
    public void ResolveWritableDatabasePath_WithWritableDirectory_ReturnsExpectedPath()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("swing-adviser-tests-").FullName;

        try
        {
            var path = RuntimeDatabasePathResolver.ResolveWritableDatabasePath(tempDirectory);

            Assert.Equal(Path.Combine(tempDirectory, "swing-adviser.db"), path);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ResolveWritableDatabasePath_WithNonExistentDirectory_Throws()
    {
        var nonExistentDirectory = Path.Combine(Path.GetTempPath(), $"swing-adviser-missing-{Guid.NewGuid():N}");

        Assert.Throws<InvalidOperationException>(
            () => RuntimeDatabasePathResolver.ResolveWritableDatabasePath(nonExistentDirectory));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void ResolveWritableDatabasePath_WithAclDeniedDirectory_ThrowsWithoutImplicitFallback()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("swing-adviser-acl-denied-").FullName;
        var directoryInfo = new DirectoryInfo(tempDirectory);
        var currentUser = WindowsIdentity.GetCurrent().User!;
        var accessControl = directoryInfo.GetAccessControl();
        var denyRule = new FileSystemAccessRule(
            currentUser,
            FileSystemRights.WriteData | FileSystemRights.CreateFiles,
            AccessControlType.Deny);

        try
        {
            accessControl.AddAccessRule(denyRule);
            directoryInfo.SetAccessControl(accessControl);

            var exception = Assert.Throws<InvalidOperationException>(
                () => RuntimeDatabasePathResolver.ResolveWritableDatabasePath(tempDirectory));
            Assert.Contains(tempDirectory, exception.Message);
        }
        finally
        {
            accessControl.RemoveAccessRule(denyRule);
            directoryInfo.SetAccessControl(accessControl);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
