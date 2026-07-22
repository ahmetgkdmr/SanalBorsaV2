using AutoMapper;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Stock, StockDto>()
            .ConstructUsing(s => new StockDto(
                s.Id,
                s.Symbol,
                s.Name,
                s.Sector,
                s.Industry,
                s.Currency,
                s.Exchange,
                s.IsActive,
                s.EarliestDataDate,
                s.LatestDataDate,
                s.NeedsHistoryRefresh,
                s.MarketType == Domain.Entities.MarketType.Crypto ? "crypto" : "bist",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null));

        CreateMap<StockPriceHistory, PriceHistoryDto>();

        CreateMap<CorporateAction, CorporateActionDto>()
            .ConstructUsing(src => new CorporateActionDto(
                src.Id,
                src.Stock != null ? src.Stock.Symbol : string.Empty,
                src.ActionType,
                src.ActionType.ToString(),
                src.ActionDate,
                src.Value,
                src.SubscriptionPrice,
                src.Description,
                src.CreatedAt));
    }
}
