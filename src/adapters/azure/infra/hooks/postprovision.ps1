#!/usr/bin/env pwsh
# Post-provision hook (azd runs it after "azd provision" and inside "azd up", before the code is deployed).
#
# Runs the product's Azure SQL migrations as the person who ran azd, with that person's own azd sign-in: the tables,
# the row-level security policies, and the function app's managed identity as a contained database user with its
# grants. The app's identity never runs DDL; this hook is the only place the schema changes.
#
# Needs: the .NET 10 SDK, network access from this machine to the SQL server (set SQL_CLIENT_IP_ADDRESS before
# provisioning so the firewall lets this machine in), and the signed-in person must be the SQL server's Microsoft
# Entra administrator or a member of the administrator group.
# Run it again at any time with: azd hooks run postprovision

$ErrorActionPreference = 'Stop'

foreach ($name in @('AZURE_SQL_SERVER_FQDN', 'AZURE_SQL_DATABASE_NAME', 'PRODUCT_APP_IDENTITY_NAME')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "The azd environment has no value for $name. Run 'azd provision' first."
    }
}

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$server = Join-Path $root 'Product.Azure.Server'

# The deployer's connection: Microsoft Entra sign-in through the default credential chain, never a password.
$env:ConnectionStrings__Product = "Server=tcp:$($env:AZURE_SQL_SERVER_FQDN),1433;Database=$($env:AZURE_SQL_DATABASE_NAME);Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;"

# Use the azd sign-in, not whichever other tool happens to be signed in. A pipeline can set its own value first.
if ([string]::IsNullOrWhiteSpace($env:AZURE_TOKEN_CREDENTIALS)) {
    $env:AZURE_TOKEN_CREDENTIALS = 'AzureDeveloperCliCredential'
}

# The migrate command reads both values from configuration: ConnectionStrings:Product (above) and
# Product:Migrate:AppIdentityName, the database user name of the app's managed identity (the function app's name), which
# fills the variable $(AppIdentityName) in the script that creates that user.
$env:Product__Migrate__AppIdentityName = $env:PRODUCT_APP_IDENTITY_NAME

Write-Host "Applying the Azure SQL migrations to $($env:AZURE_SQL_DATABASE_NAME) on $($env:AZURE_SQL_SERVER_FQDN) as the signed-in deployer."
& dotnet run --project $server --configuration Release --no-launch-profile -- migrate
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'The migration did not complete. Common causes:'
    Write-Host '  - This machine is not allowed through the SQL firewall. Set its public IPv4 address with'
    Write-Host '    "azd env set SQL_CLIENT_IP_ADDRESS <address>" (the error above usually names it) and run "azd provision" again.'
    Write-Host '  - The signed-in person is not the SQL server''s Microsoft Entra administrator (see SQL_ADMINISTRATOR_OBJECT_ID).'
    Write-Host '  - The azd sign-in expired. Run "azd auth login" and then "azd hooks run postprovision".'
    exit $LASTEXITCODE
}

Write-Host 'Azure SQL migrations applied.'
