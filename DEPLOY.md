# Deploying Job-Screener to Azure (free tier)

This puts the app on **Azure App Service, Free (F1) plan**, with a public address like
`https://<your-app-name>.azurewebsites.net`.

## What you get and what you give up

- Free plan limits: 60 CPU minutes per day, 1 GB memory, no custom domain, no "Always On" (the app sleeps when idle and
  takes a few seconds to wake), and a small cap on simultaneous WebSocket connections (Blazor Server uses WebSockets).
  Fine for a demo, not for heavy traffic.
- Storage: the SQLite file lives in `/home`, which persists across restarts on App Service.
- The app has no accounts in version 1 (see the notice on its home page and the README).

## Steps

1. **Create an Azure account** at https://azure.microsoft.com/free (a card is needed for identity checks; the F1
   plan itself costs nothing). Sign in to https://portal.azure.com.
2. **Create the web app:** Portal -> Create a resource -> Web App.
   - Subscription: your free subscription. Resource group: create `job-screener-rg`.
   - Name: a globally unique name, e.g. `job-screener-yourname` (this becomes the URL).
   - Publish: **Code**. Runtime stack: **.NET 8 (LTS)**. Operating system: **Linux**. Region: a nearby one.
   - Pricing plan: **Free F1** (choose "Explore pricing plans" if it is not listed first).
   - Review + create -> Create.
3. **Turn on WebSockets and set the database path:** open the web app ->
   - Settings -> Configuration -> General settings -> **Web sockets: On** -> Save.
   - Settings -> Environment variables -> App settings -> add
     `ConnectionStrings__Default` = `Data Source=/home/jobscreener.db` -> Apply.
4. **Get the publish profile:** web app Overview -> **Download publish profile** (a `.PublishSettings` file).
   If "Download publish profile" is greyed out, go to Settings -> Configuration -> General settings and set
   **SCM Basic Auth Publishing Credentials: On**, save, then try again.
5. **Give GitHub the deployment details** (repository `siradakole/Job-Screener` -> Settings -> Secrets and variables
   -> Actions):
   - Secrets tab -> New repository secret: name `AZURE_WEBAPP_PUBLISH_PROFILE`, value = the full contents of the
     downloaded file.
   - Variables tab -> New repository variable: name `AZURE_WEBAPP_NAME`, value = your web app's name.
6. **Deploy:** repository -> Actions -> **deploy-azure** -> Run workflow. When it finishes, open
   `https://<your-app-name>.azurewebsites.net` (the first load after idle can take 10 to 30 seconds).

## Checks after deploying

- `https://<your-app-name>.azurewebsites.net/healthz` returns `ok`.
- Upload a resume on the home page and run a search with the Remote, last-7-days setting.
- If the page loads but looks frozen, confirm Web sockets is On (step 3).

## Keep it safe

- Never commit the publish profile. It is a secret; only store it as the GitHub secret above.
- Leave `Sources__LinkedIn__Enabled` unset (off). Add Adzuna keys only as App settings in the portal, never in code.
- To stop all charges and remove the app: delete the `job-screener-rg` resource group.
