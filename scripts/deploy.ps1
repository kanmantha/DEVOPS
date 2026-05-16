param(
    [Parameter(Mandatory = $false)]
    [string]$Environment = "Development",

    [Parameter(Mandatory = $false)]
    [string]$PublishPath = "./publish"
)

Write-Host "=== DevOps MVC App Deployment ===" -ForegroundColor Cyan
Write-Host "Environment : $Environment"
Write-Host "Publish Path : $PublishPath"
Write-Host ""

# 1. Restore
Write-Host "[1/4] Restoring packages..." -ForegroundColor Yellow
dotnet restore src/DevopsMvcApp/DevopsMvcApp.csproj
if ($LASTEXITCODE -ne 0) { throw "Restore failed" }

# 2. Build
Write-Host "[2/4] Building..." -ForegroundColor Yellow
dotnet build src/DevopsMvcApp/DevopsMvcApp.csproj -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# 3. Run EF migrations
Write-Host "[3/4] Running EF migrations..." -ForegroundColor Yellow
dotnet ef database update --project src/DevopsMvcApp/DevopsMvcApp.csproj
if ($LASTEXITCODE -ne 0) { throw "Migration failed" }

# 4. Publish
Write-Host "[4/4] Publishing..." -ForegroundColor Yellow
dotnet publish src/DevopsMvcApp/DevopsMvcApp.csproj -c Release -o $PublishPath
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Write-Host ""
Write-Host "=== Deployment complete! ===" -ForegroundColor Green
Write-Host "Published to: $PublishPath"
