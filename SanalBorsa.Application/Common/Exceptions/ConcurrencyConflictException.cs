namespace SanalBorsa.Application.Common.Exceptions;

/// <summary>
/// Aynı portföy üzerinde eşzamanlı iki işlem (ör. iki alım/satım) çakıştığında fırlatılır —
/// bkz. <see cref="ConcurrencySafe"/> bu istisnayı yakalayıp otomatik tekrar dener.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException()
        : base("Portföy üzerinde eşzamanlı bir işlem tespit edildi.") { }

    public ConcurrencyConflictException(Exception inner)
        : base("Portföy üzerinde eşzamanlı bir işlem tespit edildi.", inner) { }
}
