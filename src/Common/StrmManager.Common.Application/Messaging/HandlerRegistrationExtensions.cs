using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace StrmManager.Common.Application.Messaging;

/// <summary>
/// Registers command/query handlers by scanning an assembly for types implementing
/// ICommandHandler/IQueryHandler, without requiring the (internal) handler classes
/// to be visible outside their own assembly - endpoints depend only on the public
/// handler interfaces, which the DI container resolves via reflection.
/// </summary>
public static class HandlerRegistrationExtensions
{
    private static readonly Type[] HandlerInterfaceDefinitions =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
    ];

    public static IServiceCollection AddHandlersFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        IEnumerable<Type> concreteTypes = assembly.GetTypes().Where(type => type is { IsAbstract: false, IsInterface: false });

        foreach (Type type in concreteTypes)
        {
            foreach (Type implementedInterface in type.GetInterfaces())
            {
                if (!implementedInterface.IsGenericType)
                {
                    continue;
                }

                if (HandlerInterfaceDefinitions.Contains(implementedInterface.GetGenericTypeDefinition()))
                {
                    services.AddScoped(implementedInterface, type);
                }
            }
        }

        return services;
    }
}
