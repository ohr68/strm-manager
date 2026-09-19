using System.Reflection;
using StrmManager.Modules.Catalog.Presentation;

namespace StrmManager.ArchitectureTests;

/// <summary>
/// The stable playback endpoint must write its redirect by hand. Framework result helpers (Results.Redirect,
/// TypedResults.Redirect, RedirectHttpResult, MVC RedirectResult) LOG THEIR DESTINATION ("Executing RedirectResult,
/// redirecting to {Destination}") - for this endpoint that destination is a signed provider URL. The behavioral proof (a
/// canary URL scanned for in every log at Trace level) lives in the integration tests; this is the structural backstop that
/// fails the moment the endpoint calls one of those helpers.
///
/// It reads the compiled IL of the endpoint type AND every nested/compiler-generated type of it, not just its named members:
/// NetArchTest does not look inside lambdas or method groups (it skips the closure classes the compiler generates for them),
/// so a rewrite of the handler into a lambda would slip straight past it. The scan is deliberately over-inclusive - it
/// looks at every call-like instruction's operand - because a spurious match here is a reviewable false alarm, whereas a miss
/// would defeat the point.
/// </summary>
public class StablePlaybackEndpointTests
{
    private const string EndpointFullName = "StrmManager.Modules.Catalog.Presentation.Movies.StreamMovie";
    private const string ControlFullName = "StrmManager.Modules.Catalog.Presentation.Movies.ProcessMovie";

    private static readonly string[] ForbiddenNamespaces =
    [
        "Microsoft.AspNetCore.Http.HttpResults",       // RedirectHttpResult and the other typed results
        "Microsoft.AspNetCore.Mvc",                    // RedirectResult, ProblemDetails-producing helpers
        "StrmManager.Common.Presentation.ApiResults",  // the project's own body-producing (problem details) results
    ];

    private static readonly string[] ForbiddenTypes =
    [
        "Microsoft.AspNetCore.Http.Results",           // Results.Redirect / Results.Ok / ...
        "Microsoft.AspNetCore.Http.TypedResults",      // TypedResults.Redirect
        "Microsoft.AspNetCore.Http.ResultsExtensions",
    ];

    private static readonly Assembly Presentation = typeof(AssemblyReference).Assembly;

    private static IEnumerable<Type> TypeAndItsNestedTypes(string fullName) =>
        Presentation.GetTypes().Where(type => type.FullName == fullName || type.FullName!.StartsWith(fullName + "+", StringComparison.Ordinal));

    private static bool IsForbidden(Type declaring) =>
        ForbiddenTypes.Contains(declaring.FullName)
        || ForbiddenNamespaces.Any(ns => declaring.FullName!.StartsWith(ns + ".", StringComparison.Ordinal));

    /// <summary>Every type whose members the given type (and its nested types) call, create or take a delegate to, read from the IL.</summary>
    private static HashSet<Type> TypesCalledBy(string fullName)
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var called = new HashSet<Type>();

        foreach (Type type in TypeAndItsNestedTypes(fullName))
        {
            foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
            {
                byte[]? il = method.GetMethodBody()?.GetILAsByteArray();

                if (il is null)
                {
                    continue;
                }

                for (int i = 0; i < il.Length - 4; i++)
                {
                    // call (0x28), callvirt (0x6F), newobj (0x73) carry a 4-byte token; ldftn/ldvirtftn are 0xFE 0x06/0x07 then a token.
                    int tokenAt = il[i] is 0x28 or 0x6F or 0x73 ? i + 1
                        : il[i] == 0xFE && i + 5 < il.Length && il[i + 1] is 0x06 or 0x07 ? i + 2
                        : -1;

                    if (tokenAt < 0)
                    {
                        continue;
                    }

                    int token = BitConverter.ToInt32(il, tokenAt);

                    // Method tables only: MethodDef (0x06), MemberRef (0x0A), MethodSpec (0x2B).
                    if ((token >> 24) is not (0x06 or 0x0A or 0x2B))
                    {
                        continue;
                    }

                    try
                    {
                        MethodBase? target = type.Module.ResolveMethod(token, type.IsGenericType ? type.GetGenericArguments() : null, null);

                        if (target?.DeclaringType is { } declaring)
                        {
                            called.Add(declaring);
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or NotSupportedException)
                    {
                        // Not a real token (an operand byte pattern that merely looks like one): ignore it.
                    }
                }
            }
        }

        return called;
    }

    [Fact]
    public void TheScanWorks_AnotherEndpointsFrameworkResultCallsAreDetected_EvenInsideALambda()
    {
        // Control: without it, the assertion below could pass simply because the scan never finds anything.
        Type[] calls = TypesCalledBy(ControlFullName).Where(IsForbidden).ToArray();

        Assert.NotEmpty(TypeAndItsNestedTypes(ControlFullName));
        Assert.Contains(calls, type => type.FullName == "Microsoft.AspNetCore.Http.Results");
    }

    [Fact]
    public void TheStablePlaybackEndpoint_IsLookedAt_AndUsesNoRedirectOrResultHelper()
    {
        Type[] endpointTypes = TypeAndItsNestedTypes(EndpointFullName).ToArray();
        Assert.Contains(endpointTypes, type => type.FullName == EndpointFullName); // the type is really being inspected

        // Positive control on this very type: the scan sees what the endpoint does call (it sets response headers).
        HashSet<Type> calledByEndpoint = TypesCalledBy(EndpointFullName);
        Assert.Contains(calledByEndpoint, type => type.FullName!.StartsWith("Microsoft.AspNetCore.Http.", StringComparison.Ordinal));

        string[] offenders = calledByEndpoint.Where(IsForbidden).Select(type => type.FullName!).Order().ToArray();

        Assert.True(
            offenders.Length == 0,
            "The stable playback endpoint must write its 307 by hand (Location assigned from PlaybackLocation.Reveal() only). " +
            $"It calls a framework redirect/result helper, which logs its destination: {string.Join(", ", offenders)}");
    }
}
