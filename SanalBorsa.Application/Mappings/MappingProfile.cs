using AutoMapper;
using SanalBorsa.Application.DTOs;
using SanalBorsa.Domain.Entities;

namespace SanalBorsa.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Stock, StockDto>();

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
