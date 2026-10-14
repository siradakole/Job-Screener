FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/JobScreener.Web -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 ConnectionStrings__Default="Data Source=/data/jobscreener.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "JobScreener.Web.dll"]
