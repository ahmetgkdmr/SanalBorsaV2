namespace SanalBorsa.Domain.Entities;

/// <summary>
/// Bir kurumsal olayın (bedelsiz/bedelli/temettü) belirli bir kullanıcı portföyüne UYGULANDIĞININ
/// kaydı — proje sohbeti: CorporateAction.AppliedToPortfolios tek başına yeterli değil, çünkü iş
/// yarıda (bazı portföyler işlendi, bazıları işlenemedi) kesilirse bir sonraki çalıştırmada aynı
/// olay tekrar taranır; bu tablo, HANGİ (olay, portföy) çiftinin zaten işlendiğini kalıcı olarak
/// tutar, yeniden tarama sırasında sadece kalanlar uygulanır, tamamlananlar asla tekrar edilmez.
/// </summary>
public class AppliedCorporateAction
{
    public int Id { get; set; }

    public int CorporateActionId { get; set; }

    public Guid PortfolioId { get; set; }

    /// <summary>Bu uygulamanın etkisi (kaç TL nakit / kaç yeni pay) — denetim/geri alma için.</summary>
    public string Effect { get; set; } = string.Empty;

    public DateTime AppliedAt { get; set; }
}
