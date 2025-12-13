# Step 1: Delete and recreate Generated folder
$schemaGeneratedDir = "..\Generated"
$xsdDir = "..\..\Content\Schemas"

if (Test-Path $schemaGeneratedDir) {
    Write-Host "Deleting existing Generated folder..." -ForegroundColor Yellow
    Remove-Item $schemaGeneratedDir -Recurse -Force
}
Write-Host "Creating Generated folder..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $schemaGeneratedDir -Force | Out-Null

# Step 2: Run XSD to generate the big file
Write-Host "Generating C# classes from WorldDefinition.xsd..." -ForegroundColor Green
$result = & xsd "$xsdDir\WorldDefinition.xsd" /classes /namespace:Ambient.Domain /out:$schemaGeneratedDir
if ($LASTEXITCODE -ne 0) {
    Write-Host "XSD generation failed" -ForegroundColor Red
    exit
}

$inputFile = "$schemaGeneratedDir\WorldDefinition.cs"
if (-not (Test-Path $inputFile)) {
    Write-Host "File not found: $inputFile" -ForegroundColor Red
    exit
}

$lines = Get-Content $inputFile
$outputDir = Split-Path $inputFile

# Find where namespace starts
$namespaceStart = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^namespace Ambient\.Domain") {
        $namespaceStart = $i
        break
    }
}

if ($namespaceStart -eq -1) {
    Write-Host "Could not find namespace" -ForegroundColor Red
    exit
}

# Get the header (everything before namespace)
$header = $lines[0..($namespaceStart-1)] -join "`n"

# Process each class/enum
$currentType = $null
$currentLines = @()
$braceCount = 0
$inType = $false

for ($i = $namespaceStart + 1; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    
    # Found a class or enum declaration
    if ($line -match "^\s*public\s+(partial\s+class|enum)\s+(\w+)") {
        # Save previous type if we have one
        if ($currentType -and $currentLines.Count -gt 0) {
            $fileName = "$currentType.cs"
            $filePath = Join-Path $outputDir $fileName
            
            $fileContent = $header + "`n`nnamespace Ambient.Domain {`n"
            $fileContent += ($currentLines -join "`n")
            $fileContent += "`n}"
            
            Set-Content $filePath $fileContent -Encoding UTF8
            Write-Host "Created: $fileName"
        }
        
        # Start new type - collect attributes from previous lines
        $currentType = $matches[2]
        $currentLines = @()
        
        # Look backwards to collect attributes and comments
        $attrStartLine = $i - 1
        while ($attrStartLine -gt $namespaceStart -and 
               ($lines[$attrStartLine] -match "^\s*\[" -or 
                $lines[$attrStartLine] -match "^\s*///" -or
                $lines[$attrStartLine] -match "^\s*$")) {
            $attrStartLine--
        }
        $attrStartLine++ # Move back to first attribute/comment line
        
        # Add all attributes/comments before the class
        for ($j = $attrStartLine; $j -lt $i; $j++) {
            if ($lines[$j] -match "^\s*(\[|///)" -or $lines[$j] -match "^\s*$") {
                $currentLines += $lines[$j]
            }
        }
        
        $braceCount = 0
        $inType = $true
    }
    
    # If we're inside a type, collect lines
    if ($inType) {
        $currentLines += $line
        
        # Count braces
        $openBraces = ($line | Select-String '{' -AllMatches).Matches.Count
        $closeBraces = ($line | Select-String '}' -AllMatches).Matches.Count
        $braceCount += $openBraces - $closeBraces
        
        # End of type when braces balance
        if ($braceCount -eq 0 -and $line -match '}') {
            $inType = $false
        }
    }
}

# Save the last type
if ($currentType -and $currentLines.Count -gt 0) {
    $fileName = "$currentType.cs"
    $filePath = Join-Path $outputDir $fileName
    
    $fileContent = $header + "`n`nnamespace Ambient.Domain {`n"
    $fileContent += ($currentLines -join "`n")
    $fileContent += "`n}"
    
    Set-Content $filePath $fileContent -Encoding UTF8
    Write-Host "Created: $fileName"
}

# Step 3: Remove the original big file
Remove-Item $inputFile -Force
Write-Host "Removed original file: $inputFile" -ForegroundColor Yellow
Write-Host "Done! Individual class files created in Generated folder." -ForegroundColor Green