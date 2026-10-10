namespace EnterpriseInventory.Domain.Assets;

/// <summary>Kind of device. Values are stored in the database; never renumber existing members.</summary>
public enum AssetType
{
    Desktop = 1,
    Laptop = 2,
    Monitor = 3,
    Printer = 4,
    Phone = 5,
    Tablet = 6,
    Server = 7,
    NetworkDevice = 8,
    Peripheral = 9,
    Other = 99,
}
