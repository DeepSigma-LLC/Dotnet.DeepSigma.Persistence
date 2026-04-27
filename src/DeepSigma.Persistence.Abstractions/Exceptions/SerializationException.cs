namespace DeepSigma.Persistence;

/// <summary>Thrown when a value cannot be serialized to or deserialized from JSON.</summary>
public class SerializationException : PersistenceException
{
    /// <summary>Initializes a new instance with no message.</summary>
    public SerializationException() { }
    /// <summary>Initializes a new instance with the specified <paramref name="message"/>.</summary>
    public SerializationException(string message) : base(message) { }
    /// <summary>Initializes a new instance with the specified <paramref name="message"/> and <paramref name="innerException"/>.</summary>
    public SerializationException(string message, Exception innerException) : base(message, innerException) { }
}
