using System.Reflection;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using ICMarkets.Application.Dtos;

namespace ICMarkets.UnitTests;

// The dependency rule, enforced by a test instead of by review: Application describes
// the use case in its own terms and must not know an external provider's contract,
// nor manage the DI container.
public sealed class ArchitectureTests
{
    private const BindingFlags AllMembers =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Assembly Application = typeof(ChainInfo).Assembly;

    private static IEnumerable<Type> ApplicationTypes() => Application.GetTypes();

    [Fact]
    public void Application_HasNoTypeNamedAfterAnExternalProvider()
    {
        ApplicationTypes()
            .Where(t => t.Name.Contains("BlockCypher", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.FullName)
            .Should().BeEmpty("provider-specific contracts and adapters belong in Infrastructure");
    }

    [Fact]
    public void Application_HasNoWireFormatAttributes()
    {
        // [JsonPropertyName] is the fingerprint of a type that mirrors an external payload.
        var offenders = ApplicationTypes()
            .SelectMany(t => t.GetMembers(AllMembers))
            .Where(m => m.GetCustomAttributes<JsonPropertyNameAttribute>(inherit: false).Any())
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}");

        offenders.Should().BeEmpty("the external wire contract must not leak into Application");
    }

    [Fact]
    public void Application_DoesNotReferenceInfrastructure()
    {
        Application.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().NotContain("ICMarkets.Infrastructure");
    }

    [Fact]
    public void Application_DoesNotUseTheDiContainerAsAServiceLocator()
    {
        // IServiceScopeFactory / IServiceScope / IServiceProvider in a signature or field
        // means Application is creating scopes or resolving services at runtime. That is
        // composition-root work: Application declares ports (e.g. IUnitOfWorkFactory)
        // and Infrastructure decides how they are satisfied.
        string[] forbidden = ["IServiceScopeFactory", "IServiceScope", "IServiceProvider", "AsyncServiceScope"];

        var offenders = new List<string>();
        foreach (var type in ApplicationTypes())
        {
            foreach (var member in type.GetMembers(AllMembers))
            {
                foreach (var used in MemberTypes(member).SelectMany(Flatten))
                {
                    if (forbidden.Contains(used.Name))
                        offenders.Add($"{type.FullName}.{member.Name} uses {used.Name}");
                }
            }
        }

        offenders.Distinct().Should().BeEmpty("Application must not locate services or manage DI scopes");
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        if (t.HasElementType)
            t = t.GetElementType()!;
        yield return t;
        if (!t.IsGenericType)
            yield break;
        foreach (var arg in t.GetGenericArguments())
            foreach (var inner in Flatten(arg))
                yield return inner;
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member)
    {
        switch (member)
        {
            case FieldInfo f:
                return [f.FieldType];
            case PropertyInfo p:
                return [p.PropertyType];
            case MethodBase mb:
                var types = mb.GetParameters().Select(x => x.ParameterType).ToList();
                if (mb is MethodInfo mi)
                    types.Add(mi.ReturnType);
                return types;
            default:
                return [];
        }
    }
}
