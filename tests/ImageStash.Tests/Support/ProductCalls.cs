using System.Reflection;

namespace ImageStash.Tests.Support;

/// <summary>What the product's code calls, read from its IL, for the checks that pin which type alone may call something.</summary>
public static class ProductCalls
{
    private const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Every method and constructor of the product's portable assembly, those of generated types (lambdas, state machines) included.</summary>
    public static IEnumerable<MethodBase> Methods() =>
        typeof(App).Assembly.GetTypes().SelectMany(type => type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything)));

    /// <summary>The methods <paramref name="method"/>'s body calls, read from its IL's call, callvirt and newobj operands.</summary>
    public static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        if (method.GetMethodBody()?.GetILAsByteArray() is not { } il)
        {
            yield break;
        }

        for (var i = 0; i + 4 < il.Length; i++)
        {
            // call (0x28), callvirt (0x6F) or newobj (0x73), then a method token: a MethodDef, MemberRef or MethodSpec.
            if (il[i] is not (0x28 or 0x6F or 0x73) || il[i + 4] is not (0x06 or 0x0A or 0x2B))
            {
                continue;
            }

            MethodBase? called;
            try
            {
                called = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
            }
            catch (Exception e) when (e is ArgumentException or BadImageFormatException)
            {
                // Bytes that only look like a call: an operand of another instruction.
                called = null;
            }

            if (called is not null)
            {
                yield return called;
            }
        }
    }

    /// <summary>The type a lambda's or a state machine's generated type is nested in.</summary>
    public static Type Outermost(Type type) => type.DeclaringType is { } outer ? Outermost(outer) : type;

    /// <summary>
    /// Every type <paramref name="type"/> names, with the generic arguments and element types they carry: its base type
    /// and interfaces, its fields' and properties' types, its methods' parameters and results, and the types whose
    /// members its methods call; its nested types (generated ones included) are read as part of it.
    /// </summary>
    public static IEnumerable<Type> ReferencedTypes(Type type)
    {
        var named = new List<Type?> { type.BaseType };
        named.AddRange(type.GetInterfaces());
        named.AddRange(type.GetFields(Everything).Select(field => field.FieldType));
        named.AddRange(type.GetProperties(Everything).Select(property => property.PropertyType));
        foreach (var method in type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything)))
        {
            named.AddRange(method.GetParameters().Select(parameter => parameter.ParameterType));
            named.Add((method as MethodInfo)?.ReturnType);
            named.AddRange(CalledMethods(method).Select(called => called.DeclaringType));
        }

        return named.OfType<Type>().SelectMany(Carried)
            .Concat(type.GetNestedTypes(Everything).SelectMany(ReferencedTypes))
            .Distinct();
    }

    private static IEnumerable<Type> Carried(Type type) =>
        type.IsGenericParameter ? []
        : type.HasElementType ? Carried(type.GetElementType()!)
        : type.IsGenericType ? type.GetGenericArguments().SelectMany(Carried).Prepend(type.GetGenericTypeDefinition())
        : [type];
}
