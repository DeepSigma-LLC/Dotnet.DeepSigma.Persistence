using System.Security.Cryptography;
using System.Text;

namespace DeepSigma.Persistence.Core;

public static class KeyHasher
{
    /// <summary>Returns the lowercase SHA-256 hex digest of the UTF-8 encoded key.</summary>
    public static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    /// <summary>Returns the first two characters of the hash, used as the FileSystem shard directory name.</summary>
    public static string Shard(string key) => Hash(key)[..2];
}
