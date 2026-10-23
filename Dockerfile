# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/FamilyDispatch.Web/FamilyDispatch.Web.csproj src/FamilyDispatch.Web/
RUN dotnet restore src/FamilyDispatch.Web/FamilyDispatch.Web.csproj
COPY src/ src/
RUN dotnet publish src/FamilyDispatch.Web/FamilyDispatch.Web.csproj -c Release -o /app --no-restore

# Run (the aspnet image listens on 8080 and has a non-root "app" user)
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Database and uploads live here. On Render's free tier this disk resets on restart; the seed refills demo data.
RUN mkdir -p /app/data/uploads && chown -R app:app /app/data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Default="Data Source=/app/data/app.db" \
    Storage__Root=/app/data/uploads
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "FamilyDispatch.Web.dll"]
