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
            .ForMember(d => d.Symbol, o => o.MapFrom(s => s.Stock.Symbol))
            .ForMember(d => d.ActionTypeName, o => o.MapFrom(s => s.ActionType.ToString()));
    }
}
