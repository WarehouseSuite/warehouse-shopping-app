namespace Shop.Types.Common.ValueTypes;

public readonly record struct Address(
    string Country,
    string City,
    int GridX,
    int GridY );