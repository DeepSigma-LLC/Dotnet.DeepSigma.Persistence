namespace DeepSigma.Persistance;

public abstract class PersistenceOptions
{
    public int MaxKeyLength { get; set; } = 512;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the options are misconfigured.
    /// Called by each backend's <c>AddXxxPersistence</c> extension immediately after the
    /// caller's configure delegate runs, so errors surface at composition time rather than
    /// on the first repository call.
    /// </summary>
    /// <remarks>Backend option subclasses should override and call <c>base.Validate()</c>.</remarks>
    public virtual void Validate()
    {
        if (MaxKeyLength <= 0)
            throw new InvalidOperationException(
                $"{GetType().Name}.MaxKeyLength must be greater than 0 (got {MaxKeyLength}).");
    }
}
