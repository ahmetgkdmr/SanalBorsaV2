using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Portfolio.Queries.GetPublicTradeHistory;

/// <summary>
/// Bir kullanıcının HERKESE AÇIK işlem geçmişi — liderlik tablosundaki profil detayı için.
/// Sadece kullanıcı <c>ShowTradeHistoryPublic</c> ayarını açtıysa veri döner.
/// </summary>
public record GetPublicTradeHistoryQuery(string Username, int Take = 50)
    : IRequest<PublicTradeHistoryDto>;
