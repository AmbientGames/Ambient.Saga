# Sort generated schema files into organized folders
# Logic: Define Presentation and Simulation explicitly, everything else goes to Gameplay

Write-Host "Sorting generated schema files..." -ForegroundColor Green

$schemaGeneratedDir = "..\Generated"
$gameplayDir = "$schemaGeneratedDir\Gameplay"
$simulationDir = "$schemaGeneratedDir\Simulation"
$presentationDir = "$schemaGeneratedDir\Presentation"

New-Item -ItemType Directory -Path $gameplayDir -Force | Out-Null
New-Item -ItemType Directory -Path $simulationDir -Force | Out-Null
New-Item -ItemType Directory -Path $presentationDir -Force | Out-Null

# Define the exceptions - everything else defaults to Gameplay
$presentationWords = @("Asset", "Model")
$simulationWords = @("Block", "Climate", "Living", "Material", "Substance", "State", "Style", "Process", "Simulation", "Tree", "Shrub", "Ground", "Leaf", "Static")

# Get all generated CS files
$allFiles = Get-ChildItem "$schemaGeneratedDir\*.cs"

foreach ($file in $allFiles) {
    $className = $file.BaseName
    $destDir = $gameplayDir
    $category = "Gameplay"

    # Check presentation first
    foreach ($word in $presentationWords) {
        if ($className -like "*$word*") {
            $destDir = $presentationDir
            $category = "Presentation"
            break
        }
    }

    # Check simulation if not presentation
    if ($category -eq "Gameplay") {
        foreach ($word in $simulationWords) {
            if ($className -like "*$word*") {
                $destDir = $simulationDir
                $category = "Simulation"
                break
            }
        }
    }

    Move-Item $file.FullName (Join-Path $destDir $file.Name)
    Write-Host "Moved: $($file.Name) to $category"
}

Write-Host "`n=== Schema Sorting Complete ===" -ForegroundColor Cyan
Write-Host "Files organized into domain-specific folders" -ForegroundColor Green