namespace DeepSigma.Persistence.Core;

/// <summary>
/// Provides validation for keys used in the persistence layer. 
/// This includes checks for null, empty, length, and invalid characters (e.g., null bytes). 
/// The maximum length can be configured to accommodate different backend constraints. 
/// The streaming validation method allows for efficient validation of large collections of keys without buffering the entire sequence in memory.
/// </summary>
public static class KeyValidator
{
    /// <summary>
    /// Validates that the specified key is not null, empty, exceeds the maximum allowed length, or contains null bytes.
    /// </summary>
    /// <param name="key">The key to validate. Cannot be null, empty, longer than the specified maximum length, or contain null bytes.</param>
    /// <param name="maxLength">The maximum allowed length of the key. Must be a positive integer. Defaults to 512.</param>
    /// <exception cref="ArgumentException">Thrown if the key is empty, exceeds the specified maximum length, or contains null bytes.</exception>
    public static void Validate(string key, int maxLength = 512)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (key.Length == 0)
            throw new ArgumentException("Key must not be empty.", nameof(key));

        if (key.Length > maxLength)
            throw new ArgumentException(
                $"Key length {key.Length} exceeds the configured maximum of {maxLength}.", nameof(key));

        if (key.Contains('\0'))
            throw new ArgumentException("Key must not contain null bytes.", nameof(key));
    }

    /// <summary>
    /// Validates each key as it is enumerated. Streaming validation lets bulk callers
    /// chunk the result without buffering the whole sequence twice.
    /// </summary>
    public static IEnumerable<string> ValidateAll(IEnumerable<string> keys, int maxLength = 512)
    {
        foreach (var key in keys)
        {
            Validate(key, maxLength);
            yield return key;
        }
    }
}
