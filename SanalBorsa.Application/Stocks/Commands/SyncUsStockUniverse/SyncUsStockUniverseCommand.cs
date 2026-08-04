using MediatR;

namespace SanalBorsa.Application.Stocks.Commands.SyncUsStockUniverse;

/// <summary>
/// UsStockSymbolSeed'teki pilot sembolleri Stock satırına çevirir (yoksa oluşturur, varsa dokunmaz).
/// Mevcut SyncStockUniverseCommand'a bilinçli olarak dokunulmadı — o BIST'e sabit (.IS eki,
/// Currency="TRY", MarketType.Bist hardcoded); bu ayrı, basit komut BIST akışına sıfır risk taşır.
/// </summary>
public record SyncUsStockUniverseCommand : IRequest<SyncUsStockUniverseResult>;

public record SyncUsStockUniverseResult(int Added, int AlreadyExisting);
