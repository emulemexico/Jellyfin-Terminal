param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Changelog = "Release $Version"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Jellyfin Terminal - Release Script ===" -ForegroundColor Cyan
Write-Host "Target Version: $Version"

# 1. Comprobar Git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Error: 'git' no esta instalado o no se encuentra en PATH."
}

# 2. Comprobar gh
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "Error: 'gh' (GitHub CLI) no esta instalado o no se encuentra en PATH."
}

# 3. Comprobar autenticacion de GitHub
Write-Host "Verificando autenticacion en GitHub..."
gh auth status
if ($LASTEXITCODE -ne 0) {
    throw "Error: No hay una sesion activa en GitHub CLI. Ejecute 'gh auth login' antes de continuar."
}

$owner = (gh api user --jq .login).Trim()
if (-not $owner) {
    throw "Error: No se pudo obtener el nombre de usuario de GitHub."
}
Write-Host "Usuario GitHub detectado: $owner" -ForegroundColor Green

# 4. Comprobar .NET 9
$dotnetVer = (dotnet --version 2>$null)
if (-not $dotnetVer -or -not ($dotnetVer.StartsWith("9."))) {
    if (Test-Path "$HOME\.dotnet\dotnet.exe") {
        $env:PATH = "$HOME\.dotnet;$env:PATH"
        $dotnetVer = (dotnet --version 2>$null)
    }
}
if (-not $dotnetVer -or -not ($dotnetVer.StartsWith("9."))) {
    throw "Error: Se requiere el SDK de .NET 9.x. Version actual: $dotnetVer"
}
Write-Host "SDK .NET detectado: $dotnetVer" -ForegroundColor Green

# 5. dotnet clean, restore, build Release
Write-Host "Limpiando proyecto..."
dotnet clean -c Release
if ($LASTEXITCODE -ne 0) { throw "Fallo en 'dotnet clean'." }

Write-Host "Restaurando dependencias..."
dotnet restore
if ($LASTEXITCODE -ne 0) { throw "Fallo en 'dotnet restore'." }

Write-Host "Compilando Release..."
dotnet build -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "Fallo en 'dotnet build -c Release'. Abortando release." }

$dllPath = "bin\Release\net9.0\Jellyfin.Plugin.Terminal.dll"
if (-not (Test-Path $dllPath)) {
    throw "Error: No se encontro la DLL generada en $dllPath."
}

# 6. Crear dist y generar ZIP
$distDir = "dist"
$stagingDir = "dist\staging"
if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir -Force | Out-Null }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

Copy-Item $dllPath $stagingDir
$zipName = "Jellyfin.Plugin.Terminal_${Version}.zip"
$zipPath = Join-Path $distDir $zipName

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stagingDir\*" -DestinationPath $zipPath -Force
Remove-Item $stagingDir -Recurse -Force

# 7. Calcular MD5
$md5 = (Get-FileHash $zipPath -Algorithm MD5).Hash.ToLower()
Write-Host "ZIP generado: $zipPath" -ForegroundColor Green
Write-Host "MD5 Checksum: $md5" -ForegroundColor Green

# 8. Actualizar build.yaml
$buildYamlPath = "build.yaml"
if (Test-Path $buildYamlPath) {
    $content = Get-Content $buildYamlPath -Raw
    $content = $content -replace '(?m)^version:\s*".*?"', "version: `"$Version`""
    $content = $content -replace '(?m)^owner:\s*".*?"', "owner: `"$owner`""
    Set-Content -Path $buildYamlPath -Value $content -Encoding UTF8
    Write-Host "build.yaml actualizado." -ForegroundColor Green
}

# 9. Actualizar manifest.json
$manifestPath = "manifest.json"
if (-not (Test-Path $manifestPath)) {
    throw "Error: No se encontro $manifestPath."
}

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$sourceUrl = "https://github.com/$owner/Jellyfin-Terminal/releases/download/v$Version/$zipName"

$manifestJson = Get-Content $manifestPath -Raw | ConvertFrom-Json
$pluginEntry = $manifestJson | Where-Object { $_.guid -eq "a5e4d291-7643-41bb-b892-91e84fc5711b" }

if (-not $pluginEntry) {
    throw "Error: No se encontro el plugin en manifest.json."
}

$newVersionObj = [PSCustomObject]@{
    version   = $Version
    changelog = $Changelog
    targetAbi = "10.11.0.0"
    sourceUrl = $sourceUrl
    checksum  = $md5
    timestamp = $timestamp
}

$existingVersions = @($pluginEntry.versions | Where-Object { $_.version -ne $Version })
$pluginEntry.versions = @($newVersionObj) + $existingVersions
$pluginEntry.owner = $owner

$updatedJson = @($manifestJson) | ConvertTo-Json -Depth 10
if (-not $updatedJson.Trim().StartsWith("[")) {
    $updatedJson = "[`n$updatedJson`n]"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($manifestPath, $updatedJson, $utf8NoBom)

# Validar manifest.json
Get-Content $manifestPath -Raw | ConvertFrom-Json | Out-Null
Write-Host "manifest.json actualizado y validado." -ForegroundColor Green

# 10. Git commit y tag
Write-Host "Creando commit y tag en Git..."
git add .
git commit -m "Release Terminal $Version"
git tag -a "v$Version" -m "Terminal $Version"

# 11. Push main y tag
Write-Host "Haciendo push a GitHub..."
git push origin main
git push origin "v$Version"

# 12. GitHub Release
Write-Host "Creando GitHub Release v$Version..."
gh release create "v$Version" $zipPath --title "Terminal $Version" --notes "$Changelog"

# 13. Verificar Release y Asset en GitHub
Write-Host "Verificando disponibilidad del asset en GitHub..."
try {
    $relCheck = gh release view "v$Version" --json assets --jq ".assets[].name"
    if ($relCheck -match [regex]::Escape($zipName)) {
        Write-Host "Verificacion exitosa: Asset '$zipName' publicado en GitHub Release v$Version." -ForegroundColor Green
    } else {
        Write-Host "Advertencia: No se encontro el asset en la respuesta de GitHub CLI." -ForegroundColor Yellow
    }
} catch {
    Write-Host "Nota: $($_.Exception.Message)" -ForegroundColor Yellow
}

Write-Host "Release $Version completado con exito!" -ForegroundColor Green