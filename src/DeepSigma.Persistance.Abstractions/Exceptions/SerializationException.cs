namespace DeepSigma.Persistence;

public class SerializationException : PersistenceException
{
    public SerializationException() { }
    public SerializationException(string message) : base(message) { }
    public SerializationException(string message, Exception innerException) : base(message, innerException) { }
}
