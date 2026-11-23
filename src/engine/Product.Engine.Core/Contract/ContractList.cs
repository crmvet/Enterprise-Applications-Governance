using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Product.Engine.Core.Reasons;

namespace Product.Engine.Core.Contract;

// Where a contract list comes from. Only the first three are engine meanings that labels map to.
public enum ContractGroup
{
    // Document 20, section 2.4: the engine contract.
    EngineMeaning,

    // Document 21, section 13.4: lists added by the configuration model.
    ConfigurationModel,

    // Document 25, section 15: lists the engine design asks document 20 to add.
    EngineDesign,

    // Document 25, section 5: fixed words in the operation records. No labels map to these.
    OperationVocabulary,
}

// The untyped view of a contract list. Lets callers find a list by name, for example "contract:gate-level".
public interface IContractList
{
    // The stable name, kebab-case, as used in a setting value type "contract:<name>".
    string Name { get; }

    // The name as the design documents write it.
    string Title { get; }

    ContractGroup Group { get; }

    // The design document and section that define the list.
    string Source { get; }

    // The enum type that holds the meanings in code.
    Type MeaningType { get; }

    // The wire values in their defined order.
    IReadOnlyList<string> WireValues { get; }

    // True when the wire value is one of the list's values. Ordinal and case-sensitive.
    bool IsDefined(string? wireValue);
}

// One fixed value list of the engine contract.
// Each meaning has one wire value: kebab-case, lowercase, as in document 21, section 8.3.
// Parsing is ordinal and strict. "In progress" and "IN-PROGRESS" are not "in-progress".
public sealed class ContractList<TMeaning> : IContractList
    where TMeaning : struct, Enum
{
    private readonly Dictionary<string, TMeaning> _byWire;
    private readonly Dictionary<TMeaning, string> _byMeaning;

    public ContractList(string name, string title, ContractGroup group, string source, params KeyValuePair<TMeaning, string>[] values)
    {
        if (!WireFormat.IsKebabCase(name))
        {
            throw new ArgumentException("A contract name must be kebab-case.", nameof(name));
        }

        if (string.IsNullOrEmpty(title))
        {
            throw new ArgumentException("A contract list needs a title.", nameof(title));
        }

        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("A contract list needs a source.", nameof(source));
        }

        if (values == null || values.Length == 0)
        {
            throw new ArgumentException("A contract list needs at least one value.", nameof(values));
        }

        _byWire = new Dictionary<string, TMeaning>(StringComparer.Ordinal);
        _byMeaning = new Dictionary<TMeaning, string>();
        var meanings = new TMeaning[values.Length];
        var wires = new string[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            TMeaning meaning = values[i].Key;
            string wire = values[i].Value;

            if (!Enum.IsDefined(typeof(TMeaning), meaning))
            {
                throw new ArgumentException("Contract " + name + " has a value outside its enum.", nameof(values));
            }

            if (!WireFormat.IsKebabCase(wire))
            {
                throw new ArgumentException("Contract " + name + " has a wire value that is not kebab-case: " + wire, nameof(values));
            }

            if (_byWire.ContainsKey(wire) || _byMeaning.ContainsKey(meaning))
            {
                throw new ArgumentException("Contract " + name + " repeats a value: " + wire, nameof(values));
            }

            _byWire.Add(wire, meaning);
            _byMeaning.Add(meaning, wire);
            meanings[i] = meaning;
            wires[i] = wire;
        }

        // Every enum member must have exactly one wire value, so no meaning is left without a name.
        int enumCount = Enum.GetValues(typeof(TMeaning)).Length;
        if (enumCount != values.Length)
        {
            throw new ArgumentException("Contract " + name + " does not cover every member of " + typeof(TMeaning).Name + ".", nameof(values));
        }

        Name = name;
        Title = title;
        Group = group;
        Source = source;
        Values = new ReadOnlyCollection<TMeaning>(meanings);
        WireValues = new ReadOnlyCollection<string>(wires);
    }

    public string Name { get; }

    public string Title { get; }

    public ContractGroup Group { get; }

    public string Source { get; }

    public Type MeaningType => typeof(TMeaning);

    // The meanings in their defined order.
    public IReadOnlyList<TMeaning> Values { get; }

    public IReadOnlyList<string> WireValues { get; }

    public bool IsDefined(string? wireValue)
    {
        return wireValue != null && _byWire.ContainsKey(wireValue);
    }

    public bool TryParse(string? wireValue, out TMeaning meaning)
    {
        if (wireValue != null && _byWire.TryGetValue(wireValue, out meaning))
        {
            return true;
        }

        meaning = default;
        return false;
    }

    // Parses a wire value. Throws ContractValueException for a value outside the list.
    public TMeaning Parse(string? wireValue)
    {
        if (TryParse(wireValue, out TMeaning meaning))
        {
            return meaning;
        }

        throw new ContractValueException(Name, wireValue);
    }

    public string ToWire(TMeaning meaning)
    {
        if (_byMeaning.TryGetValue(meaning, out string? wire))
        {
            return wire;
        }

        throw new ArgumentOutOfRangeException(nameof(meaning), "The value is not a member of contract " + Name + ".");
    }

    public override string ToString()
    {
        return Name;
    }
}

// Thrown when a value is not one of a contract list's fixed meanings.
// Adapters map it to the invalid input category (document 25, section 7).
public sealed class ContractValueException : ArgumentException
{
    public ContractValueException(string contractName, string? value)
        : base("The value is not a fixed meaning of contract " + contractName + ".")
    {
        ContractName = contractName;
        Value = value;
    }

    public string ContractName { get; }

    // The value that failed. May be null or empty.
    public string? Value { get; }

    public string ReasonCode => ReasonCodes.InputContractValueUnknown;
}
