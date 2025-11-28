using System.Reflection;
using Xunit;

namespace Ambient.SagaEngine.Tests;

/// <summary>
/// Architectural tests to enforce CQRS boundaries and Clean Architecture principles.
/// These tests prevent accidental violations of layer dependencies.
/// </summary>
public class ArchitectureTests
{
    ///// <summary>
    ///// Verifies that Presentation.UI layer does NOT reference Infrastructure.Persistence.
    ///// All infrastructure interactions must go through Contracts or Application layers.
    ///// </summary>
    //[Fact]
    //public void PresentationLayer_ShouldNotReference_InfrastructurePersistence()
    //{
    //    // Arrange
    //    var presentationAssembly = Assembly.Load("Ambient.Saga.Presentation.UI");
    //    var infrastructureNamespace = "Ambient.SagaEngine.Infrastructure.Persistence";

    //    // Act: Get all types from Presentation assembly
    //    var presentationTypes = presentationAssembly.GetTypes();

    //    // Check for Infrastructure references in:
    //    // 1. Using directives (we can't check directly, but we can check type references)
    //    // 2. Field/property types
    //    // 3. Method parameters/return types
    //    // 4. Base classes

    //    var violations = new List<string>();

    //    foreach (var type in presentationTypes)
    //    {
    //        // Check fields and properties
    //        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    //        foreach (var field in fields)
    //        {
    //            if (field.FieldType.Namespace != null && field.FieldType.Namespace.StartsWith(infrastructureNamespace))
    //            {
    //                violations.Add($"{type.FullName} has field '{field.Name}' of type {field.FieldType.FullName}");
    //            }
    //        }

    //        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    //        foreach (var property in properties)
    //        {
    //            if (property.PropertyType.Namespace != null && property.PropertyType.Namespace.StartsWith(infrastructureNamespace))
    //            {
    //                violations.Add($"{type.FullName} has property '{property.Name}' of type {property.PropertyType.FullName}");
    //            }
    //        }

    //        // Check methods
    //        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    //        foreach (var method in methods)
    //        {
    //            // Check return type
    //            if (method.ReturnType.Namespace != null && method.ReturnType.Namespace.StartsWith(infrastructureNamespace))
    //            {
    //                violations.Add($"{type.FullName}.{method.Name}() returns {method.ReturnType.FullName}");
    //            }

    //            // Check parameters
    //            foreach (var parameter in method.GetParameters())
    //            {
    //                if (parameter.ParameterType.Namespace != null && parameter.ParameterType.Namespace.StartsWith(infrastructureNamespace))
    //                {
    //                    violations.Add($"{type.FullName}.{method.Name}() has parameter '{parameter.Name}' of type {parameter.ParameterType.FullName}");
    //                }
    //            }
    //        }
    //    }

    //    // Assert
    //    Assert.Empty(violations);
    //}

    ///// <summary>
    ///// Verifies that Presentation.UI layer does NOT reference Domain.Services.
    ///// All domain service calls must go through CQRS commands/queries.
    ///// </summary>
    //[Fact]
    //public void PresentationLayer_ShouldNotReference_DomainServices()
    //{
    //    // Arrange
    //    var presentationAssembly = Assembly.Load("Ambient.Saga.Presentation.UI");
    //    var domainServicesNamespace = "Ambient.SagaEngine.Domain.Services";

    //    // Act
    //    var presentationTypes = presentationAssembly.GetTypes();
    //    var violations = new List<string>();

    //    foreach (var type in presentationTypes)
    //    {
    //        // Check fields and properties
    //        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    //        foreach (var field in fields)
    //        {
    //            if (field.FieldType.Namespace != null && field.FieldType.Namespace.StartsWith(domainServicesNamespace))
    //            {
    //                violations.Add($"{type.FullName} has field '{field.Name}' of type {field.FieldType.FullName}");
    //            }
    //        }

    //        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    //        foreach (var property in properties)
    //        {
    //            if (property.PropertyType.Namespace != null && property.PropertyType.Namespace.StartsWith(domainServicesNamespace))
    //            {
    //                violations.Add($"{type.FullName} has property '{property.Name}' of type {property.PropertyType.FullName}");
    //            }
    //        }

    //        // Check methods
    //        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
    //        foreach (var method in methods)
    //        {
    //            // Check return type
    //            if (method.ReturnType.Namespace != null && method.ReturnType.Namespace.StartsWith(domainServicesNamespace))
    //            {
    //                violations.Add($"{type.FullName}.{method.Name}() returns {method.ReturnType.FullName}");
    //            }

    //            // Check parameters
    //            foreach (var parameter in method.GetParameters())
    //            {
    //                if (parameter.ParameterType.Namespace != null && parameter.ParameterType.Namespace.StartsWith(domainServicesNamespace))
    //                {
    //                    violations.Add($"{type.FullName}.{method.Name}() has parameter '{parameter.Name}' of type {parameter.ParameterType.FullName}");
    //                }
    //            }
    //        }
    //    }

    //    // Assert
    //    Assert.Empty(violations);
    //}

    /// <summary>
    /// Verifies Application layer handlers do NOT directly access Infrastructure.
    /// Handlers should only inject repository interfaces from Contracts layer.
    /// </summary>
    [Fact]
    public void ApplicationHandlers_ShouldOnlyReference_RepositoryInterfaces()
    {
        // Arrange
        var applicationAssembly = Assembly.Load("Ambient.SagaEngine");
        var infrastructureNamespace = "Ambient.SagaEngine.Infrastructure.Persistence";

        // Act: Check all handler types
        var handlerTypes = applicationAssembly.GetTypes()
            .Where(t => t.Namespace != null &&
                       t.Namespace.StartsWith("Ambient.SagaEngine.Application.Handlers"));

        var violations = new List<string>();

        foreach (var handlerType in handlerTypes)
        {
            // Check constructor parameters (handlers should inject interfaces, not concrete types)
            var constructors = handlerType.GetConstructors();
            foreach (var constructor in constructors)
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    if (parameter.ParameterType.Namespace != null &&
                        parameter.ParameterType.Namespace.StartsWith(infrastructureNamespace))
                    {
                        violations.Add($"{handlerType.FullName} constructor injects concrete type {parameter.ParameterType.FullName}");
                    }
                }
            }
        }

        // Assert
        Assert.Empty(violations);
    }
}
