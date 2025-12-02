# Sort generated schema files into organized folders

Write-Host "Sorting generated schema files..." -ForegroundColor Green

# Create organized output directories
$schemaGeneratedDir = "..\DefinitionGenerated"
$gameplayDir = "$schemaGeneratedDir\Gameplay"
$simulationDir = "$schemaGeneratedDir\Simulation" 
$presentationDir = "$schemaGeneratedDir\Presentation"
$enumsDir = "$schemaGeneratedDir\Enums"
$coreDir = "$schemaGeneratedDir\Core"

New-Item -ItemType Directory -Path $gameplayDir -Force | Out-Null
New-Item -ItemType Directory -Path $simulationDir -Force | Out-Null
New-Item -ItemType Directory -Path $presentationDir -Force | Out-Null
New-Item -ItemType Directory -Path $enumsDir -Force | Out-Null
New-Item -ItemType Directory -Path $coreDir -Force | Out-Null

# Define keywords for each category (order matters - first match wins)
$presentationWords = @("Asset", "Model")
$gameplayWords = @("Saga", "Interactable", "Trigger", "Structure", "Quest", "Landmark", "Character", "Avatar", "Dialogue", "Tool", "Equipment", "Consumable", "Spell", "Reward", "Achievement", "Acquirable", "Summon", "Affinity", "Entity", "Comparison", "Condition", "Item", "CombatStance", "StatusEffect", "Reputation", "Narrative", "Loadout", "Faction", "Content", "Procedural", "Party")
$simulationWords = @("Block", "Climate", "Living", "Material", "Substance", "State", "Style", "Process", "Simulation", "Tree", "Shrub", "Ground", "Leaf", "Static", "ItemChoice")
$coreWords = @("World", "Schema", "Gameplay", "Presentation", "Preset", "HeightMap", "Procedural", "TemplateMetadata")
$enumTypes = @("WorldGameMode", "BlockMode", "EffectLevel", "StaticBlockGenerationMode", "BlockCategory", "DispositionType", "TextureGenerationMode", "LeafShape", "SystemSubstance", "SystemState", "SystemStyle", "SystemLivingShape", "WorldDataSource", "ProceduralGenerationMode", "WorldLifeMode", "TriggerType", "Seed", "ChunkCount", "LoadoutSlot", "ToolType", "QuestDifficulty", "ItemUseType", "CharacterTraitType")

# Get all generated CS files
$allFiles = Get-ChildItem "$schemaGeneratedDir\*.cs"

foreach ($file in $allFiles) {
    $className = $file.BaseName
    $moved = $false
    
    # Check if it's an enum
    if ($enumTypes -contains $className) {
        Move-Item $file.FullName (Join-Path $enumsDir $file.Name)
        Write-Host "Moved: $($file.Name) to Enums"
        continue
    }
    
    # Check presentation keywords first (Asset beats Material)
    foreach ($word in $presentationWords) {
        if ($className -like "*$word*") {
            $destDir = Join-Path $presentationDir $word
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Move-Item $file.FullName (Join-Path $destDir $file.Name)
            Write-Host "Moved: $($file.Name) to Presentation\$word"
            $moved = $true
            break
        }
    }
    
    if ($moved) { continue }
    
    # Check gameplay keywords
    foreach ($word in $gameplayWords) {
        if ($className -like "*$word*") {
            $destDir = Join-Path $gameplayDir $word
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Move-Item $file.FullName (Join-Path $destDir $file.Name)
            Write-Host "Moved: $($file.Name) to Gameplay\$word"
            $moved = $true
            break
        }
    }
    
    if ($moved) { continue }
    
    # Check simulation keywords
    foreach ($word in $simulationWords) {
        if ($className -like "*$word*") {
            $destDir = Join-Path $simulationDir $word
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Move-Item $file.FullName (Join-Path $destDir $file.Name)
            Write-Host "Moved: $($file.Name) to Simulation\$word"
            $moved = $true
            break
        }
    }
    
    if ($moved) { continue }
    
    # Check core keywords
    foreach ($word in $coreWords) {
        if ($className -like "*$word*") {
            $destDir = Join-Path $coreDir $word
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            Move-Item $file.FullName (Join-Path $destDir $file.Name)
            Write-Host "Moved: $($file.Name) to Core\$word"
            $moved = $true
            break
        }
    }
    
    if (-not $moved) {
        Write-Host "Left in root: $($file.Name)" -ForegroundColor Yellow
    }
}

Write-Host "`n=== Schema Sorting Complete ===" -ForegroundColor Cyan
Write-Host "Files organized into domain-specific folders" -ForegroundColor Green