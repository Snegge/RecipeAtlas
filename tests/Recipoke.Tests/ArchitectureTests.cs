using System.Reflection;
using System.Reflection.Emit;
using Recipoke.Api.Features.Recipes.Api;

// Inspect actual CLR signatures and IL member references, including async state machines.
internal static class ArchitectureTests
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
    public static void Run(Action<bool, string> check)
    {
        var domain = typeof(Recipe).Assembly;
        check(domain.GetReferencedAssemblies().All(a => a.Name is "System.Runtime" or "System.Collections" or "System.Linq" or "System.Text.Json" or
            "System.Text.RegularExpressions" or "System.Runtime.Numerics" or "System.Memory"), "Domain references only explicit BCL assemblies");
        var api = typeof(RecipeEndpoints).Assembly;
        var count = 0;
        foreach (var type in domain.GetTypes().Concat(api.GetTypes()))
        foreach (var dependency in Dependencies(type))
        {
            var ns = type.Namespace ?? "";
            var target = dependency.Namespace ?? "";
            bool imports = ns.StartsWith("Recipoke.Api.Features.RecipeImports");
            bool application = ns.EndsWith(".Application") || ns.Contains(".Application.");
            bool endpoint = ns.EndsWith(".Api") && ns.StartsWith("Recipoke.Api.Features.");
            bool forbidden = type.Assembly == domain && (target.StartsWith("Recipoke.Api") || target.StartsWith("Microsoft.") ||
                dependency == typeof(HttpClient) || target.StartsWith("System.Diagnostics") || dependency == typeof(File) || dependency == typeof(Directory));
            forbidden |= imports && (target.StartsWith("Recipoke.Api.Infrastructure.Persistence") || target.StartsWith("Microsoft.EntityFrameworkCore") ||
                target.StartsWith("Recipoke.Api.Features.Recipes") || target.StartsWith("Recipoke.Domain.Recipes"));
            forbidden |= application && (target.Contains(".Infrastructure") && !target.StartsWith("Recipoke.Api.Infrastructure.Persistence") ||
                target.StartsWith("Microsoft.AspNetCore.Http") || target.StartsWith("Microsoft.AspNetCore.Mvc"));
            forbidden |= endpoint && (target.StartsWith("Microsoft.EntityFrameworkCore") || target.StartsWith("Recipoke.Api.Infrastructure.Persistence") ||
                target.StartsWith("Recipoke.Domain.Recipes"));
            if (forbidden) throw new Exception($"Architecture dependency violation: {type.FullName} -> {dependency.FullName}");
            count++;
        }
        check(count > 100, "Architecture checks inspected actual signature and IL dependencies");
        Console.WriteLine($"Architecture boundaries: {count} signature/IL dependencies inspected.");
        // Prove the scanner observes calls, rather than only class names/signatures.
        check(Dependencies(typeof(ForbiddenProbe)).Contains(typeof(File)), "Dependency scanner detects a forbidden method-body reference");
    }
    private sealed class ForbiddenProbe { public static bool Probe(string path) => File.Exists(path); }
    private static HashSet<Type> Dependencies(Type type)
    {
        var found = new HashSet<Type>();
        void Add(Type? t)
        {
            if (t is null || t.IsGenericParameter || !found.Add(t)) return;
            if (t.HasElementType) Add(t.GetElementType());
            if (t.IsGenericType) foreach (var arg in t.GetGenericArguments()) Add(arg);
        }
        void Member(MemberInfo? member)
        {
            if (member is Type t) Add(t);
            else if (member is not null)
            {
                Add(member.DeclaringType);
                if (member is MethodInfo m) { Add(m.ReturnType); foreach (var a in m.GetGenericArguments()) Add(a); }
                if (member is FieldInfo f) Add(f.FieldType);
                if (member is MethodBase method) foreach (var p in method.GetParameters()) Add(p.ParameterType);
            }
        }
        Add(type.BaseType); foreach (var i in type.GetInterfaces()) Add(i);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var field in type.GetFields(flags)) Add(field.FieldType);
        foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
        {
            Member(method);
            var body = method.GetMethodBody(); if (body is null) continue;
            foreach (var local in body.LocalVariables) Add(local.LocalType);
            var bytes = body.GetILAsByteArray()!;
            for (int pos = 0; pos < bytes.Length;)
            {
                short value = bytes[pos++];
                if (value == 0xfe) value = (short)(0xfe00 | bytes[pos++]);
                var op = Opcodes[value];
                if (op.OperandType is OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineType or OperandType.InlineTok)
                {
                    var token = BitConverter.ToInt32(bytes, pos);
                    Member(method.Module.ResolveMember(token, type.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null));
                }
                pos += op.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, pos),
                    _ => 4
                };
            }
        }
        return found;
    }
}
