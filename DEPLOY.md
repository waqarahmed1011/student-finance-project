# Deploying to Azure

Azure App Service (Linux, .NET 8) for the web app and Azure SQL Database for the data. Roughly 30 minutes the first time. All commands run from the repo root on a Mac.

**Cost:** App Service B1 (~$13/month) + Azure SQL S0 (~$15/month), billed hourly. Delete the resource group when you're done (last step) and it's a few dollars. Avoid the free tiers for a demo: the free SQL offer auto-pauses and the first page load after a pause takes about a minute, and the free App Service plan sleeps.

## 0. Tools

```bash
brew install azure-cli sqlcmd
brew install --cask dotnet-sdk   # .NET 8 SDK, if you don't have it
az login
```

Pick names. The SQL server and web app names must be globally unique.

```bash
RG=rg-student-finance-demo
LOCATION=centralus
SQL_SERVER=sfw-sql-$RANDOM
APP_NAME=student-account-workbench-$RANDOM
SQL_ADMIN_USER=sqladmin
SQL_ADMIN_PASSWORD='<make one up: 12+ chars, upper, lower, number, symbol>'
APP_PASSWORD='<another strong password>'
READER_PASSWORD='<another strong password>'
echo $SQL_SERVER $APP_NAME   # write these down
```

## 1. Database

```bash
az group create --name $RG --location $LOCATION

az sql server create --name $SQL_SERVER --resource-group $RG --location $LOCATION \
  --admin-user $SQL_ADMIN_USER --admin-password "$SQL_ADMIN_PASSWORD"

# let Azure services (the web app) and your Mac through the firewall
az sql server firewall-rule create --resource-group $RG --server $SQL_SERVER \
  --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
MY_IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create --resource-group $RG --server $SQL_SERVER \
  --name MyMac --start-ip-address $MY_IP --end-ip-address $MY_IP

az sql db create --resource-group $RG --server $SQL_SERVER \
  --name StudentFinanceWorkbench --service-objective S0
```

Load the schema and synthetic data (takes a minute or two):

```bash
SQL_SERVER=$SQL_SERVER.database.windows.net \
SQL_ADMIN_USER=$SQL_ADMIN_USER SQL_ADMIN_PASSWORD="$SQL_ADMIN_PASSWORD" \
APP_PASSWORD="$APP_PASSWORD" READER_PASSWORD="$READER_PASSWORD" \
AZURE=1 ./tools/setup-database.sh
```

`AZURE=1` skips the drop/create step, so run it against an empty database. To reload, delete and recreate the database first.

## 2. Web app

```bash
az appservice plan create --name plan-student-finance --resource-group $RG --sku B1 --is-linux

az webapp create --name $APP_NAME --resource-group $RG --plan plan-student-finance --runtime "DOTNETCORE:8.0"

az webapp config set --name $APP_NAME --resource-group $RG --always-on true

az webapp config connection-string set --name $APP_NAME --resource-group $RG --connection-string-type SQLAzure --settings \
  Workbench="Server=tcp:$SQL_SERVER.database.windows.net,1433;Database=StudentFinanceWorkbench;User ID=workbench_app;Password=$APP_PASSWORD;Encrypt=True;" \
  ReportReader="Server=tcp:$SQL_SERVER.database.windows.net,1433;Database=StudentFinanceWorkbench;User ID=report_reader;Password=$READER_PASSWORD;Encrypt=True;"
```

Optional settings:

```bash
# AI report requests. Key from https://aistudio.google.com/apikey
# Set a budget or quota on the key in Google Cloud since the site is public.
az webapp config appsettings set --name $APP_NAME --resource-group $RG --settings Gemini__ApiKey='<your key>'

# Shows a source code link on the "How it works" page
az webapp config appsettings set --name $APP_NAME --resource-group $RG --settings Links__SourceCode='https://github.com/<you>/<repo>'
```

## 3. Deploy the code

```bash
dotnet publish src/Workbench.Web -c Release -o publish
(cd publish && zip -qr ../app.zip .)
az webapp deploy --name $APP_NAME --resource-group $RG --src-path app.zip --type zip
echo "https://$APP_NAME.azurewebsites.net"
```

Open the URL in a private window and click through every page.

### Optional: deploy from GitHub on every push to main

```bash
az webapp deployment list-publishing-profiles --name $APP_NAME --resource-group $RG --xml > profile.xml
```

In the GitHub repo: Settings > Secrets and variables > Actions. Add secret `AZURE_WEBAPP_PUBLISH_PROFILE` with the contents of `profile.xml` (then delete the file), and variable `AZURE_WEBAPP_NAME` with the app name. `.github/workflows/ci.yml` will test and deploy.

## Before the interview

- Load the site 10 minutes before the call so everything is warm.
- Run the sample vendor file once yourself so the reconciliation list isn't empty, then run it again live.
- Try the three AI questions you plan to show. Have a fallback: the SQL box works without AI.

## Tear down

```bash
az group delete --name $RG --yes --no-wait
```
