# Deploying for free (Azure)

The app is ASP.NET Core + SQL Server, so it can't run on Vercel or Netlify (those host static sites and JavaScript functions). Azure has free tiers for both pieces:

| Piece | Free tier | Catch |
|---|---|---|
| Web app | App Service **F1** (Linux) | Goes to sleep when idle. First visit after a nap takes ~20-30 seconds. 60 CPU minutes/day, plenty for a demo. |
| Database | Azure SQL Database **free offer** | Pauses when idle. First query after a pause takes up to ~1 minute. 100,000 vCore-seconds/month, plenty for a demo. |

Fix for the catches: open the site 10 minutes before the interview and click through every page. Everything stays warm after that.

All commands run in Terminal from the `student-finance-project` folder. **Keep the same Terminal window open for the whole guide** because later steps use names set in step 1. Paste one block at a time.

## 0. One-time setup

1. **Create a free Azure account** at https://azure.microsoft.com/free. It asks for a card to verify identity; the free services below don't charge it. (Without this, `az login` says "No subscriptions found".)
2. **Install the .NET 8 SDK** from https://dotnet.microsoft.com/download/dotnet/8.0 (macOS, Arm64 for Apple Silicon). Homebrew's `dotnet-sdk` is a newer version that can't run this app's tests or run it locally. Both can be installed side by side.
3. Install the command-line tools and sign in:

```bash
brew install azure-cli sqlcmd
```

```bash
az login
```

When it asks which subscription, pick the free one ("Azure subscription 1").

## 1. Pick names

The SQL server and web app names must be unique across all of Azure, so these add a random number. Replace the three passwords with your own (12+ characters with upper, lower, number and symbol; avoid `'` and `"`).

```bash
RG=rg-student-finance-demo
LOCATION=centralus
SQL_SERVER=sfw-sql-$RANDOM
APP_NAME=student-account-workbench-$RANDOM
SQL_ADMIN_USER=sqladmin
SQL_ADMIN_PASSWORD='ChangeMe-Admin-2026!'
APP_PASSWORD='ChangeMe-App-2026!'
READER_PASSWORD='ChangeMe-Reader-2026!'
```

```bash
echo "SQL server: $SQL_SERVER   Web app: $APP_NAME"
```

Write down both names. If you close Terminal, paste the block again with these exact names (not `$RANDOM`) before continuing.

## 2. Database (free offer)

```bash
az group create --name $RG --location $LOCATION
```

```bash
az sql server create --name $SQL_SERVER --resource-group $RG --location $LOCATION --admin-user $SQL_ADMIN_USER --admin-password "$SQL_ADMIN_PASSWORD"
```

Let the web app and your Mac through the database firewall:

```bash
az sql server firewall-rule create --resource-group $RG --server $SQL_SERVER --name AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0
```

```bash
MY_IP=$(curl -s https://api.ipify.org)
az sql server firewall-rule create --resource-group $RG --server $SQL_SERVER --name MyMac --start-ip-address $MY_IP --end-ip-address $MY_IP
```

Create the database using the free offer:

```bash
az sql db create --resource-group $RG --server $SQL_SERVER --name StudentFinanceWorkbench --edition GeneralPurpose --family Gen5 --capacity 2 --compute-model Serverless --use-free-limit --free-limit-exhaustion-behavior AutoPause
```

If that command errors on the free-offer options, create the database in the Azure portal instead: search "SQL databases" > Create > pick your resource group and the `sfw-sql-...` server > database name `StudentFinanceWorkbench` > click **Apply offer** on the "Want to try Azure SQL Database for free?" banner > Review + create.

Load the schema and synthetic data (a minute or two):

```bash
SQL_SERVER=$SQL_SERVER.database.windows.net SQL_ADMIN_USER=$SQL_ADMIN_USER SQL_ADMIN_PASSWORD="$SQL_ADMIN_PASSWORD" APP_PASSWORD="$APP_PASSWORD" READER_PASSWORD="$READER_PASSWORD" AZURE=1 bash tools/setup-database.sh
```

It should end with `Done.` If it fails partway, delete the database (`az sql db delete --resource-group $RG --server $SQL_SERVER --name StudentFinanceWorkbench --yes`), create it again, and rerun.

## 3. Web app (free F1)

```bash
az appservice plan create --name plan-student-finance --resource-group $RG --sku F1 --is-linux
```

```bash
az webapp create --name $APP_NAME --resource-group $RG --plan plan-student-finance --runtime "DOTNETCORE:8.0"
```

```bash
az webapp config connection-string set --name $APP_NAME --resource-group $RG --connection-string-type SQLAzure --settings Workbench="Server=tcp:$SQL_SERVER.database.windows.net,1433;Database=StudentFinanceWorkbench;User ID=workbench_app;Password=$APP_PASSWORD;Encrypt=True;Connect Timeout=90;" ReportReader="Server=tcp:$SQL_SERVER.database.windows.net,1433;Database=StudentFinanceWorkbench;User ID=report_reader;Password=$READER_PASSWORD;Encrypt=True;Connect Timeout=90;"
```

(`Connect Timeout=90` gives the paused database time to wake up instead of erroring.)

Add your Gemini key (see "Getting a Gemini API key" below) and the GitHub link:

```bash
az webapp config appsettings set --name $APP_NAME --resource-group $RG --settings Gemini__ApiKey='PASTE-YOUR-KEY-HERE' Links__SourceCode='https://github.com/waqarahmed1011/student-finance-project'
```

## 4. Deploy the code

```bash
dotnet publish src/Workbench.Web -c Release -o publish
```

```bash
(cd publish && zip -qr ../app.zip .)
az webapp deploy --name $APP_NAME --resource-group $RG --src-path app.zip --type zip
```

```bash
echo "https://$APP_NAME.azurewebsites.net"
```

Open that URL. The first load can take a minute while everything wakes up. Then click through every page.

To deploy again after code changes, rerun the three commands in this step.

## Getting a Gemini API key (free)

1. Go to https://aistudio.google.com/apikey and sign in with a Google account.
2. Accept the terms if asked, then click **Create API key**.
3. If it asks for a project, pick **Create API key in new project** (or any existing project).
4. Copy the key (starts with `AIza`). Treat it like a password: don't commit it to GitHub or paste it into chats or emails.
5. Use it in the `Gemini__ApiKey` command in step 3. For running locally, put it in `src/Workbench.Web/appsettings.Development.json` under `"Gemini": { "ApiKey": "..." }` (that file is git-ignored).

The free tier needs no billing and has per-minute and per-day request limits that are far above what a demo uses. The app also limits each visitor to 30 AI requests per 10 minutes. Note that Google may use free-tier prompts to improve its products; the app only ever sends the question and the view definitions, never student data.

## If cold starts bother you

Upgrade for the interview week only, then downgrade or delete:

```bash
az appservice plan update --name plan-student-finance --resource-group $RG --sku B1
az webapp config set --name $APP_NAME --resource-group $RG --always-on true
```

B1 is about $13/month billed hourly, so a week costs around $3.

## Optional: deploy from GitHub on every push to main

```bash
az webapp deployment list-publishing-profiles --name $APP_NAME --resource-group $RG --xml > profile.xml
```

In the GitHub repo: Settings > Secrets and variables > Actions. Add secret `AZURE_WEBAPP_PUBLISH_PROFILE` with the contents of `profile.xml` (then delete the file), and variable `AZURE_WEBAPP_NAME` with the app name. `.github/workflows/ci.yml` will then test and deploy on every push to `main`.

## Before the interview

- Open the site 10 minutes before the call and click every page so the app and database are awake.
- Run the sample vendor file once yourself so the reconciliation list isn't empty, then run it again live.
- Try the AI questions you plan to show. If AI misbehaves, the SQL box still works by hand.

## Tear down

```bash
az group delete --name $RG --yes --no-wait
```
