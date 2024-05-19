namespace RetailDomain.ValueTypes;

public readonly record struct Address(
    string Country,
    string City,
    int GridX,
    int GridY );