using Kimlik.Application.Abstractions;
using Kimlik.Application.Common;
using Kimlik.Contracts.Management;
using Kimlik.Domain.Auditing;
using Kimlik.Domain.Common;
using Kimlik.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Organizations;

public sealed class CreateOrganizationHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<OrganizationResponse>> HandleAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var publicMetadata = Metadata.Serialize(request.PublicMetadata);
        var privateMetadata = Metadata.Serialize(request.PrivateMetadata);
        if (publicMetadata.IsFailure || privateMetadata.IsFailure)
        {
            return Metadata.TooLarge;
        }

        if (ProfileFields.Check(request.PictureUrl) is { } invalid)
        {
            return invalid;
        }

        var now = timeProvider.GetUtcNow();
        var created = Organization.Create(request.Name, request.Slug, request.RequireMfa, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var organization = created.Value;
        organization.SetPictureUrl(request.PictureUrl, now);
        organization.SetMetadata(publicMetadata.Value, privateMetadata.Value, now);
        if (await context.Organizations.AnyAsync(existing => existing.Slug == organization.Slug, cancellationToken))
        {
            return OrganizationErrors.SlugTaken;
        }

        context.Organizations.Add(organization);
        auditLog.Record(AuditActions.OrganizationCreated, AuditSubject.Organization(organization.Id), organizationId: organization.Id);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return OrganizationErrors.SlugTaken;
        }

        return organization.ToResponse();
    }
}

public sealed class UpdateOrganizationHandler(IKimlikDbContext context, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<OrganizationResponse>> HandleAsync(Guid id, UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (await context.Organizations.SingleOrDefaultAsync(organization => organization.Id == id, cancellationToken) is not { } organization)
        {
            return OrganizationErrors.NotFound;
        }

        if (!request.HasName && !request.HasSlug && !request.HasPictureUrl && request.RequireMfa is null && !request.HasPublicMetadata && !request.HasPrivateMetadata)
        {
            return organization.ToResponse();
        }

        var slug = request.HasSlug ? request.Slug ?? string.Empty : organization.Slug;
        if (slug != organization.Slug && await context.Organizations.AnyAsync(existing => existing.Slug == slug, cancellationToken))
        {
            return OrganizationErrors.SlugTaken;
        }

        var publicMetadata = request.HasPublicMetadata ? Metadata.Serialize(request.PublicMetadata) : organization.PublicMetadata;
        var privateMetadata = request.HasPrivateMetadata ? Metadata.Serialize(request.PrivateMetadata) : organization.PrivateMetadata;
        if (publicMetadata.IsFailure || privateMetadata.IsFailure)
        {
            return Metadata.TooLarge;
        }

        var pictureUrl = request.HasPictureUrl ? request.PictureUrl : organization.PictureUrl;
        if (ProfileFields.Check(pictureUrl) is { } invalid)
        {
            return invalid;
        }

        var now = timeProvider.GetUtcNow();
        var updated = organization.Update(
            request.HasName ? request.Name ?? string.Empty : organization.Name,
            slug,
            request.RequireMfa ?? organization.RequireMfa,
            now);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        organization.SetPictureUrl(pictureUrl, now);
        organization.SetMetadata(publicMetadata.Value, privateMetadata.Value, now);

        auditLog.Record(AuditActions.OrganizationUpdated, AuditSubject.Organization(id), organizationId: id);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return OrganizationErrors.SlugTaken;
        }

        return organization.ToResponse();
    }
}

/// <summary>Deletes an organization with its memberships. Tokens issued for it stop working at their next refresh.</summary>
public sealed class DeleteOrganizationHandler(IKimlikDbContext context, IAuditLog auditLog)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        if (await context.Organizations.SingleOrDefaultAsync(organization => organization.Id == id, cancellationToken) is not { } organization)
        {
            return OrganizationErrors.NotFound;
        }

        context.Organizations.Remove(organization);
        auditLog.Record(AuditActions.OrganizationDeleted, AuditSubject.Organization(id), new Dictionary<string, object?> { ["slug"] = organization.Slug }, organizationId: id);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
