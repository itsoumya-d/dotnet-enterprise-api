FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OrdersApi.slnx ./
COPY src/OrdersApi/OrdersApi.csproj src/OrdersApi/
COPY tests/OrdersApi.Tests/OrdersApi.Tests.csproj tests/OrdersApi.Tests/
RUN dotnet restore
COPY . .
RUN dotnet publish src/OrdersApi/OrdersApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OrdersApi.dll"]
