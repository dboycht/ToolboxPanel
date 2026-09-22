# publish-release.ps1 -- create the GitHub Release for ToolboxPanel 2.0.6
# ASCII-only script (PS 5.1 reads BOM-less files as ANSI; Chinese would break quoting).
# Rule: release NAME = version number, release BODY = changelog (UTF-8 read from a file).
# Usage: powershell -NoProfile -File pack\publish-release.ps1
$ErrorActionPreference = 'Stop'
$log = New-Object System.Collections.Generic.List[string]
function Log($m) { $log.Add([string]$m) }

$repo = 'dboycht/ToolboxPanel'
$tag = '2.0.6'
$zip = 'D:\code\github_repository\ToolboxPanel\dist\ToolboxPanel-2.0.6-win-x64.zip'
$bodyFile = 'D:\code\github_repository\ToolboxPanel\pack\release-body-2.0.6.md'
$out = 'D:\code\github_repository\ToolboxPanel\pack\release-result.txt'

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# token: 1) try git credential fill, 2) fall back to Windows Credential Manager (CredRead)
$token = $null
try {
    $cred = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
    $token = ($cred | Where-Object { $_ -like 'password=*' } | Select-Object -First 1) -replace '^password=', ''
} catch { }

if (-not $token) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Cred {
  [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern bool CredRead(string target, int type, int flags, out IntPtr credential);
  [DllImport("advapi32.dll")] static extern void CredFree(IntPtr cred);
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  struct CREDENTIAL {
    public int Flags; public int Type; public string TargetName; public string Comment;
    public long LastWritten; public int CredentialBlobSize; public IntPtr CredentialBlob;
    public int Persist; public int AttributeCount; public IntPtr Attributes;
    public string TargetAlias; public string UserName;
  }
  public static string GetStr(string target, int type) {
    IntPtr p;
    if (!CredRead(target, type, 0, out p)) return null;
    var c = (CREDENTIAL)Marshal.PtrToStructure(p, typeof(CREDENTIAL));
    string s = Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2);
    CredFree(p);
    return s;
  }
}
"@
    $token = [Cred]::GetStr('git:https://github.com', 1)
    Log 'token source: Windows Credential Manager'
} else {
    Log 'token source: git credential fill'
}

if (-not $token) { Log 'ERROR: no token'; $log -join "`n" | Set-Content $out -Encoding UTF8; exit 1 }
Log ('token len=' + $token.Length)

$headers = @{
    Authorization          = "token $token"
    'User-Agent'           = 'dsh-agent'
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

$body = [System.IO.File]::ReadAllText($bodyFile, [System.Text.Encoding]::UTF8)
$payload = @{ tag_name = $tag; name = $tag; body = $body; draft = $false; prerelease = $false }
$json = $payload | ConvertTo-Json -Depth 6
$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)

try {
    $rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
    Log ('release created: id=' + $rel.id + ' name=' + $rel.name + ' tag=' + $rel.tag_name)
    Log ('html_url=' + $rel.html_url)
} catch {
    Log ('ERROR creating release: ' + $_.Exception.Message)
    if ($_.Exception.Response) {
        $sr = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        Log ('response: ' + $sr.ReadToEnd())
    }
    $log -join "`n" | Set-Content $out -Encoding UTF8
    exit 1
}

try {
    $assetBytes = [System.IO.File]::ReadAllBytes($zip)
    $assetName = [System.IO.Path]::GetFileName($zip)
    $asset = Invoke-RestMethod -Method Post -Uri "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=$assetName" -Headers $headers -Body $assetBytes -ContentType 'application/zip'
    Log ('asset uploaded: name=' + $asset.name + ' size=' + $asset.size + ' state=' + $asset.state)
    Log ('asset url=' + $asset.browser_download_url)
} catch {
    Log ('ERROR uploading asset: ' + $_.Exception.Message)
    $log -join "`n" | Set-Content $out -Encoding UTF8
    exit 1
}

try {
    $check = Invoke-RestMethod -Method Get -Uri "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers
    Log ('verify: name=' + $check.name + ' tag=' + $check.tag_name + ' assets=' + $check.assets.Count)
    foreach ($a in $check.assets) { Log ('  asset: ' + $a.name + ' ' + $a.size + ' bytes') }
} catch {
    Log ('WARN verify failed: ' + $_.Exception.Message)
}

$log -join "`n" | Set-Content $out -Encoding UTF8
exit 0
