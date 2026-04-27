namespace DeepSigma.Persistence;

/// <summary>Base exception for all errors raised by DeepSigma persistence backends.</summary>
public class PersistenceException : Exception
{
    /// <summary>Initializes a new instance with no message.</summary>
    public PersistenceException() { }
    /// <summary>Initializes a new instance with the specified <paramref name="message"/>.</summary>
    public PersistenceException(string message) : base(message) { }
    /// <summary>Initializes a new instance with the specified <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public PersistenceException(string message, Exception innerException) : base(message, innerException) { }
}
