namespace DeepSigma.Persistance.Core;

public static class KeyValidator
{
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
