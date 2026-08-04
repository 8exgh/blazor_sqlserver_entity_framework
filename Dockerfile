# Runtime image for the Blazor app. Used by docker-compose.e2e.yml and CI; local development
# still runs `dotnet run` against the compose SQL container.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY InvoiceRecon/InvoiceRecon.csproj InvoiceRecon/
RUN dotnet restore InvoiceRecon/InvoiceRecon.csproj
COPY InvoiceRecon/ InvoiceRecon/
RUN dotnet publish InvoiceRecon/InvoiceRecon.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "InvoiceRecon.dll"]
