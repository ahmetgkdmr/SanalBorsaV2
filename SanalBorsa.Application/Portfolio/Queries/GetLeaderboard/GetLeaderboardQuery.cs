using MediatR;
using SanalBorsa.Application.DTOs;

namespace SanalBorsa.Application.Portfolio.Queries.GetLeaderboard;

/// <summary>
/// Sanal portföyünü en çok büyüten kullanıcıların sıralaması.
/// </summary>
/// <param name="Take">Kaç kişi dönecek (podyum + liste).</param>
public record GetLeaderboardQuery(int Take = 50) : IRequest<LeaderboardDto>;
