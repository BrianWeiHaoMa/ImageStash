using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ImageStash.Tests.Portable;

/// <summary>
/// F13, R1: the portable assembly reaches Windows only through its seams. The build fails on an in-box Windows-only
/// API (CA1416); these checks read the built assembly for what the compiler cannot see, including a Windows-only
/// call behind an operating-system guard, which CA1416 accepts.
/// </summary>
[TestClass]
public sealed class PortableAssemblyTests
{
    private static readonly Assembly Portable = typeof(App).Assembly;

    [TestMethod]
    public void DoesNotReferenceTheWindowsAssembly()
    {
        var names = Portable.GetReferencedAssemblies().Select(a => a.Name).ToList();

        CollectionAssert.DoesNotContain(names, "ImageStash.Windows");
    }

    [TestMethod]
    public void ReferencesNoWindowsOnlyAssembly()
    {
        var windowsOnly = Portable.GetReferencedAssemblies()
            .Select(Assembly.Load)
            .Where(IsWindowsOnly)
            .Select(a => a.GetName().Name)
            .ToList();

        Assert.IsEmpty(windowsOnly, $"Windows-only assemblies referenced: {string.Join(", ", windowsOnly)}");
    }

    [TestMethod]
    public void UsesNoWindowsOnlyTypeOrMember()
    {
        using var file = File.OpenRead(Portable.Location);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        var module = Portable.ManifestModule;

        var used = metadata.TypeReferences.Select(h => (Name: Describe(metadata, h), Members: new[] { module.ResolveType(MetadataTokens.GetToken(h)) }.AsEnumerable<MemberInfo>()))
            .Concat(metadata.MemberReferences.Select(h => (Name: Describe(metadata, h), Members: Resolve(metadata, module, h))))
            .ToList();
        var unresolved = used.Where(u => !u.Members.Any()).Select(u => u.Name).ToList();
        var windowsOnly = used.Where(u => u.Members.Any(IsWindowsOnly)).Select(u => u.Name).Distinct().ToList();

        Assert.IsEmpty(unresolved, $"References this check cannot read: {string.Join(", ", unresolved)}");
        Assert.IsEmpty(windowsOnly, $"Windows-only APIs used: {string.Join(", ", windowsOnly)}");
    }

    [TestMethod]
    public void JudgesAFrameworkAssemblyByWhatItPromisesEverySystem()
    {
        Assert.IsTrue(IsWindowsOnly(Assembly.Load("Microsoft.Win32.Registry")));
        Assert.IsFalse(IsWindowsOnly(typeof(System.Security.Cryptography.SHA256).Assembly));
        Assert.IsFalse(IsWindowsOnly(typeof(Console).Assembly));
    }

    [TestMethod]
    public void TargetsNoPlatform()
    {
        Assert.IsEmpty(Portable.GetCustomAttributes<TargetPlatformAttribute>());
        Assert.IsEmpty(Portable.GetCustomAttributes<SupportedOSPlatformAttribute>());
    }

    [TestMethod]
    public void DeclaresNoPlatformInvoke()
    {
        var invokes = Portable.GetTypes()
            .SelectMany(t => t.GetMethods(Everything).Cast<MethodBase>().Concat(t.GetConstructors(Everything)))
            .Where(m => m.Attributes.HasFlag(MethodAttributes.PinvokeImpl))
            .Select(m => $"{m.DeclaringType}.{m.Name}")
            .ToList();

        Assert.IsEmpty(invokes, $"Platform invokes declared: {string.Join(", ", invokes)}");
    }

    [TestMethod]
    public void DeclaresNoComInterop()
    {
        var com = Portable.GetTypes()
            .Where(t => t.IsImport || t.GetCustomAttributesData().Any(a => a.AttributeType.Name == "GeneratedComInterfaceAttribute"))
            .Select(t => t.FullName)
            .ToList();

        Assert.IsEmpty(com, $"COM interop types declared: {string.Join(", ", com)}");
    }

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static bool IsWindows(SupportedOSPlatformAttribute attribute) =>
        attribute.PlatformName.StartsWith("windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The member a reference names. A member of a generic type instantiated over the caller's own type parameters
    /// cannot be resolved without that context, so it is looked up by name on the generic type's definition, and every
    /// member of that name counts.
    /// </summary>
    private static IEnumerable<MemberInfo> Resolve(MetadataReader metadata, Module module, MemberReferenceHandle handle)
    {
        try
        {
            return [module.ResolveMember(MetadataTokens.GetToken(handle))!];
        }
        catch (ArgumentException)
        {
            var reference = metadata.GetMemberReference(handle);
            if (reference.Parent.Kind != HandleKind.TypeSpecification)
            {
                return [];
            }

            var signature = metadata.GetBlobReader(metadata.GetTypeSpecification((TypeSpecificationHandle)reference.Parent).Signature);
            if (signature.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
            {
                return [];
            }

            signature.ReadSignatureTypeCode();
            var definition = module.ResolveType(MetadataTokens.GetToken(signature.ReadTypeHandle()));
            return definition.GetMember(metadata.GetString(reference.Name), Everything & ~BindingFlags.DeclaredOnly)
                .Append(definition);
        }
    }

    private static string Describe(MetadataReader metadata, TypeReferenceHandle handle)
    {
        var type = metadata.GetTypeReference(handle);
        return $"{metadata.GetString(type.Namespace)}.{metadata.GetString(type.Name)}";
    }

    private static string Describe(MetadataReader metadata, MemberReferenceHandle handle)
    {
        var member = metadata.GetMemberReference(handle);
        var parent = member.Parent.Kind == HandleKind.TypeReference ? Describe(metadata, (TypeReferenceHandle)member.Parent) : member.Parent.Kind.ToString();
        return $"{parent}.{metadata.GetString(member.Name)}";
    }

    /// <summary>What CA1416 reads: the member, the property or event it belongs to, its declaring types, and its assembly.</summary>
    private static bool IsWindowsOnly(MemberInfo member)
    {
        var marked = new List<ICustomAttributeProvider> { member };
        if (member is MethodInfo method && method.DeclaringType is { } owner)
        {
            marked.AddRange(owner.GetProperties(Everything).Where(p => p.GetMethod == method || p.SetMethod == method));
            marked.AddRange(owner.GetEvents(Everything).Where(e => e.AddMethod == method || e.RemoveMethod == method));
        }

        var assemblies = new List<Assembly>();
        for (var type = member as Type ?? member.DeclaringType; type is not null; type = type.DeclaringType)
        {
            marked.Add(type);
            assemblies.Add(type.Assembly);
        }

        return assemblies.Any(IsWindowsOnly) || marked.Any(m => m.GetCustomAttributes(typeof(SupportedOSPlatformAttribute), false)
            .Cast<SupportedOSPlatformAttribute>()
            .Any(IsWindows));
    }

    /// <summary>
    /// Whether an assembly as a whole is Windows-only. The shared framework ships a build of some assemblies per
    /// operating system and marks its Windows build Windows-only even where the API is not (System.Console,
    /// System.Security.Cryptography), so a framework assembly is judged by its reference assembly, which is what CA1416
    /// reads; one without a reference assembly is judged as loaded.
    /// </summary>
    private static bool IsWindowsOnly(Assembly assembly)
    {
        var runtime = RuntimeEnvironment.GetRuntimeDirectory();
        var reference = Path.Combine(ReferencePack, assembly.GetName().Name + ".dll");
        if (!string.Equals(Path.GetDirectoryName(assembly.Location) + Path.DirectorySeparatorChar, runtime, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(reference))
        {
            return assembly.GetCustomAttributes<SupportedOSPlatformAttribute>().Any(IsWindows);
        }

        using var file = File.OpenRead(reference);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        return metadata.GetAssemblyDefinition().GetCustomAttributes()
            .Select(metadata.GetCustomAttribute)
            .Where(a => a.Constructor.Kind == HandleKind.MemberReference
                && metadata.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent is { Kind: HandleKind.TypeReference } parent
                && metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)parent).Name) == nameof(SupportedOSPlatformAttribute))
            .Select(a => metadata.GetBlobReader(a.Value))
            .Any(blob => blob.ReadUInt16() == 1 && blob.ReadSerializedString() is { } platform
                && platform.StartsWith("windows", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The reference assemblies of the running framework, from the SDK's targeting pack beside it.</summary>
    private static string ReferencePack
    {
        get
        {
            var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
            var pack = Path.Combine(runtime.Parent!.Parent!.Parent!.FullName, "packs", "Microsoft.NETCore.App.Ref", runtime.Name, "ref", $"net{Environment.Version.Major}.0");
            Assert.IsTrue(Directory.Exists(pack), $"No reference pack at {pack} to judge framework assemblies by");
            return pack;
        }
    }
}
