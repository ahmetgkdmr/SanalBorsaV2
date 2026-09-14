namespace SanalBorsa.Application.Common.Exceptions;

/// <summary>
/// Kullanıcıya gösterilebilir bir iş kuralı ihlali ("Yetersiz bakiye", "Borsa kapalı" gibi).
///
/// <para>
/// Önceden bu durumlar <see cref="InvalidOperationException"/> ile fırlatılıyor ve middleware
/// o tipi toptan 400'e eşleyip <c>ex.Message</c>'ı istemciye yazıyordu. Sorun şu ki
/// <see cref="InvalidOperationException"/> .NET'in her yerinden gelebilir — EF Core, LINQ'in
/// "Sequence contains no elements" hatası, HttpClient — ve o iç mesajlar da 400 olarak
/// dışarı sızıyordu. Kendi tipimiz sayesinde "kullanıcıya söylenebilir" ile "beklenmeyen
/// sistem hatası" ayrımı net: ilki 400 + mesaj, ikincisi 500 + genel metin.
/// </para>
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }

    public BusinessRuleException(string message, Exception inner) : base(message, inner) { }
}
