namespace SanalBorsa.Application.Common.Interfaces;

/// <summary>
/// Portföy alım/satım işlemlerinde TL↔USD çevrimi için kullanılan anlık USD/TRY kuru.
/// </summary>
public interface IPortfolioFxRateProvider
{
    Task<decimal> GetUsdTryRateAsync(CancellationToken ct = default);
}
