using DeepSigma.Persistance.FileSystem;
using DeepSigma.Persistance.Testing;

namespace DeepSigma.Persistance.FileSystem.Tests;

public sealed class FileSystemRepositoryTests
    : ExpiringRepositoryContractTests<FileSystemRepository<string>>, IDisposable
{
    private readonly List<string> _tempDirs = [];

    protected override FileSystemRepository<string> CreateRepository()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ds-fs-{Guid.NewGuid():N}");
        _tempDirs.Add(path);
        return new FileSystemRepository<string>(new FileSystemOptions { RootPath = path });
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
            TryDeleteDir(dir);
    }

    private static void TryDeleteDir(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
