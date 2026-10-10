using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Organization;
using EnterpriseInventory.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EnterpriseInventory.Infrastructure.Persistence.Seed;

/// <summary>
/// Sample lookups (brands and models, cities and locations, departments) for a development database, so the
/// screens have something to choose from. Run on purpose with <c>dotnet run -- seed-development-data</c>; it never
/// runs on its own. Idempotent: a value that already exists (by name, under the database's Turkish collation) is
/// left alone, so running it again adds only what is missing.
/// </summary>
/// <remarks>
/// Refused outside the Development environment. It adds no employees, no users and no passwords: people come
/// from Active Directory, and the development sign-in uses the fake directory, which production refuses.
/// </remarks>
public static class DevelopmentSeed
{
    /// <summary>The command-line argument that runs the seed instead of the web server.</summary>
    public const string Command = "seed-development-data";

    /// <summary>Recorded as <c>CreatedBy</c> on the rows the seed adds.</summary>
    public const string UserName = "development-seed";

    internal static readonly IReadOnlyDictionary<string, string[]> ModelsByBrand = new Dictionary<string, string[]>
    {
        ["Dell"] = ["Latitude 5440", "OptiPlex 7010", "P2423D"],
        ["HP"] = ["EliteBook 840 G10", "ProDesk 400 G9", "LaserJet Pro M404"],
        ["Lenovo"] = ["ThinkPad T14", "ThinkCentre M70q"],
    };

    internal static readonly IReadOnlyDictionary<string, string[]> LocationsByCity = new Dictionary<string, string[]>
    {
        ["İstanbul"] = ["Genel Müdürlük", "Kadıköy Şube"],
        ["Ankara"] = ["Bölge Müdürlüğü"],
        ["İzmir"] = ["Alsancak Şube"],
    };

    internal static readonly string[] Departments = ["Bilgi İşlem", "Muhasebe", "İnsan Kaynakları", "Satın Alma"];

    /// <summary>Adds what is missing and returns how many rows were added.</summary>
    /// <exception cref="InvalidOperationException">The environment is not Development.</exception>
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"Development data can only be seeded in the Development environment, not in '{environment.EnvironmentName}'.");
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // There is no signed-in user; the rows are stamped with the seed's name instead.
        using (scope.ServiceProvider.GetRequiredService<SignInIdentity>().ActAs(UserName))
        {
            foreach (var (brandName, modelNames) in ModelsByBrand)
            {
                var brand = await db.Brands.SingleOrDefaultAsync(b => b.Name == brandName, cancellationToken).ConfigureAwait(false)
                    ?? db.Brands.Add(Brand.Create(brandName)).Entity;
                foreach (var modelName in modelNames)
                {
                    if (brand.Id == 0 || !await db.AssetModels.AnyAsync(m => m.BrandId == brand.Id && m.Name == modelName, cancellationToken).ConfigureAwait(false))
                    {
                        db.AssetModels.Add(AssetModel.Create(brand, modelName));
                    }
                }
            }

            foreach (var (cityName, locationNames) in LocationsByCity)
            {
                var city = await db.Cities.SingleOrDefaultAsync(c => c.Name == cityName, cancellationToken).ConfigureAwait(false)
                    ?? db.Cities.Add(City.Create(cityName)).Entity;
                foreach (var locationName in locationNames)
                {
                    if (city.Id == 0 || !await db.Locations.AnyAsync(l => l.CityId == city.Id && l.Name == locationName, cancellationToken).ConfigureAwait(false))
                    {
                        db.Locations.Add(Location.Create(city, locationName));
                    }
                }
            }

            foreach (var departmentName in Departments)
            {
                if (!await db.Departments.AnyAsync(d => d.Name == departmentName, cancellationToken).ConfigureAwait(false))
                {
                    db.Departments.Add(Department.Create(departmentName));
                }
            }

            // One save: everything missing is added together, or nothing is.
            return await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
