using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ImageStash.Windows.Tests.Support;

/// <summary>
/// The product's calls as built, read from an assembly's metadata and IL: what reaches a type, an Avalonia
/// <c>Clipboard</c> property or a platform invoke, and which methods call, or take as a delegate, a given method.
/// </summary>
public static class CompiledCalls
{
    /// <summary>
    /// What in <paramref name="assembly"/> reaches one of <paramref name="types"/>, an Avalonia <c>Clipboard</c>
    /// property, or a platform invoke of one of <paramref name="entryPoints"/>, read from its metadata as built.
    /// </summary>
    public static List<string> ClipboardReads(Assembly assembly, IReadOnlyCollection<string> types, IReadOnlyCollection<string> entryPoints)
    {
        using var file = File.OpenRead(assembly.Location);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        var found = new List<string>();
        foreach (var handle in metadata.TypeReferences)
        {
            var name = TypeName(metadata, handle);
            if (types.Contains(name))
            {
                found.Add(name);
            }
        }

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);
            var parent = member.Parent.Kind == HandleKind.TypeReference ? TypeName(metadata, (TypeReferenceHandle)member.Parent) : "";
            var name = metadata.GetString(member.Name);
            if (types.Contains(parent) || (name == "get_Clipboard" && parent.StartsWith("Avalonia.", StringComparison.Ordinal)))
            {
                found.Add($"{parent}.{name}");
            }
        }

        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            var import = method.GetImport();
            if (!import.Name.IsNil && entryPoints.Contains(metadata.GetString(import.Name)))
            {
                found.Add($"{TypeName(metadata, method.GetDeclaringType())}.{metadata.GetString(method.Name)} → {metadata.GetString(metadata.GetModuleReference(import.Module).Name)}!{metadata.GetString(import.Name)}");
            }
        }

        return found;
    }

    private static string TypeName(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var type = metadata.GetTypeReference(handle);
        return $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
    }

    /// <summary>A type defined in the assembly by its full name, a nested type after its outer one and a <c>+</c>.</summary>
    private static string TypeName(MetadataReader metadata, TypeDefinitionHandle handle)
    {
        var type = metadata.GetTypeDefinition(handle);
        var outer = type.GetDeclaringType();
        return outer.IsNil ? $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}" : $"{TypeName(metadata, outer)}+{metadata.GetString(type.Name)}";
    }

    /// <summary>
    /// Every method in <paramref name="assembly"/> whose body calls, or takes as a delegate, one of
    /// <paramref name="targets"/> (each a type's full name and a method name), read from its IL as built, by its type's
    /// full name and its own name.
    /// </summary>
    public static List<string> CallersOf(Assembly assembly, IReadOnlyCollection<string> targets)
    {
        using var file = File.OpenRead(assembly.Location);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        return Calls(pe, metadata).Where(call => targets.Contains(MethodName(metadata, call.Called))).Select(call => MethodName(metadata, call.Caller)).ToList();
    }

    /// <summary>
    /// Every method in <paramref name="assembly"/> that reaches a platform invoke of one of
    /// <paramref name="entryPoints"/>, through calls or delegates within the assembly, without itself being reached
    /// from one of <paramref name="reads"/>: a read made anywhere else, even inside the seam's write.
    /// </summary>
    public static List<string> ReadsOutsideTheRead(Assembly assembly, IReadOnlyCollection<string> entryPoints, IReadOnlyCollection<string> reads)
    {
        using var file = File.OpenRead(assembly.Location);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        var calls = Calls(pe, metadata).Where(call => call.Called.Kind == HandleKind.MethodDefinition).Select(call => (call.Caller, Called: (MethodDefinitionHandle)call.Called)).ToList();
        var reaching = metadata.MethodDefinitions.Where(handle =>
        {
            var import = metadata.GetMethodDefinition(handle).GetImport();
            return !import.Name.IsNil && entryPoints.Contains(metadata.GetString(import.Name));
        }).ToHashSet();
        var fromTheRead = metadata.MethodDefinitions.Where(handle => reads.Contains(MethodName(metadata, handle))).ToHashSet();
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (caller, called) in calls)
            {
                grew |= reaching.Contains(called) && reaching.Add(caller);
                grew |= fromTheRead.Contains(caller) && fromTheRead.Add(called);
            }
        }

        return reaching.Except(fromTheRead).Select(handle => MethodName(metadata, handle)).ToList();
    }

    /// <summary>Every call, or delegate taken, in every method body of the assembly: the method's handle and the called method's token.</summary>
    private static List<(MethodDefinitionHandle Caller, EntityHandle Called)> Calls(PEReader pe, MetadataReader metadata)
    {
        var calls = new List<(MethodDefinitionHandle, EntityHandle)>();
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
            {
                continue;
            }

            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
            while (il.RemainingBytes > 0)
            {
                var first = il.ReadByte();
                var opCode = first == 0xFE ? TwoByteOpCodes[il.ReadByte()] : OneByteOpCodes[first];
                if (opCode.OperandType == OperandType.InlineMethod)
                {
                    calls.Add((handle, MetadataTokens.EntityHandle(il.ReadInt32())));
                    continue;
                }

                // A switch's operand is its count, then that many branch targets.
                var operand = opCode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 * il.ReadInt32(),
                    _ => 4,
                };
                il.Offset += operand;
            }
        }

        return calls;
    }

    /// <summary>The method a call's token names, by its type's full name and its name; "" for what is not a method.</summary>
    private static string MethodName(MetadataReader metadata, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var definition = metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                return $"{TypeName(metadata, definition.GetDeclaringType())}.{metadata.GetString(definition.Name)}";
            case HandleKind.MemberReference:
                var reference = metadata.GetMemberReference((MemberReferenceHandle)handle);
                var parent = reference.Parent.Kind == HandleKind.TypeReference ? TypeName(metadata, (TypeReferenceHandle)reference.Parent) : "";
                return $"{parent}.{metadata.GetString(reference.Name)}";
            case HandleKind.MethodSpecification:
                return MethodName(metadata, metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
            default:
                return "";
        }
    }

    private static readonly OpCode[] OneByteOpCodes = OpCodeTable(twoByte: false);

    private static readonly OpCode[] TwoByteOpCodes = OpCodeTable(twoByte: true);

    /// <summary>Every IL opcode by its last byte, the one-byte ones or those after the 0xFE prefix.</summary>
    private static OpCode[] OpCodeTable(bool twoByte)
    {
        var table = new OpCode[256];
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var opCode = (OpCode)field.GetValue(null)!;
            if ((opCode.Size == 2) == twoByte)
            {
                table[(ushort)opCode.Value & 0xFF] = opCode;
            }
        }

        return table;
    }
}
