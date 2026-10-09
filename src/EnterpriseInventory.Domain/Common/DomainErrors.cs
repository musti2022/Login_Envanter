namespace EnterpriseInventory.Domain.Common;

/// <summary>Stable error codes raised by the domain model.</summary>
public static class DomainErrors
{
    public const string General = "Domain.RuleViolated";
    public const string Required = "Validation.Required";
    public const string TooLong = "Validation.TooLong";
    public const string InvalidValue = "Validation.InvalidValue";

    public static class Audit
    {
        public const string UpdatedBeforeCreated = "Audit.UpdatedBeforeCreated";
    }

    public static class Asset
    {
        public const string Archived = "Asset.Archived";
        public const string AlreadyArchived = "Asset.AlreadyArchived";
        public const string AlreadyAssigned = "Asset.AlreadyAssigned";
        public const string NotAvailableForAssignment = "Asset.NotAvailableForAssignment";
        public const string NotAssigned = "Asset.NotAssigned";
        public const string HasActiveAssignment = "Asset.HasActiveAssignment";
        public const string StatusRequiresAssignmentFlow = "Asset.StatusRequiresAssignmentFlow";
        public const string ReturnBeforeAssignment = "Asset.ReturnBeforeAssignment";
        public const string AssignmentOverlapsHistory = "Asset.AssignmentOverlapsHistory";
        public const string InactiveReference = "Asset.InactiveReference";
        public const string LocationCityMismatch = "Asset.LocationCityMismatch";
    }

    public static class Employee
    {
        public const string Inactive = "Employee.Inactive";
    }
}
