namespace Kimlik.Infrastructure.Persistence;

/// <summary>
/// Database settings from the <c>Kimlik:Database</c> configuration section.
/// The connection string itself is read from <c>ConnectionStrings:Kimlik</c>.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Kimlik:Database";

    public const string ConnectionStringName = "Kimlik";

    /// <summary>
    /// Applies pending migrations when the server starts. Disable it when migrations run
    /// as a separate deployment step with the <c>migrate</c> command.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;
}
