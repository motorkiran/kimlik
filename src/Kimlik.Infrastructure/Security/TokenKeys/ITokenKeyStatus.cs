namespace Kimlik.Infrastructure.Security.TokenKeys;

/// <summary>Tells whether this instance has loaded its token keys and can serve OpenID Connect requests.</summary>
public interface ITokenKeyStatus
{
    bool IsLoaded { get; }
}
