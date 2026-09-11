namespace Bigarr.Decision.CustomFormats;

public interface ICustomFormatSpecification
{
    string Name { get; }

    string Implementation { get; }

    bool Negate { get; }

    bool Required { get; }

    bool IsSatisfiedBy(CustomFormatInput input);
}

public abstract class CustomFormatSpecificationBase(CustomFormatSpecificationDefinition definition) : ICustomFormatSpecification
{
    protected CustomFormatSpecificationDefinition Definition { get; } = definition;

    public string Name => Definition.Name;

    public string Implementation => Definition.Implementation;

    public bool Negate => Definition.Negate;

    public bool Required => Definition.Required;

    public bool IsSatisfiedBy(CustomFormatInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return IsSatisfiedByWithoutNegate(input) != Negate;
    }

    protected abstract bool IsSatisfiedByWithoutNegate(CustomFormatInput input);
}
