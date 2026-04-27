namespace DeepSigma.Persistence;

public class PersistenceException : Exception
{
    public PersistenceException() { }
    public PersistenceException(string message) : base(message) { }
    public PersistenceException(string message, Exception innerException) : base(message, innerException) { }
}
