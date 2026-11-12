namespace Bigarr.Domain;

/// <summary>
/// A timestamp the database context fills from the application clock when the entity is first tracked as new, unless
/// the code that built the entity already set it. Entities never read the system clock themselves.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SetWhenAddedAttribute : Attribute;
