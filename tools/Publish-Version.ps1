param(
    [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$GitPath='git',
    [string]$GhPath='gh'
)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root

# Проверка назначения публикации
function Invoke-Checked([string]$Program,[string[]]$Arguments) {
    & $Program @Arguments
    if($LASTEXITCODE -ne 0){throw "$Program failed: $LASTEXITCODE"}
}
$current=Get-Content (Join-Path $root 'current-version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$repo=$current.repository
$relative="versions/$Version"
$folder=Join-Path $root $relative
$metadata=Get-Content (Join-Path $folder 'version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($metadata.version -ne $Version -or $metadata.tag -ne "v$Version"){throw 'Version metadata mismatch'}
$project=Join-Path $folder 'src/RiverGuard.App/RiverGuard.App.csproj'
$projectXml=[xml](Get-Content $project -Raw -Encoding UTF8)
if($projectXml.Project.PropertyGroup.Version -ne $Version){throw 'Project version mismatch'}
$repositoryState=& $GhPath repo view $repo --json isPrivate
if($LASTEXITCODE -ne 0){throw 'Authenticated repository access is required'}
$wasPrivate=($repositoryState | ConvertFrom-Json).isPrivate
if(!$wasPrivate){Write-Warning 'Repository is public; visibility will remain unchanged.'}
$releases=& $GhPath release list --repo $repo --limit 1000 --json tagName
if($LASTEXITCODE -ne 0){throw 'Cannot query existing releases'}
if(($releases | ConvertFrom-Json).tagName -contains "v$Version"){throw 'Release exists; refusing to overwrite it'}
$branch=& $GitPath branch --show-current
if($LASTEXITCODE -ne 0 -or $branch -ne 'main'){throw 'Publish from main only'}

# Сборка и проверки
Invoke-Checked dotnet @('build',(Join-Path $folder 'src/RiverGuard.sln'),'-c','Release','--nologo')
Invoke-Checked dotnet @('run','--project',(Join-Path $folder 'src/RiverGuard.Tests'),'-c','Release','--no-build','--','--catalog-tests','--catalog-report',(Join-Path $folder 'docs/core-checks.json'))
$app=Join-Path $folder 'app'
Invoke-Checked dotnet @('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:DebugType=None','-p:DebugSymbols=false','-o',$app,'--nologo')
$started=[DateTime]::UtcNow
$process=Start-Process -FilePath (Join-Path $app 'RiverGuard.exe') -ArgumentList '--smoke-test' -Wait -PassThru
$success=Join-Path $app 'SmokeTest/success.txt'
if((Test-Path (Join-Path $app 'smoke-error.txt')) -or !(Test-Path $success)){throw 'Application smoke test failed'}
if((Get-Item $success).LastWriteTimeUtc -lt $started){throw 'Stale smoke-test result'}

# Комплект только одной версии
$output=Join-Path $root ("artifacts/$Version/"+[Guid]::NewGuid().ToString('N'))
$payload=Join-Path $output "RiverGuard-$Version"
New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Item (Join-Path $folder 'THIRD_PARTY_NOTICES.txt') (Join-Path $app 'THIRD_PARTY_NOTICES.txt') -Force
Get-ChildItem $app -File | Where-Object { $_.Extension -in @('.exe','.dll','.json','.config','.txt') -and $_.Name -ne 'smoke-error.txt' } | Copy-Item -Destination $payload
"RiverGuard $Version`nАвтор приложения: Apdulium-beep.`nРаспакуйте ZIP целиком и запустите RiverGuard.exe.`nИсследовательская модель, не сертифицированный расчёт аварии ГТС.`nЛицензии компонентов: THIRD_PARTY_NOTICES.txt." | Set-Content (Join-Path $payload 'README.txt') -Encoding UTF8
$zip=Join-Path $output "RiverGuard-$Version-win-x64.zip"
Compress-Archive -Path $payload -DestinationPath $zip
$sha=Join-Path $output "RiverGuard-$Version-SHA256.txt"
((Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path $zip -Leaf)) | Set-Content $sha -Encoding UTF8

# Текущая ветка содержит только текущую версию
$ignorePath=Join-Path $root '.gitignore'
$ignore=Get-Content $ignorePath -Raw -Encoding UTF8
if($ignore -notmatch '(?s)# BEGIN CURRENT VERSION.*?# END CURRENT VERSION'){throw 'Missing gitignore version marker'}
$ignore=[regex]::Replace($ignore,'(?s)# BEGIN CURRENT VERSION.*?# END CURRENT VERSION',"# BEGIN CURRENT VERSION`nversions/*/`n!versions/$Version/`n# END CURRENT VERSION")
Set-Content $ignorePath $ignore -Encoding UTF8
$readmePath=Join-Path $root 'README.md'
$readme=Get-Content $readmePath -Raw -Encoding UTF8
if($readme -notmatch '(?s)<!-- BEGIN CURRENT VERSION -->.*?<!-- END CURRENT VERSION -->'){throw 'Missing README version marker'}
$block=@"
<!-- BEGIN CURRENT VERSION -->
## Текущая версия: $Version

- **[Скачать программу для Windows x64](https://github.com/$repo/releases/download/v$Version/RiverGuard-$Version-win-x64.zip)**
- [Релиз и контрольная сумма](https://github.com/$repo/releases/tag/v$Version)
- [Исходники и документация этой версии](versions/$Version/README.md)
<!-- END CURRENT VERSION -->
"@
Set-Content $readmePath ([regex]::Replace($readme,'(?s)<!-- BEGIN CURRENT VERSION -->.*?<!-- END CURRENT VERSION -->',$block)) -Encoding UTF8
if($current.version -ne $Version){Invoke-Checked $GitPath @('rm','-r','--cached','--ignore-unmatch','--',$current.directory)}
@{version=$Version;directory=$relative;tag="v$Version";repository=$repo} | ConvertTo-Json | Set-Content (Join-Path $root 'current-version.json') -Encoding UTF8
Invoke-Checked $GitPath @('add','--','.gitignore','README.md','current-version.json',$relative)
& $GitPath diff --cached --quiet
if($LASTEXITCODE -eq 1){Invoke-Checked $GitPath @('commit','-m',"Release RiverGuard $Version - Apdulium-beep")}
elseif($LASTEXITCODE -ne 0){throw 'Cannot inspect staged changes'}
$commit=& $GitPath rev-parse HEAD
if($LASTEXITCODE -ne 0){throw 'Cannot identify release commit'}
$resolvedGh=(Get-Command $GhPath).Source.Replace('\','/')
$helper='!"'+$resolvedGh+'" auth git-credential'
Invoke-Checked $GitPath @('-c','credential.helper=','-c',"credential.helper=$helper",'push','origin','main')

# Релиз привязан к проверенному коммиту
$notes=Join-Path $output 'release.md'
@"
# RiverGuard $Version

Автор приложения: **Apdulium-beep**.

Windows x64: распакуйте ZIP целиком и запустите RiverGuard.exe. В архиве только версия $Version, без пользовательских проектов и тестовых хранилищ. Установка .NET и Office не требуется. Контрольная сумма приложена.

Исходники, документация и инструкция: [versions/$Version](https://github.com/$repo/tree/v$Version/versions/$Version).

Сборка прошла проверки ядра и интерфейса. Обязательные лицензии компонентов сохранены. Исследовательская модель, не сертифицированный инженерный расчёт.
"@ | Set-Content $notes -Encoding UTF8
Invoke-Checked $GhPath @('release','create',"v$Version",$zip,$sha,'--repo',$repo,'--target',$commit,'--title',"RiverGuard $Version · Apdulium-beep",'--notes-file',$notes,'--latest')
$remote=& $GhPath api "repos/$repo/releases/tags/v$Version"
if($LASTEXITCODE -ne 0){throw 'Cannot verify uploaded release'}
$assets=($remote | ConvertFrom-Json).assets
foreach($file in @($zip,$sha)){
    $asset=$assets | Where-Object { $_.name -eq (Split-Path $file -Leaf) }
    if(!$asset -or $asset.size -ne (Get-Item $file).Length){throw 'Uploaded asset mismatch'}
    if($asset.digest -and $asset.digest -ne ('sha256:'+(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant())){throw 'Uploaded checksum mismatch'}
}
$repositoryState=& $GhPath repo view $repo --json isPrivate
if($LASTEXITCODE -ne 0 -or ($repositoryState | ConvertFrom-Json).isPrivate -ne $wasPrivate){throw 'Repository visibility changed during publication'}
if($env:OS -eq 'Windows_NT'){
    $link=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $root 'Запустить RiverGuard.lnk'))
    $link.TargetPath=Join-Path $app 'RiverGuard.exe';$link.WorkingDirectory=$app;$link.IconLocation=$link.TargetPath+',0';$link.Description="RiverGuard $Version · Apdulium-beep";$link.Save()
}
Write-Output "Published and verified: https://github.com/$repo/releases/tag/v$Version"
