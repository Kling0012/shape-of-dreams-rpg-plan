using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Read metadata without loading game code or decompiling method bodies.
using var module = ModuleDefinition.ReadModule(args[0]);
IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in Flatten(type.NestedTypes)) yield return nested;
    }
}
string[] Attributes(ICustomAttributeProvider value) => value.CustomAttributes
    .Select(a => a.AttributeType.FullName).Order().ToArray();
var types = Flatten(module.Types).Where(t => t.Name != "<Module>").Select(t => new
{
    name = t.FullName,
    simpleName = t.Name,
    baseType = t.BaseType?.FullName,
    isEnum = t.IsEnum,
    interfaces = t.Interfaces.Select(i => i.InterfaceType.FullName).ToArray(),
    attributes = Attributes(t),
    fields = t.Fields.Select(f => new
    {
        name = f.Name, type = f.FieldType.FullName, isPublic = f.IsPublic,
        constant = f.HasConstant ? Convert.ToString(f.Constant, System.Globalization.CultureInfo.InvariantCulture) : null,
        attributes = Attributes(f)
    }).ToArray(),
    properties = t.Properties.Select(p => new
    {
        name = p.Name, type = p.PropertyType.FullName,
        isPublic = p.GetMethod?.IsPublic == true || p.SetMethod?.IsPublic == true,
        attributes = Attributes(p)
    }).ToArray(),
    events = t.Events.Select(e => e.Name).ToArray(),
    methods = t.Methods.Select(m => new
    {
        name = m.Name, result = m.ReturnType.FullName, genericCount = m.GenericParameters.Count,
        parameters = m.Parameters.Select(p => new { name = p.Name, type = p.ParameterType.FullName }).ToArray(),
        attributes = Attributes(m),
        typeTokens = m.HasBody ? m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldtoken && i.Operand is TypeReference)
            .Select(i => ((TypeReference)i.Operand).FullName).Distinct().ToArray() : Array.Empty<string>(),
        strings = m.HasBody ? m.Body.Instructions.Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand).ToArray() : Array.Empty<string>(),
        calls = m.HasBody ? m.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => (MethodReference)i.Operand).Select(r => new
            {
                owner = r.DeclaringType.FullName, name = r.Name,
                arguments = r is GenericInstanceMethod g ? g.GenericArguments.Select(a => a.FullName).ToArray() : Array.Empty<string>()
            }).ToArray() : Array.Empty<object>()
    }).ToArray()
}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new
{
    assembly = module.Assembly.Name.Name,
    references = module.AssemblyReferences.Select(r => r.FullName).Order().ToArray(),
    resources = module.Resources.Select(r => r.Name).Order().ToArray(),
    types
}));
