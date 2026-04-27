using System.Security.Cryptography;
using System.Text;

namespace DeepSigma.Persistence.Core;

/// <summary>
/// Provides utility methods for generating SHA-256 hashes and shard directory names from string keys.
/// </summary>
/// <remarks>
/// This class is static and cannot be instantiated. It is intended for use in scenarios where
/// consistent, case-insensitive hash values and shard directory names are required, such as file system storage or
/// distributed caching.
/// </remarks>
public static class KeyHasher
{
    /// <summary>Returns the lowercase SHA-256 hex digest of the UTF-8 encoded key.</summary>
    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Returns the first two characters of the hash, used as the FileSystem shard directory name.</summary>
    public static string Shard(string key) => Hash(key)[..2];
}
