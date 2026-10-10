using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Common;
using FluentValidation;

namespace EnterpriseInventory.Application.Lookups;

/// <summary>Body of <c>POST /api/brands</c>, <c>/api/cities</c> and <c>/api/departments</c>.</summary>
public record CreateLookupRequest
{
    public string? Name { get; init; }
}

/// <summary>Body of <c>POST /api/models</c>.</summary>
public sealed record CreateModelRequest : CreateLookupRequest
{
    public int? BrandId { get; init; }
}

/// <summary>Body of <c>POST /api/locations</c>.</summary>
public sealed record CreateLocationRequest : CreateLookupRequest
{
    public int? CityId { get; init; }
}

/// <summary>
/// Body of <c>PUT /api/brands/{id}</c> and the other lookups: the new name and state, and the <c>rowVersion</c> the
/// caller read. A model's brand and a location's city do not change: the assets that use them must stay consistent.
/// </summary>
public sealed record UpdateLookupRequest : CreateLookupRequest
{
    public bool? IsActive { get; init; }

    public string? RowVersion { get; init; }
}

/// <summary>The name rules every lookup shares.</summary>
internal abstract class LookupNameValidator<T> : AbstractValidator<T>
    where T : CreateLookupRequest
{
    protected LookupNameValidator()
    {
        RuleFor(r => r.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("Ad zorunludur.")
            .Must(name => name!.Trim().Length <= ReferenceDataEntity.NameMaxLength)
            .WithMessage($"Ad en fazla {ReferenceDataEntity.NameMaxLength} karakter olabilir.")
            .Must(name => !name!.Any(char.IsControl)).WithMessage("Ad geçersiz karakter içeriyor.");
    }
}

internal sealed class CreateLookupRequestValidator : LookupNameValidator<CreateLookupRequest>;

internal sealed class CreateModelRequestValidator : LookupNameValidator<CreateModelRequest>
{
    public CreateModelRequestValidator()
    {
        RuleFor(r => r.BrandId).NotNull().WithMessage("Marka seçilmelidir.").GreaterThan(0).WithMessage("Marka geçersiz.");
    }
}

internal sealed class CreateLocationRequestValidator : LookupNameValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(r => r.CityId).NotNull().WithMessage("Şehir seçilmelidir.").GreaterThan(0).WithMessage("Şehir geçersiz.");
    }
}

internal sealed class UpdateLookupRequestValidator : LookupNameValidator<UpdateLookupRequest>
{
    public UpdateLookupRequestValidator()
    {
        RuleFor(r => r.IsActive).NotNull().WithMessage("Durum (isActive) zorunludur.");
        RuleFor(r => r.RowVersion).MustBeRowVersion();
    }
}
