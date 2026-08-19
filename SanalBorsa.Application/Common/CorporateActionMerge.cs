using SanalBorsa.Domain.Entities;
using SanalBorsa.Domain.Enums;

namespace SanalBorsa.Application.Common;

/// <summary>KAP birincil kaynak — resmi kamu aydınlatma platformu, parser'ı sağlamlaştırıldı.
/// İş Yatırım sadece KAP'ın kaçırdığı (aynı tarih+tür'de KAP kaydı OLMAYAN) olayları tamamlar —
/// proje sohbeti: KAP 2010 öncesine (ve bazı stocklarda 2010 sonrasına da) hiç gitmiyor, o dönem
/// için İş Yatırım tek kaynak. Aynı (tarih, tür) çiftinde ikisi de varsa KAP'ınki kazanır.
/// Hem gerçek senkron (SyncCorporateActionsCommandHandler) hem tanı uçları (StocksController)
/// AYNI mantığı kullansın diye ortak bir yerde tutuluyor.</summary>
public static class CorporateActionMerge
{
    public static List<CorporateAction> MergePreferKap(
        IReadOnlyList<CorporateAction> kapActions,
        IReadOnlyList<CorporateAction> isYatirimActions)
    {
        var merged = new Dictionary<(DateTime Date, CorporateActionType Type), CorporateAction>();
        foreach (var a in isYatirimActions)
            merged[(a.ActionDate.Date, a.ActionType)] = a;
        foreach (var a in kapActions)
            merged[(a.ActionDate.Date, a.ActionType)] = a;

        return merged.Values
            .OrderBy(a => a.ActionDate)
            .ThenBy(a => a.ActionType)
            .ToList();
    }
}
