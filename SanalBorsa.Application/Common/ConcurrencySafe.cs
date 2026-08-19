using SanalBorsa.Application.Common.Exceptions;
using SanalBorsa.Domain.Interfaces;

namespace SanalBorsa.Application.Common;

/// <summary>
/// Alım/satım handler'ları için optimistic-concurrency retry sarmalayıcısı. Portföy Cash'i
/// "oku → kontrol et → yaz" şeklinde işlendiğinden (transaction/lock yok), eşzamanlı iki işlem
/// aynı bakiyeyi görüp ikisi de geçebilirdi — <see cref="Exceptions.ConcurrencyConflictException"/>
/// (UserPortfolio.RowVersion çakışmasından doğar) yakalanıp portföy sıfırdan okunarak birkaç kez
/// tekrar denenir; her deneme fiyatı/bakiyeyi baştan görür, yani "stale" veriyle asla yazılmaz.
/// </summary>
public static class ConcurrencySafe
{
    private const int MaxAttempts = 5;

    public static async Task<T> RunAsync<T>(IUnitOfWork uow, Func<Task<T>> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                uow.ClearChanges();
            }
        }
    }
}
