# Windows code signing

CodeCrack ships an unsigned build by default. Signing is **optional and fully
credential-guarded**: `winapp/make-app.ps1` produces a working, unsigned bundle
when no signing credentials are present, and signs + verifies automatically once
you add the right secret. Nothing about the build *requires* a certificate.

Two backends are supported, checked in this order:

| Priority | Backend | Trigger (env / secret) | Best for |
|---|---|---|---|
| 1 | **Azure Trusted Signing** | `CODECRACK_AZURE_ENDPOINT` + `CODECRACK_AZURE_ACCOUNT` + `CODECRACK_AZURE_CERT_PROFILE` | **Recommended.** No HSM/USB token, Microsoft-managed keys, pay-as-you-go, first-class in GitHub Actions. |
| 2 | **PFX certificate** | `CODECRACK_WINDOWS_CERT` (path to `.pfx`) + `CODECRACK_WINDOWS_CERT_PASSWORD` | An OV/EV code-signing cert you already hold as a `.pfx`. |

If neither is configured, signing is a **no-op** and the build still succeeds.
After signing (either backend), `make-app.ps1` re-checks the signature with
`signtool verify /pa` and fails the build if verification does not pass.

The signing logic lives in `Invoke-Sign` / `Resolve-SigningMode` /
`Resolve-AzureDlib` / `Get-SignTool` in `winapp/make-app.ps1`. `Get-SignTool`
resolves `signtool.exe` from `PATH` first, then the newest Windows SDK
(`C:\Program Files (x86)\Windows Kits\10\bin\*\{x64,x86,arm64}\signtool.exe`).

---

## Option A — Azure Trusted Signing (recommended)

Trusted Signing keeps the private key in an Azure-managed HSM; you never handle a
`.pfx`. It fits CI cleanly and works for individuals and organizations.

### 1. Provision (one-time, in Azure)

1. In the Azure Portal, create a **Trusted Signing account** (Microsoft.CodeSigning).
   Note its **endpoint URI** for your region, e.g. `https://eus.codesigning.azure.net`.
2. Complete **identity validation** (individual or organization). Individual
   validation is typically fast; organization validation needs D-U-N-S details.
3. Create a **Certificate profile** (Public Trust) under the account. Note its name.
4. Create a service principal (App registration) for CI and grant it the
   **Trusted Signing Certificate Profile Signer** role on the account. Record its
   `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, and a client secret (`AZURE_CLIENT_SECRET`).

### 2. Wire the values

`make-app.ps1` signs via `signtool` + the **Azure.CodeSigning** dlib
(`Azure.CodeSigning.Dlib.dll`, shipped in the `Microsoft.Trusted.Signing.Client`
NuGet package). Azure authentication uses the ambient credential
(`AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET`, or an
`azure/login` OIDC session).

Environment variables read by `make-app.ps1`:

| Variable | Meaning |
|---|---|
| `CODECRACK_AZURE_ENDPOINT` | Trusted Signing account endpoint URI |
| `CODECRACK_AZURE_ACCOUNT` | Trusted Signing account name |
| `CODECRACK_AZURE_CERT_PROFILE` | Certificate profile name |
| `CODECRACK_AZURE_DLIB` | *(optional)* explicit path to `Azure.CodeSigning.Dlib.dll`. If unset, `make-app.ps1` searches the NuGet global-packages cache. |
| `AZURE_TENANT_ID` / `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` | Service-principal credential for signing |

Local dry run (restore the client into the NuGet cache first, then build):

```powershell
dotnet restore  # any project that references Microsoft.Trusted.Signing.Client, or:
#   mkdir tsclient; @'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.Trusted.Signing.Client" Version="1.0.60" /></ItemGroup></Project>'@ | Set-Content tsclient\tsclient.csproj; dotnet restore tsclient\tsclient.csproj

$env:CODECRACK_AZURE_ENDPOINT     = 'https://eus.codesigning.azure.net'
$env:CODECRACK_AZURE_ACCOUNT      = '<account-name>'
$env:CODECRACK_AZURE_CERT_PROFILE = '<profile-name>'
$env:AZURE_TENANT_ID = '...'; $env:AZURE_CLIENT_ID = '...'; $env:AZURE_CLIENT_SECRET = '...'
$env:CODECRACK_SKIP_LAUNCH = '1'
./winapp/make-app.ps1
```

### 3. In GitHub Actions

Add these **repository secrets**: `CODECRACK_AZURE_ENDPOINT`,
`CODECRACK_AZURE_ACCOUNT`, `CODECRACK_AZURE_CERT_PROFILE`, `AZURE_TENANT_ID`,
`AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`.

`release.yml` (and the CI `windows-build` job) already consume them:

- A guarded **"Prepare Azure Trusted Signing client"** step runs only when
  `secrets.CODECRACK_AZURE_ENDPOINT` is set; it restores
  `Microsoft.Trusted.Signing.Client` so the dlib is in the NuGet cache.
- The build step passes all `CODECRACK_AZURE_*` and `AZURE_*` values into
  `make-app.ps1`, which then signs and verifies.

No secrets => both the prep step and signing are skipped; the release is unsigned
but still builds and publishes.

> Alternative: instead of the built-in dlib path you can use the official
> [`azure/trusted-signing-action`](https://github.com/Azure/trusted-signing-action)
> as a post-build step. Point it at `dist/CodeCrack/CodeCrack.exe`. Either path
> yields the same signed artifact.

---

## Option B — PFX (OV certificate)

If you hold an OV (Organization Validation) code-signing certificate as a `.pfx`:

1. Export/obtain the `.pfx` and its password. (EV certs usually live on an HSM/
   token and are **not** exportable as a `.pfx` — use Trusted Signing instead.)
2. Local build:

   ```powershell
   $env:CODECRACK_WINDOWS_CERT          = 'C:\path\to\codecrack.pfx'
   $env:CODECRACK_WINDOWS_CERT_PASSWORD = '<pfx password>'
   $env:CODECRACK_SKIP_LAUNCH = '1'
   ./winapp/make-app.ps1
   ```

   `make-app.ps1` runs
   `signtool sign /fd SHA256 /f <pfx> /p <password> /tr http://timestamp.digicert.com /td SHA256`
   then `signtool verify /pa`.

3. In GitHub Actions, add secrets `CODECRACK_WINDOWS_CERT` and
   `CODECRACK_WINDOWS_CERT_PASSWORD`. Because a secret can't be a file path on the
   runner, materialize the `.pfx` from a base64 secret in a step before the build,
   e.g.:

   ```yaml
   - name: Write signing cert
     if: ${{ secrets.CODECRACK_WINDOWS_CERT_B64 != '' }}
     shell: pwsh
     run: |
       [IO.File]::WriteAllBytes("$env:RUNNER_TEMP\cc.pfx",
         [Convert]::FromBase64String("${{ secrets.CODECRACK_WINDOWS_CERT_B64 }}"))
       "CODECRACK_WINDOWS_CERT=$env:RUNNER_TEMP\cc.pfx" | Out-File $env:GITHUB_ENV -Append
   ```

   (then set `CODECRACK_WINDOWS_CERT_PASSWORD` as the build step's env).

---

## Verifying a signed build

```powershell
signtool verify /pa /v dist\CodeCrack\CodeCrack.exe
# or inspect the Digital Signatures tab of the exe's Properties dialog
```

`make-app.ps1` already runs `signtool verify /pa` as part of signing, so a green
build is a verified build.

## Which should I pick?

- **Shipping from GitHub Actions, no hardware token** -> Azure Trusted Signing.
- **You already own an OV `.pfx`** -> PFX.
- **EV certificate on an HSM/USB token** -> can't be scripted headlessly here;
  migrate to Trusted Signing for CI.
