using EnterpriseInventory.Domain.Common;

namespace EnterpriseInventory.UnitTests.DomainModel;

internal static class DomainAssert
{
    public static void Violates(string expectedCode, Action action)
    {
        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(expectedCode, exception.Code);
    }
}
