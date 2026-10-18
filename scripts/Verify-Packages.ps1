param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (!$Version) {
        [xml]$props = Get-Content Directory.Build.props -Raw
        $Version = $props.Project.PropertyGroup.Version
    }
    if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Versão inválida.' }
    $feed = Join-Path $root "artifacts/packages/$Version"
    $cache = Join-Path $root ("artifacts/consumer-cache/" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $feed, $cache | Out-Null
    dotnet pack Aspleo.Cqrs.slnx -c Release "-p:Version=$Version" -o $feed
    if ($LASTEXITCODE) { throw 'Falha ao empacotar.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($id in @('Aspleo.Cqrs.Abstractions', 'Aspleo.Cqrs.DependencyInjection')) {
        $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $feed "$id.$Version.nupkg"))
        try {
            if (!$archive.GetEntry('README.md') -or !$archive.GetEntry("lib/net10.0/$id.dll")) { throw "Conteúdo inválido: $id" }
            $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($manifest.package.metadata.version -ne $Version) { throw 'Versão divergente.' }
            if ($manifest.package.metadata.authors -ne 'Corporação Aspleo') { throw 'Autoria divergente.' }
            if ($manifest.package.metadata.license.type -ne 'expression' -or $manifest.package.metadata.license.InnerText -ne 'MIT') { throw 'Licença MIT ausente ou inválida.' }
            if (!$archive.GetEntry('LICENSE')) { throw 'Arquivo LICENSE ausente.' }
            $deps = @($manifest.package.metadata.dependencies.group.dependency | Where-Object { $_ })
            if ($id.EndsWith('Abstractions') -and $deps.Count) { throw 'Abstractions deve ser independente.' }
            if ($id.EndsWith('DependencyInjection')) {
                $dependency = $deps | Where-Object id -eq 'Aspleo.Cqrs.Abstractions'
                if (!$dependency -or $dependency.version -notin @($Version, "[$Version, )", "[$Version]")) { throw 'Dependência interna divergente.' }
            }
        } finally { $archive.Dispose() }
    }
    $config = Join-Path $cache 'nuget.config'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    @"
<configuration>
  <packageSources><clear /><add key="local" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><packageSource key="local"><package pattern="Aspleo.Cqrs.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content $config
    $consumer = 'examples/LocalConsumer/LocalConsumer.csproj'
    dotnet restore $consumer --configfile $config --packages $cache --force --no-http-cache "-p:Version=$Version"
    if ($LASTEXITCODE) { throw 'Falha no restore pelo feed local.' }
    dotnet run --project $consumer -c Release --no-restore "-p:Version=$Version"
    if ($LASTEXITCODE) { throw 'Falha no consumidor NuGet.' }
    Write-Output "Pacotes e consumidor validados: $feed"
} finally { Pop-Location }
