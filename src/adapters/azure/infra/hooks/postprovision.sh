#!/bin/sh
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

set -eu

for name in AZURE_SQL_SERVER_FQDN AZURE_SQL_DATABASE_NAME PRODUCT_APP_IDENTITY_NAME; do
    eval "value=\${$name:-}"
    if [ -z "$value" ]; then
        echo "The azd environment has no value for $name. Run 'azd provision' first." >&2
        exit 1
    fi
done

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
root=$(CDPATH= cd -- "$script_dir/../.." && pwd)

# The deployer's connection: Microsoft Entra sign-in through the default credential chain, never a password.
ConnectionStrings__Product="Server=tcp:${AZURE_SQL_SERVER_FQDN},1433;Database=${AZURE_SQL_DATABASE_NAME};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;"
export ConnectionStrings__Product

# Use the azd sign-in, not whichever other tool happens to be signed in. A pipeline can set its own value first.
if [ -z "${AZURE_TOKEN_CREDENTIALS:-}" ]; then
    AZURE_TOKEN_CREDENTIALS=AzureDeveloperCliCredential
    export AZURE_TOKEN_CREDENTIALS
fi

# The migrate command reads both values from configuration: ConnectionStrings:Product (above) and
# Product:Migrate:AppIdentityName, the database user name of the app's managed identity (the function app's name), which
# fills the variable $(AppIdentityName) in the script that creates that user.
Product__Migrate__AppIdentityName="$PRODUCT_APP_IDENTITY_NAME"
export Product__Migrate__AppIdentityName

echo "Applying the Azure SQL migrations to ${AZURE_SQL_DATABASE_NAME} on ${AZURE_SQL_SERVER_FQDN} as the signed-in deployer."
if ! dotnet run --project "$root/Product.Azure.Server" --configuration Release --no-launch-profile -- migrate; then
    cat >&2 <<'EOF'

The migration did not complete. Common causes:
  - This machine is not allowed through the SQL firewall. Set its public IPv4 address with
    "azd env set SQL_CLIENT_IP_ADDRESS <address>" (the error above usually names it) and run "azd provision" again.
  - The signed-in person is not the SQL server's Microsoft Entra administrator (see SQL_ADMINISTRATOR_OBJECT_ID).
  - The azd sign-in expired. Run "azd auth login" and then "azd hooks run postprovision".
EOF
    exit 1
fi

echo "Azure SQL migrations applied."
