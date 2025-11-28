# Story Generator XSD Schemas

This directory contains XSD schemas for the Story Generator project. All C# classes are **auto-generated** from these schemas.

---

## 📁 Schemas

### 1. GenerationConfiguration.xsd
Defines story generation configurations (separate from world runtime configs).

**Purpose**: Configure procedural story generation from real-world GPS locations

**Classes Generated**:
- `GenerationConfiguration` - Root configuration class
- `SourceLocation` - GPS location for story anchors
- `SourceLocationType` - Enum (Structure, Landmark, QuestSignpost)
- `GenerationStyle` - Enum (Trail, RadialExploration)

**XML Example**:
```xml
<GenerationConfiguration xmlns="Ambient.StoryGenerator"
    WorldRef="MyWorld"
    DisplayName="Epic Quest"
    GenerationStyle="Trail"
    Spacing="500"
    Seed="42"
    Theme="Medieval fantasy">
    <SourceLocation DisplayName="Castle" Type="Structure" Lat="45.0" Lon="0.0" />
</GenerationConfiguration>
```

**Data Files**: `../GenerationConfigs/*.xml`

---

### 2. Theme.xsd
Defines theme manifests for content libraries.

**Purpose**: Package character archetypes, equipment, spells, etc. into reusable themes

**Classes Generated**:
- `ThemeDefinition` - Root theme manifest class
- `ThemeTag` - Theme categorization

**XML Example**:
```xml
<Theme xmlns="Ambient.StoryGenerator"
    RefName="FeudalJapan"
    DisplayName="Feudal Japan">
    <Description>Samurai, ronin, traditional Japanese culture</Description>
    <Author>Your Name</Author>
    <Version>1.0</Version>
    <Tags>
        <Tag>Historical</Tag>
        <Tag>Japan</Tag>
    </Tags>
</Theme>
```

**Data Files**: `../Themes/*/Theme.xml`

**Note**: Theme content files (CharacterArchetypes.xml, Equipment.xml, etc.) use `Ambient.Domain` schemas, not StoryGenerator schemas.

---

## 🔧 Generating C# Classes

**Single command to generate all classes:**

```powershell
cd Ambient.Game.StoryGenerator/DefinitionXsd
.\BuildDefinitions.ps1
```

**What it does:**
1. Deletes `../DefinitionGenerated/` folder
2. Generates classes from `GenerationConfiguration.xsd`
3. Generates classes from `Theme.xsd`
4. Splits combined files into individual class files
5. Renames `Theme.cs` → `ThemeDefinition.cs` (avoids conflicts)

**Output** (`../DefinitionGenerated/`):
```
DefinitionGenerated/
├── GenerationConfiguration.cs
├── SourceLocation.cs
├── SourceLocationType.cs
├── GenerationStyle.cs
└── ThemeDefinition.cs
```

---

## 📝 Modifying Schemas

**To add new elements or attributes:**

1. Edit the `.xsd` file (e.g., `GenerationConfiguration.xsd`)
2. Run `.\BuildDefinitions.ps1`
3. Generated classes automatically updated
4. Update any code using the new schema

**Example - Adding a new attribute:**

```xml
<!-- Before -->
<xs:attribute name="Spacing" type="xs:double" use="optional" default="500" />

<!-- After -->
<xs:attribute name="Spacing" type="xs:double" use="optional" default="500" />
<xs:attribute name="MinSpacing" type="xs:double" use="optional" default="100" />
```

Then regenerate:
```powershell
.\BuildDefinitions.ps1
```

The generated `GenerationConfiguration.cs` will now include:
```csharp
[XmlAttribute("MinSpacing")]
public double MinSpacing { get; set; } = 100;
```

---

## ⚠️ Important Notes

1. **Never edit generated files directly** - They are overwritten on rebuild
2. **Always regenerate after schema changes** - Otherwise code/schema mismatch
3. **Namespace is `Ambient.StoryGenerator`** - All generated classes use this namespace
4. **Theme.cs renamed to ThemeDefinition.cs** - Prevents conflicts with Theme directory

---

## 🎯 Usage in Code

### GenerationConfiguration
```csharp
using Ambient.Game.StoryGenerator;

var loader = new GenerationConfigurationLoader(configsPath);
GenerationConfiguration config = loader.LoadConfig("KyotoGenerated");

Console.WriteLine($"World: {config.WorldRef}");
Console.WriteLine($"Style: {config.GenerationStyle}");
Console.WriteLine($"Locations: {config.SourceLocations.Count}");
```

### ThemeDefinition
```csharp
using Ambient.Game.StoryGenerator;
using System.Xml.Serialization;

var serializer = new XmlSerializer(typeof(ThemeDefinition), "Ambient.StoryGenerator");
using var stream = File.OpenRead("Theme.xml");
var theme = (ThemeDefinition)serializer.Deserialize(stream);

Console.WriteLine($"Theme: {theme.DisplayName}");
Console.WriteLine($"Author: {theme.Author}");
```

---

## 🔍 Validation

**Validate XML against schema:**

```bash
# Using xmllint (Linux/Mac)
xmllint --noout --schema GenerationConfiguration.xsd ../GenerationConfigs/KyotoGeneration.xml

# Using Visual Studio (Windows)
# Open XML file, it will auto-validate if schema reference is correct
```

**Schema location in XML:**
```xml
<GenerationConfiguration xmlns="Ambient.StoryGenerator"
    xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    xsi:schemaLocation="Ambient.StoryGenerator ../DefinitionXsd/GenerationConfiguration.xsd">
```

---

## 📚 Related Files

- **Loader**: `../GenerationConfigurationLoader.cs`
- **Generator**: `../StoryGenerator.cs`
- **Theme Loader**: `../ThemeLoader.cs`
- **Data Configs**: `../GenerationConfigs/*.xml`
- **Theme Data**: `../Themes/*/Theme.xml`
- **Generated Classes**: `../DefinitionGenerated/*.cs` (gitignored)

---

**Last Updated**: 2025-11-23
**Schema Version**: 1.0
**Build Script**: `BuildDefinitions.ps1`
