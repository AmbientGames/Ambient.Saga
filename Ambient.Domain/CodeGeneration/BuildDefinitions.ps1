# Build and organize XML definition classes
# This script runs the complete definition generation and organization process

Write-Host "=== Building XML definition Classes ===" -ForegroundColor Cyan

# Step 1: Generate definition classes
Write-Host "`nStep 1: Generating definition classes..." -ForegroundColor Yellow
& .\GenerateDefinitions.ps1

if ($LASTEXITCODE -ne 0) {
    Write-Host "definition generation failed!" -ForegroundColor Red
    exit 1
}

# Step 2: Sort generated files into organized folders
Write-Host "`nStep 2: Organizing generated files..." -ForegroundColor Yellow
& .\SortGeneratedDefinitions.ps1

if ($LASTEXITCODE -ne 0) {
    Write-Host "definition sorting failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n=== Definition Build Complete ===" -ForegroundColor Green
Write-Host "Generated classes are organized in DefinitionGenerated folder" -ForegroundColor Green