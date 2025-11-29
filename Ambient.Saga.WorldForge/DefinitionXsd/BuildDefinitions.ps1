# Generate C# classes from XSD schemas

Write-Host "=== Generating Story Generator Definition Classes ===" -ForegroundColor Cyan

# Step 1: Delete and recreate DefinitionGenerated folder
$schemaGeneratedDir = "..\DefinitionGenerated"
if (Test-Path $schemaGeneratedDir) {
    Write-Host "Deleting existing DefinitionGenerated folder..." -ForegroundColor Yellow
    Remove-Item $schemaGeneratedDir -Recurse -Force
}
Write-Host "Creating DefinitionGenerated folder..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $schemaGeneratedDir -Force | Out-Null

# Step 2: Generate classes from GenerationConfiguration.xsd
Write-Host "`nGenerating GenerationConfiguration classes..." -ForegroundColor Green
& xsd "GenerationConfiguration.xsd" /classes /namespace:Ambient.StoryGenerator /out:$schemaGeneratedDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "GenerationConfiguration XSD generation failed!" -ForegroundColor Red
    exit 1
}

# Step 3: Generate classes from Theme.xsd
Write-Host "Generating Theme classes..." -ForegroundColor Green
& xsd "Theme.xsd" /classes /namespace:Ambient.StoryGenerator /out:$schemaGeneratedDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "Theme XSD generation failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Generation Complete ===" -ForegroundColor Cyan
Write-Host "Generated classes:" -ForegroundColor Green
Get-ChildItem $schemaGeneratedDir -Filter "*.cs" | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor Gray
}

Write-Host "`nNote: These classes are auto-generated from XSD schemas." -ForegroundColor Yellow
Write-Host "Do not modify generated files directly. Update the XSD and regenerate." -ForegroundColor Yellow
