using Kimlik.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Kimlik.Application.Abstractions;

/// <summary>The unit of work for use cases. EF Core's <see cref="DbSet{TEntity}"/> already is the repository.</summary>
public interface IKimlikDbContext
{
    DbSet<User> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
