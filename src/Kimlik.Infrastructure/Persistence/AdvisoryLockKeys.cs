namespace Kimlik.Infrastructure.Persistence;

/// <summary>
/// Keys for PostgreSQL advisory locks that serialize work across Kimlik instances.
/// The high bytes spell "KIMLIK" so the locks are recognizable in <c>pg_locks</c>.
/// </summary>
internal static class AdvisoryLockKeys
{
    public const long TokenKeys = 0x4B494D4C494B_0001;

    public const long DatabasePreparation = 0x4B494D4C494B_0002;
}
