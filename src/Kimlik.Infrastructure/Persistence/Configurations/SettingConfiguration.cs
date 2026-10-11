using Kimlik.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kimlik.Infrastructure.Persistence.Configurations;

internal sealed class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.HasKey(setting => setting.Key);
        builder.Property(setting => setting.Key).HasMaxLength(Setting.KeyMaxLength);
    }
}
