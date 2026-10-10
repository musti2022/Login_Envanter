using EnterpriseInventory.Domain.Assets;
using EnterpriseInventory.Domain.Catalog;
using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Organization;
using static EnterpriseInventory.UnitTests.DomainModel.TestData;

namespace EnterpriseInventory.UnitTests.DomainModel;

public class AssetTests
{
    [Fact]
    public void Create_sets_details_trims_text_and_starts_available()
    {
        var model = NewModel("HP", "EliteBook 840");
        var city = NewCity();
        var department = NewDepartment();
        var location = Location.Create(city, "Merkez Bina 3. Kat");

        var asset = Asset.Create("  DMR-0042 ", AssetType.Laptop, model, city, department, location, " PC-42 ", " SN-42 ", "  Not ");

        Assert.Equal("DMR-0042", asset.AssetCode);
        Assert.Equal("PC-42", asset.ComputerName);
        Assert.Equal("SN-42", asset.SerialNumber);
        Assert.Equal("Not", asset.Description);
        Assert.Equal(AssetType.Laptop, asset.AssetType);
        Assert.Equal(AssetStatus.Available, asset.Status);
        Assert.Same(model, asset.Model);
        Assert.Same(model.Brand, asset.Brand);
        Assert.Same(city, asset.City);
        Assert.Same(department, asset.Department);
        Assert.Same(location, asset.Location);
        Assert.False(asset.IsDeleted);
        Assert.Empty(asset.Assignments);
        Assert.Null(asset.ActiveAssignment);
    }

    [Fact]
    public void Create_takes_brand_and_model_keys_from_saved_model()
    {
        var brand = Brand.Create("Lenovo").WithId(7);
        var model = AssetModel.Create(brand, "ThinkPad T14").WithId(21);

        var asset = Asset.Create("DMR-1", AssetType.Laptop, model, NewCity().WithId(1), NewDepartment().WithId(2));

        Assert.Equal(7, asset.BrandId);
        Assert.Equal(21, asset.ModelId);
        Assert.Equal(1, asset.CityId);
        Assert.Equal(2, asset.DepartmentId);
        Assert.Null(asset.LocationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_requires_asset_code(string? assetCode)
    {
        DomainAssert.Violates(DomainErrors.Required, () =>
            Asset.Create(assetCode!, AssetType.Laptop, NewModel(), NewCity(), NewDepartment()));
    }

    [Fact]
    public void Create_rejects_too_long_values()
    {
        var longCode = new string('A', Asset.AssetCodeMaxLength + 1);
        DomainAssert.Violates(DomainErrors.TooLong, () =>
            Asset.Create(longCode, AssetType.Laptop, NewModel(), NewCity(), NewDepartment()));

        var longSerial = new string('S', Asset.SerialNumberMaxLength + 1);
        DomainAssert.Violates(DomainErrors.TooLong, () =>
            Asset.Create("DMR-1", AssetType.Laptop, NewModel(), NewCity(), NewDepartment(), serialNumber: longSerial));
    }

    [Fact]
    public void Create_accepts_values_at_the_maximum_length()
    {
        var code = new string('A', Asset.AssetCodeMaxLength);

        var asset = Asset.Create(code, AssetType.Laptop, NewModel(), NewCity(), NewDepartment());

        Assert.Equal(code, asset.AssetCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_serial_number_is_stored_as_null_so_the_unique_index_ignores_it(string? serialNumber)
    {
        var asset = Asset.Create("DMR-1", AssetType.Monitor, NewModel(), NewCity(), NewDepartment(), serialNumber: serialNumber);

        Assert.Null(asset.SerialNumber);
    }

    [Theory]
    [InlineData("5cd1234xyz", "5CD1234XYZ")]
    [InlineData(" 5CD 1234\u00A0XYZ ", "5CD1234XYZ")]
    [InlineData("sn-ıi-42", "SN-II-42")]
    [InlineData("SN-İ-1", "SN-I-1")]
    public void Serial_number_is_stored_without_whitespace_and_in_invariant_upper_case(string typed, string stored)
    {
        var asset = Asset.Create("DMR-1", AssetType.Laptop, NewModel(), NewCity(), NewDepartment(), serialNumber: typed);

        Assert.Equal(stored, asset.SerialNumber);
    }

    [Fact]
    public void Serial_number_length_is_checked_after_whitespace_is_removed()
    {
        var spaced = string.Join(' ', Enumerable.Repeat("S", Asset.SerialNumberMaxLength));

        var asset = Asset.Create("DMR-1", AssetType.Laptop, NewModel(), NewCity(), NewDepartment(), serialNumber: spaced);

        Assert.Equal(new string('S', Asset.SerialNumberMaxLength), asset.SerialNumber);
    }

    [Fact]
    public void Create_rejects_undefined_asset_type()
    {
        DomainAssert.Violates(DomainErrors.InvalidValue, () =>
            Asset.Create("DMR-1", (AssetType)12345, NewModel(), NewCity(), NewDepartment()));
    }

    [Fact]
    public void Create_rejects_inactive_reference_data()
    {
        var inactiveModel = NewModel();
        inactiveModel.Deactivate();
        DomainAssert.Violates(DomainErrors.Asset.InactiveReference, () =>
            Asset.Create("DMR-1", AssetType.Laptop, inactiveModel, NewCity(), NewDepartment()));

        var modelOfInactiveBrand = NewModel();
        modelOfInactiveBrand.Brand.Deactivate();
        DomainAssert.Violates(DomainErrors.Asset.InactiveReference, () =>
            Asset.Create("DMR-1", AssetType.Laptop, modelOfInactiveBrand, NewCity(), NewDepartment()));

        var inactiveCity = NewCity();
        inactiveCity.Deactivate();
        DomainAssert.Violates(DomainErrors.Asset.InactiveReference, () =>
            Asset.Create("DMR-1", AssetType.Laptop, NewModel(), inactiveCity, NewDepartment()));

        var inactiveDepartment = NewDepartment();
        inactiveDepartment.Deactivate();
        DomainAssert.Violates(DomainErrors.Asset.InactiveReference, () =>
            Asset.Create("DMR-1", AssetType.Laptop, NewModel(), NewCity(), inactiveDepartment));

        var city = NewCity();
        var inactiveLocation = Location.Create(city, "Depo");
        inactiveLocation.Deactivate();
        DomainAssert.Violates(DomainErrors.Asset.InactiveReference, () =>
            Asset.Create("DMR-1", AssetType.Laptop, NewModel(), city, NewDepartment(), inactiveLocation));
    }

    [Fact]
    public void Create_rejects_location_from_another_city()
    {
        var ankaraLocation = Location.Create(NewCity("Ankara"), "Ankara Ofis");

        DomainAssert.Violates(DomainErrors.Asset.LocationCityMismatch, () =>
            Asset.Create("DMR-1", AssetType.Laptop, NewModel(), NewCity("İzmir"), NewDepartment(), ankaraLocation));
    }

    [Fact]
    public void Location_city_check_uses_keys_for_saved_entities()
    {
        var location = Location.Create(NewCity("Ankara").WithId(5), "Ankara Ofis").WithId(9);
        var sameCityLoadedSeparately = NewCity("Ankara").WithId(5);

        var asset = Asset.Create("DMR-1", AssetType.Laptop, NewModel(), sameCityLoadedSeparately, NewDepartment(), location);

        Assert.Equal(9, asset.LocationId);
    }

    [Fact]
    public void UpdateDetails_rejected_by_validation_leaves_the_asset_unchanged()
    {
        var asset = NewAsset();

        DomainAssert.Violates(DomainErrors.InvalidValue, () =>
            asset.UpdateDetails("NEW-CODE", (AssetType)999, "NEW-PC", "NEW-SN", "New"));

        Assert.Equal("DMR-0001", asset.AssetCode);
        Assert.Equal("PC-IST-01", asset.ComputerName);
        Assert.Equal("SN-123", asset.SerialNumber);
    }

    [Fact]
    public void ChangeModel_moves_brand_with_the_model()
    {
        var asset = NewAsset();
        var other = NewModel("Apple", "MacBook Pro");

        asset.ChangeModel(other);

        Assert.Same(other, asset.Model);
        Assert.Same(other.Brand, asset.Brand);
    }

    [Fact]
    public void ChangeModel_requires_the_models_brand_to_be_loaded()
    {
        var asset = NewAsset();
        var modelWithoutBrand = NewModel();
        typeof(AssetModel).GetProperty(nameof(AssetModel.Brand))!.SetValue(modelWithoutBrand, null);

        Assert.Throws<ArgumentException>(() => asset.ChangeModel(modelWithoutBrand));
    }

    [Fact]
    public void Keeping_a_since_deactivated_model_or_city_is_allowed()
    {
        var asset = NewAsset();
        asset.Model.Deactivate();
        asset.City.Deactivate();

        asset.ChangeModel(asset.Model);
        var changed = asset.ChangeLocation(asset.City, NewDepartment("İnsan Kaynakları"), null);

        Assert.True(changed);
    }

    [Fact]
    public void ChangeLocation_reports_whether_anything_changed()
    {
        var city = NewCity();
        var asset = NewAsset(city);

        Assert.False(asset.ChangeLocation(asset.City, asset.Department, asset.Location));

        var newLocation = Location.Create(city, "Depo");
        Assert.True(asset.ChangeLocation(city, asset.Department, newLocation));
        Assert.Same(newLocation, asset.Location);

        Assert.True(asset.ChangeLocation(city, asset.Department, null));
        Assert.Null(asset.Location);
        Assert.Null(asset.LocationId);
    }

    [Fact]
    public void ChangeLocation_treats_saved_entities_with_the_same_key_as_unchanged()
    {
        var asset = Asset.Create("DMR-1", AssetType.Laptop, NewModel(), NewCity().WithId(1), NewDepartment().WithId(2));

        var changed = asset.ChangeLocation(NewCity().WithId(1), NewDepartment().WithId(2), null);

        Assert.False(changed);
    }

    [Fact]
    public void Assign_creates_an_active_assignment_and_marks_the_asset_assigned()
    {
        var asset = NewAsset();
        var employee = NewEmployee();

        var assignment = asset.Assign(employee, " Dizüstü + çanta ", " Yeni personel ", TestData.Admin, Now);

        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.Same(assignment, asset.ActiveAssignment);
        Assert.Same(asset, assignment.Asset);
        Assert.Same(employee, assignment.Employee);
        Assert.Equal("Dizüstü + çanta", assignment.AssignmentDescription);
        Assert.Equal("Yeni personel", assignment.Notes);
        Assert.Equal(TestData.Admin, assignment.AssignedBy);
        Assert.Equal(Now, assignment.AssignedAt);
        Assert.True(assignment.IsActive);
        Assert.Null(assignment.ReturnedAt);
    }

    [Fact]
    public void Assign_rejects_a_second_active_assignment()
    {
        var asset = NewAssignedAsset(out _);

        DomainAssert.Violates(DomainErrors.Asset.AlreadyAssigned, () =>
            asset.Assign(NewEmployee(), null, null, TestData.Admin, Now));
        Assert.Single(asset.Assignments);
    }

    [Theory]
    [InlineData(AssetStatus.Faulty)]
    [InlineData(AssetStatus.Retired)]
    public void Assign_requires_an_available_asset(AssetStatus status)
    {
        var asset = NewAsset();
        asset.ChangeStatus(status);

        DomainAssert.Violates(DomainErrors.Asset.NotAvailableForAssignment, () =>
            asset.Assign(NewEmployee(), null, null, TestData.Admin, Now));
        Assert.Empty(asset.Assignments);
    }

    [Fact]
    public void Assign_rejects_an_employee_whose_directory_account_is_disabled()
    {
        var asset = NewAsset();

        DomainAssert.Violates(DomainErrors.Employee.Inactive, () =>
            asset.Assign(NewEmployee(isActive: false), null, null, TestData.Admin, Now));
        Assert.Equal(AssetStatus.Available, asset.Status);
    }

    [Fact]
    public void Assign_requires_the_assigning_user()
    {
        var asset = NewAsset();

        DomainAssert.Violates(DomainErrors.Required, () => asset.Assign(NewEmployee(), null, null, " ", Now));
        Assert.Empty(asset.Assignments);
        Assert.Equal(AssetStatus.Available, asset.Status);
    }

    [Fact]
    public void Return_closes_the_assignment_and_keeps_it_as_history()
    {
        var asset = NewAssignedAsset(out var assignment);
        var returnedAt = Now.AddDays(30);

        var returned = asset.Return("other.admin", returnedAt);

        Assert.Same(assignment, returned);
        Assert.Equal(AssetStatus.Available, asset.Status);
        Assert.Null(asset.ActiveAssignment);
        Assert.False(assignment.IsActive);
        Assert.Equal(returnedAt, assignment.ReturnedAt);
        Assert.Equal("other.admin", assignment.ReturnedBy);
        Assert.Single(asset.Assignments);
    }

    [Fact]
    public void An_asset_can_be_reassigned_after_return_with_full_history()
    {
        var asset = NewAssignedAsset(out var first);
        asset.Return(TestData.Admin, Now.AddDays(1));

        var second = asset.Assign(NewEmployee(), null, null, TestData.Admin, Now.AddDays(2));

        Assert.Equal(2, asset.Assignments.Count);
        Assert.Same(second, asset.ActiveAssignment);
        Assert.False(first.IsActive);
        Assert.Single(asset.Assignments, a => a.IsActive);
    }

    [Fact]
    public void Return_requires_an_active_assignment()
    {
        var asset = NewAsset();

        DomainAssert.Violates(DomainErrors.Asset.NotAssigned, () => asset.Return(TestData.Admin, Now));
    }

    [Fact]
    public void Return_before_the_assignment_time_is_rejected_and_changes_nothing()
    {
        var asset = NewAssignedAsset(out var assignment);

        DomainAssert.Violates(DomainErrors.Asset.ReturnBeforeAssignment, () => asset.Return(TestData.Admin, Now.AddMinutes(-1)));

        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.True(assignment.IsActive);
    }

    [Fact]
    public void ChangeStatus_cannot_set_assigned_directly()
    {
        var asset = NewAsset();

        DomainAssert.Violates(DomainErrors.Asset.StatusRequiresAssignmentFlow, () => asset.ChangeStatus(AssetStatus.Assigned));
    }

    [Fact]
    public void ChangeStatus_is_blocked_while_assigned()
    {
        var asset = NewAssignedAsset(out _);

        DomainAssert.Violates(DomainErrors.Asset.HasActiveAssignment, () => asset.ChangeStatus(AssetStatus.Faulty));
        Assert.Equal(AssetStatus.Assigned, asset.Status);
    }

    [Fact]
    public void ChangeStatus_moves_between_non_assigned_states()
    {
        var asset = NewAsset();

        asset.ChangeStatus(AssetStatus.Faulty);
        Assert.Equal(AssetStatus.Faulty, asset.Status);

        asset.ChangeStatus(AssetStatus.Available);
        Assert.Equal(AssetStatus.Available, asset.Status);
    }

    [Fact]
    public void ChangeStatus_rejects_undefined_values()
    {
        var asset = NewAsset();

        DomainAssert.Violates(DomainErrors.InvalidValue, () => asset.ChangeStatus((AssetStatus)0));
    }

    [Fact]
    public void Archive_soft_deletes_and_keeps_assignment_history()
    {
        var asset = NewAssignedAsset(out _);
        asset.Return(TestData.Admin, Now.AddDays(1));

        asset.Archive();

        Assert.True(asset.IsDeleted);
        Assert.Single(asset.Assignments);
    }

    [Fact]
    public void Archive_requires_the_asset_to_be_returned_first()
    {
        var asset = NewAssignedAsset(out _);

        DomainAssert.Violates(DomainErrors.Asset.HasActiveAssignment, asset.Archive);
        Assert.False(asset.IsDeleted);
    }

    [Fact]
    public void Archive_twice_is_rejected()
    {
        var asset = NewAsset();
        asset.Archive();

        DomainAssert.Violates(DomainErrors.Asset.AlreadyArchived, asset.Archive);
    }

    [Fact]
    public void Archived_assets_cannot_be_changed()
    {
        var asset = NewAsset();
        asset.Archive();

        DomainAssert.Violates(DomainErrors.Asset.Archived, () => asset.UpdateDetails("X", AssetType.Laptop, null, null, null));
        DomainAssert.Violates(DomainErrors.Asset.Archived, () => asset.ChangeModel(NewModel()));
        DomainAssert.Violates(DomainErrors.Asset.Archived, () => asset.ChangeLocation(NewCity(), NewDepartment(), null));
        DomainAssert.Violates(DomainErrors.Asset.Archived, () => asset.ChangeStatus(AssetStatus.Faulty));
        DomainAssert.Violates(DomainErrors.Asset.Archived, () => asset.Assign(NewEmployee(), null, null, TestData.Admin, Now));
        DomainAssert.Violates(DomainErrors.Asset.Archived, () => asset.Return(TestData.Admin, Now));
    }

    [Fact]
    public void Assignments_cannot_be_modified_through_the_public_collection()
    {
        var asset = NewAssignedAsset(out _);

        Assert.IsNotType<List<AssetAssignment>>(asset.Assignments);
        Assert.Throws<NotSupportedException>(() => ((ICollection<AssetAssignment>)asset.Assignments).Clear());
    }

    [Fact]
    public void ChangeLocation_moves_to_another_city_keeping_the_department()
    {
        var asset = NewAsset();
        var department = asset.Department;
        var ankara = NewCity("Ankara");

        var changed = asset.ChangeLocation(ankara, department, null);

        Assert.True(changed);
        Assert.Same(ankara, asset.City);
        Assert.Same(department, asset.Department);
    }

    [Fact]
    public void Keeping_a_since_deactivated_brand_department_or_location_is_allowed()
    {
        var city = NewCity();
        var asset = NewAsset(city, Location.Create(city, "Depo"));
        asset.Brand.Deactivate();
        asset.Department.Deactivate();
        asset.Location!.Deactivate();

        asset.ChangeModel(asset.Model);
        var changed = asset.ChangeLocation(NewCity("Ankara"), asset.Department, null);
        Assert.True(changed);

        var izmir = NewCity("İzmir");
        var izmirLocation = Location.Create(izmir, "Alsancak");
        asset.ChangeLocation(izmir, asset.Department, izmirLocation);
        izmirLocation.Deactivate();
        Assert.True(asset.ChangeLocation(izmir, NewDepartment("Satış"), asset.Location));
    }

    [Fact]
    public void Saved_model_and_location_with_the_same_key_count_as_unchanged_even_if_deactivated()
    {
        var city = NewCity().WithId(1);
        var asset = Asset.Create(
            "DMR-1",
            AssetType.Laptop,
            AssetModel.Create(Brand.Create("Dell").WithId(3), "Latitude").WithId(4),
            city,
            NewDepartment().WithId(2),
            Location.Create(city, "Depo").WithId(9));

        var modelCopy = AssetModel.Create(Brand.Create("Dell").WithId(3), "Latitude").WithId(4);
        modelCopy.Deactivate();
        asset.ChangeModel(modelCopy);

        var locationCopy = Location.Create(NewCity().WithId(1), "Depo").WithId(9);
        locationCopy.Deactivate();
        var changed = asset.ChangeLocation(NewCity().WithId(1), NewDepartment().WithId(2), locationCopy);

        Assert.False(changed);
        Assert.Equal(4, asset.ModelId);
        Assert.Equal(9, asset.LocationId);
    }

    [Fact]
    public void ChangeLocation_clears_a_location_whose_navigation_was_not_loaded()
    {
        var city = NewCity().WithId(1);
        var department = NewDepartment().WithId(2);
        var asset = Asset.Create("DMR-1", AssetType.Laptop, NewModel(), city, department)
            .WithProperty(nameof(Asset.LocationId), 9);

        var changed = asset.ChangeLocation(city, department, null);

        Assert.True(changed);
        Assert.Null(asset.LocationId);
    }

    [Fact]
    public void Assign_rejects_a_start_before_the_previous_return()
    {
        var asset = NewAssignedAsset(out _);
        asset.Return(TestData.Admin, Now.AddDays(10));

        DomainAssert.Violates(DomainErrors.Asset.AssignmentOverlapsHistory, () =>
            asset.Assign(NewEmployee(), null, null, TestData.Admin, Now.AddDays(5)));
        Assert.Equal(AssetStatus.Available, asset.Status);
        Assert.Single(asset.Assignments);
    }

    [Fact]
    public void Assign_may_start_at_the_exact_time_of_the_previous_return()
    {
        var asset = NewAssignedAsset(out _);
        asset.Return(TestData.Admin, Now.AddDays(10));

        var next = asset.Assign(NewEmployee(), null, null, TestData.Admin, Now.AddDays(10));

        Assert.Same(next, asset.ActiveAssignment);
    }

    [Fact]
    public void Return_at_the_exact_assignment_time_is_allowed()
    {
        var asset = NewAssignedAsset(out var assignment);

        asset.Return(TestData.Admin, Now);

        Assert.Equal(Now, assignment.ReturnedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Return_requires_the_returning_user_and_changes_nothing_without_it(string? returnedBy)
    {
        var asset = NewAssignedAsset(out var assignment);

        DomainAssert.Violates(DomainErrors.Required, () => asset.Return(returnedBy!, Now.AddDays(1)));

        Assert.True(assignment.IsActive);
        Assert.Null(assignment.ReturnedBy);
        Assert.Equal(AssetStatus.Assigned, asset.Status);
    }

    /// <summary>An asset loaded from the database without Include(a =&gt; a.Assignments).</summary>
    private static Asset AssignedAssetWithoutLoadedAssignments() =>
        NewAsset().WithProperty(nameof(Asset.Status), AssetStatus.Assigned);

    [Fact]
    public void Status_change_and_archive_are_blocked_for_an_assigned_asset_even_without_loaded_assignments()
    {
        var asset = AssignedAssetWithoutLoadedAssignments();

        DomainAssert.Violates(DomainErrors.Asset.HasActiveAssignment, () => asset.ChangeStatus(AssetStatus.Faulty));
        DomainAssert.Violates(DomainErrors.Asset.HasActiveAssignment, asset.Archive);
        Assert.Equal(AssetStatus.Assigned, asset.Status);
        Assert.False(asset.IsDeleted);
    }

    [Fact]
    public void Return_fails_fast_when_assignments_were_not_loaded()
    {
        var asset = AssignedAssetWithoutLoadedAssignments();

        Assert.Throws<InvalidOperationException>(() => asset.Return(TestData.Admin, Now));
        Assert.Equal(AssetStatus.Assigned, asset.Status);
    }

    [Fact]
    public void Assign_is_rejected_for_an_assigned_asset_even_without_loaded_assignments()
    {
        var asset = AssignedAssetWithoutLoadedAssignments();

        DomainAssert.Violates(DomainErrors.Asset.NotAvailableForAssignment, () =>
            asset.Assign(NewEmployee(), null, null, TestData.Admin, Now));
        Assert.Empty(asset.Assignments);
    }
}
