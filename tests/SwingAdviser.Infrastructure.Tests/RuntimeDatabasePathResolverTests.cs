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
}
