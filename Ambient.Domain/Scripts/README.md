# Ambient Domain Schemas

## Entity Definition Standard

All game entities (blocks, tools, climate zones, etc.) follow this naming convention:

- **`RefName`** - Unique string identifier used for references (e.g., "stone", "iron_pickaxe", "tropical_wet")
- **`DisplayName`** - Human-readable name for UI display
- **`OrdinalId`** - If required, a numeric ID for efficient engine/networking use (e.g., 0-511 for blocks)
- **'Description'** - optional

  Partial Architecture:
  AcquirableBase (no trading, just ModelRef/TextureRef)
  ├── QuestToken (not tradeable) ✅
  └── TradeableAcquirable (adds TradePricingAttributes)
      ├── StackableAcquirable
      │   ├── Consumable
      │   ├── BuildingMaterial
      │   └── Block (moves here)
      └── DegradableAcquirable
          ├── Equipment
          ├── Tool
          └── Spell

  SagaFocus
  ├── Quest token requirements/rewards
  ├── EnterRadius
  └── Effects (optional CharacterEffects or RewardEffects)
      ├── Landmark: discovery buffs (inspiration, knowledge) - uses RewardEffects (can grant Credits/XP)
      ├── Structure: shelter effects (warmth, healing, loot) - uses CharacterEffects
      └── QuestSignpost: quest acceptance buffs (courage, determination) - uses CharacterEffects

## References

When referencing entities from other entities, use string-based reference attributes:
- **`EntityRef`** attributes (e.g., `ClimateZoneRef`, `ToolRef`, `MaterialRef`) accept any string value
- This allows unlimited extensibility - modders can reference any custom entities without schema changes

## Schema Generation

The schema generation process has been automated with PowerShell scripts.
The workflow generates individual class files organized into domain-specific folders instead of one large WorldDefinition.cs file.

### Directory Structure

- `Content/Schemas/` - XSD schemas (at solution root, shared across projects)
- `Content/Worlds/` - XML world definitions (game content data)
- `Ambient.Domain/Scripts/` - Generation scripts (this folder)
- `Ambient.Domain/Generated/` - Output folder for auto-generated C# classes
- `Ambient.Domain/Partials/` - Partial classes extending generated types

### To regenerate the schema files:

1) Open a PowerShell prompt via Tools -> Command Line -> Developer PowerShell
2) cd Ambient.Domain\Scripts
3) .\BuildDefinitions.ps1

This runs the complete process:
1. Deletes and recreates the `Generated` folder
2. Generates C# classes from `Content\Schemas\WorldDefinition.xsd` using XSD.exe
3. Splits the large WorldDefinition.cs into individual class files
4. Organizes classes into domain folders (Gameplay, Simulation, Presentation, Core, Enums)

### Output Structure

Generated classes are organized as:
- `Generated/Gameplay/` - RPG features (Achievement, Quest, Skill, Equipment, etc.)
- `Generated/Simulation/` - Engine features (Block, Climate, Living, Material, etc.)
- `Generated/Presentation/` - Graphics (Asset, Model)
- `Generated/Core/` - Core schema types (World, Schema)
- `Generated/Enums/` - Enumeration types

