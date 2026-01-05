using System.Diagnostics;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;

namespace Ambient.Infrastructure.Utilities;

/// <summary>
/// Provides XML loading and deserialization capabilities with optional schema validation.
/// </summary>
public static class XmlLoader
{
    /// <summary>
    /// Asynchronously loads and deserializes an XML file into a strongly-typed object without schema validation.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the XML content into.</typeparam>
    /// <param name="xmlFilePath">The path to the XML file to load.</param>
    /// <param name="cancellationToken">Optional cancellation token to cancel the loading operation.</param>
    /// <param name="logger">Optional logger for detailed logging.</param>
    /// <returns>A deserialized object of type T.</returns>
    public static async Task<T> LoadFromXmlAsync<T>(string xmlFilePath, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xmlFilePath))
            throw new ArgumentException("XML file path cannot be null or empty.", nameof(xmlFilePath));

        var fullPath = Path.GetFullPath(xmlFilePath);
        var typeName = typeof(T).Name;

        T result = default;

        var settings = new XmlReaderSettings
        {
            Async = true
        };

        try
        {
            logger?.LogInformation("Loading XML file: {FilePath} as type {TypeName}", fullPath, typeName);
            Debug.WriteLine($"Loading: {fullPath}");

            using Stream stream = File.OpenRead(xmlFilePath);
            using var xmlReader = XmlReader.Create(stream, settings);
            var serializer = new XmlSerializer(typeof(T));

            result = (T)await Task.Run(() => serializer.Deserialize(xmlReader), cancellationToken);

            logger?.LogInformation("Successfully loaded: {FilePath}", fullPath);
        }
        catch (InvalidOperationException e)
        {
            logger?.LogError(e, "Deserialization failed for {TypeName} from {FilePath}: {Message}", typeName, fullPath, e.Message);
            Debug.WriteLine($"Deserialization failed: {e.Message}-{fullPath}");
            throw new Exception($"Deserialization failed: {e.Message}", e);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Unexpected error loading {TypeName} from {FilePath}: {Message}", typeName, fullPath, e.Message);
            Debug.WriteLine($"Unexpected error: {e.Message}-{fullPath}");
            throw;
        }

        return result;
    }

    /// <summary>
    /// Asynchronously loads and deserializes an XML file into a strongly-typed object with schema validation.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the XML content into.</typeparam>
    /// <param name="xmlFilePath">The path to the XML file to load.</param>
    /// <param name="xsdFilePath">The path to the XSD schema file for validation.</param>
    /// <param name="cancellationToken">Optional cancellation token to cancel the loading operation.</param>
    /// <param name="logger">Optional logger for detailed logging.</param>
    /// <returns>A deserialized object of type T.</returns>
    public static async Task<T> LoadFromXmlAsync<T>(string xmlFilePath, string xsdFilePath, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xmlFilePath))
            throw new ArgumentException("XML file path cannot be null or empty.", nameof(xmlFilePath));

        if (string.IsNullOrWhiteSpace(xsdFilePath))
            throw new ArgumentException("XSD file path cannot be null or empty.", nameof(xsdFilePath));

        var fullPath = Path.GetFullPath(xmlFilePath);
        var fullXsdPath = Path.GetFullPath(xsdFilePath);
        var typeName = typeof(T).Name;

        T result = default;

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Async = true
        };

        try
        {
            logger?.LogInformation("Loading XML file: {FilePath} as type {TypeName} with schema {XsdPath}", fullPath, typeName, fullXsdPath);

            var schemaSet = new XmlSchemaSet
            {
                XmlResolver = new DebugXmlResolver()
            };

            var basePath = Path.GetDirectoryName(xsdFilePath);
            var absolutePath = Path.Combine(basePath, Path.GetFileName(xsdFilePath));

            schemaSet.Add(null, absolutePath);
            schemaSet.ValidationEventHandler += (sender, args) =>
            {
                Debug.WriteLine($"Schema validation error: {args.Message}");
                logger?.LogWarning("Schema validation issue in {FilePath}: {Message}", fullPath, args.Message);
                if (args.Severity == XmlSeverityType.Error)
                    throw new XmlSchemaValidationException(args.Message);
            };
            schemaSet.Compile();

            settings.Schemas = schemaSet;

            using Stream stream = File.OpenRead(xmlFilePath);

            using var xmlReader = XmlReader.Create(stream, settings);
            var serializer = new XmlSerializer(typeof(T));

            Debug.WriteLine($"Loading: {fullPath}");
            result = (T)await Task.Run(() => serializer.Deserialize(xmlReader), cancellationToken);

            logger?.LogInformation("Successfully loaded: {FilePath}", fullPath);
        }
        catch (XmlSchemaValidationException e)
        {
            logger?.LogError(e, "Schema validation failed for {TypeName} from {FilePath}: {Message}", typeName, fullPath, e.Message);
            Debug.WriteLine($"Validation failed: {e.Message}-{fullPath}");
            throw new Exception($"XML validation failed: {e.Message}", e);
        }
        catch (InvalidOperationException e)
        {
            logger?.LogError(e, "Deserialization failed for {TypeName} from {FilePath}: {Message}", typeName, fullPath, e.Message);
            Debug.WriteLine($"Deserialization failed: {e.Message}-{fullPath}");
            throw new Exception($"Deserialization failed: {e.Message}", e);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Unexpected error loading {TypeName} from {FilePath}: {Message}", typeName, fullPath, e.Message);
            Debug.WriteLine($"Unexpected error: {e.Message}-{fullPath}");
            throw;
        }

        return result;
    }

    private static void DisplaySchemaDetails(XmlSchema schema, HashSet<string> processedSchemas = null)
    {
        processedSchemas ??= new HashSet<string>();

        if (schema == null || schema.TargetNamespace != null && processedSchemas.Contains(schema.TargetNamespace))
            return;

        processedSchemas.Add(schema.TargetNamespace ?? Guid.NewGuid().ToString());

        Debug.WriteLine($"Loaded schema: {schema.TargetNamespace ?? "(no target namespace)"}");
        Debug.WriteLine($"Number of elements in schema: {schema.Elements.Count}");

        foreach (XmlQualifiedName elementName in schema.Elements.Names)
        {
            Debug.WriteLine($"Declared element: {elementName.Name}");
        }

        foreach (var include in schema.Includes)
        {
            if (include is XmlSchemaInclude schemaInclude)
            {
                if (schemaInclude.Schema == null)
                {
                    Debug.WriteLine($"Included schema not resolved: {schemaInclude.SchemaLocation}");
                }
                else
                {
                    Debug.WriteLine($"Processing included schema: {schemaInclude.Schema.TargetNamespace}");
                    DisplaySchemaDetails(schemaInclude.Schema, processedSchemas);
                }
            }
            else if (include is XmlSchemaImport schemaImport)
            {
                if (schemaImport.Schema == null)
                {
                    Debug.WriteLine($"Imported schema not resolved: {schemaImport.SchemaLocation}");
                }
                else
                {
                    Debug.WriteLine($"Processing imported schema: {schemaImport.Schema.TargetNamespace}");
                    DisplaySchemaDetails(schemaImport.Schema, processedSchemas);
                }
            }
            else if (include is XmlSchemaRedefine schemaRedefine)
            {
                if (schemaRedefine.Schema == null)
                {
                    Debug.WriteLine($"Redefined schema not resolved: {schemaRedefine.SchemaLocation}");
                }
                else
                {
                    Debug.WriteLine($"Processing redefined schema: {schemaRedefine.Schema.TargetNamespace}");
                    DisplaySchemaDetails(schemaRedefine.Schema, processedSchemas);
                }
            }
        }
    }
}

public class DebugXmlResolver : XmlUrlResolver
{
    public override Uri ResolveUri(Uri baseUri, string relativeUri)
    {
        var resolvedUri = base.ResolveUri(baseUri, relativeUri);
        return resolvedUri;
    }

    public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn)
    {
        return base.GetEntity(absoluteUri, role, ofObjectToReturn);
    }
}