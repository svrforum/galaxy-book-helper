param([string]$Tag='v0.2.2-experimental')
$ErrorActionPreference='Stop'
if($Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+-experimental$'){throw 'Invalid experimental release tag.'}
$root=Split-Path $PSScriptRoot -Parent
$repo='svrforum/galaxy-book-helper'
$commit=(& git -C $root rev-parse HEAD).Trim()
if(& git -C $root status --porcelain){throw 'Commit the release sources before publishing.'}
# Git Credential Manager supplies the existing GitHub authorization. Never print it.
$credential="protocol=https`nhost=github.com`n`n" | & git credential fill
$password=@($credential | Where-Object {$_ -like 'password=*'})
if($password.Count -ne 1){throw 'GitHub credential is unavailable.'}
$headers=@{Authorization='Bearer '+$password[0].Substring(9);Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28'}
$api="https://api.github.com/repos/$repo"
$release=@((Invoke-RestMethod "$api/releases?per_page=100" -Headers $headers) | Where-Object {$_.tag_name -eq $Tag})
if($release.Count -ne 1){throw 'Expected exactly one matching release draft.'}
$release=$release[0]
if(!$release.draft -or $release.target_commitish -ne $commit){throw 'Expected an unpublished draft targeting the current commit. Existing public releases are never replaced.'}
$runs=Invoke-RestMethod "$api/actions/runs?head_sha=$commit&per_page=50" -Headers $headers
$ci=@($runs.workflow_runs | Where-Object {$_.name -eq 'Verified experimental app release'})
if(!$ci.Count -or $ci[0].status -ne 'completed' -or $ci[0].conclusion -ne 'success'){throw 'Release CI has not succeeded for this exact commit.'}
$folder=Join-Path $root "artifacts/release-$Tag"
New-Item -ItemType Directory -Force $folder | Out-Null
$setup=Join-Path $root 'artifacts/GalaxyHelper-Setup.exe'
if(!(Test-Path $setup)){throw 'Build and verify the full Setup EXE first.'}
$report=Join-Path $folder 'package-check.txt'
$p=Start-Process $setup -ArgumentList @('--verify-installation',$report) -WindowStyle Hidden -Wait -PassThru
if($p.ExitCode -ne 0 -or !(Test-Path $report)){throw 'Setup embedded package verification failed.'}
$name="GalaxyHelper-$Tag-Setup.exe"
Copy-Item $setup (Join-Path $folder $name)
Copy-Item (Join-Path $root 'packaging/FIRST-START.ko.md') (Join-Path $folder 'FIRST-START.ko.md')
$hashLines=@()
foreach($asset in $release.assets){
 if($asset.name -in @('SHA256SUMS.txt',$name,'FIRST-START.ko.md')){continue}
 $path=Join-Path $folder $asset.name
 if([IO.Path]::GetFileName($asset.name) -ne $asset.name){throw 'Unsafe remote asset name.'}
 Invoke-WebRequest "$api/releases/assets/$($asset.id)" -Headers (@{Authorization=$headers.Authorization;Accept='application/octet-stream'}) -OutFile $path
 $hashLines+=((Get-FileHash $path).Hash.ToLowerInvariant()+'  '+$asset.name)
}
foreach($file in @($name,'FIRST-START.ko.md')){$hashLines+=((Get-FileHash (Join-Path $folder $file)).Hash.ToLowerInvariant()+'  '+$file)}
[IO.File]::WriteAllLines((Join-Path $folder 'SHA256SUMS.txt'),$hashLines,(New-Object Text.UTF8Encoding $false))
foreach($file in @($name,'FIRST-START.ko.md','SHA256SUMS.txt')){
 $old=@($release.assets | Where-Object {$_.name -eq $file})
 foreach($asset in $old){Invoke-RestMethod "$api/releases/assets/$($asset.id)" -Method Delete -Headers $headers | Out-Null}
 $uri=$release.upload_url.Split('{')[0]+'?name='+[Uri]::EscapeDataString($file)
 Invoke-RestMethod $uri -Method Post -Headers $headers -ContentType 'application/octet-stream' -InFile (Join-Path $folder $file) | Out-Null
}
$assets=(Invoke-RestMethod "$api/releases/$($release.id)" -Headers $headers).assets
foreach($file in @($name,'FIRST-START.ko.md','SHA256SUMS.txt')){if(!($assets | Where-Object {$_.name -eq $file -and $_.size -eq (Get-Item (Join-Path $folder $file)).Length})){throw 'Uploaded release asset size mismatch.'}}
$body=@{draft=$false;prerelease=$true;body=[IO.File]::ReadAllText((Join-Path $root "packaging/RELEASE-NOTES-$Tag.ko.md"))}|ConvertTo-Json
$published=Invoke-RestMethod "$api/releases/$($release.id)" -Method Patch -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
Write-Output $published.html_url
