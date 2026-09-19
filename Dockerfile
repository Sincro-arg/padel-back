# Imagen para Render: compila la API y la corre sobre el runtime de ASP.NET.
# Render no trae .NET, asi que el despliegue va por Docker.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Padel.Api.csproj ./
RUN dotnet restore Padel.Api.csproj
COPY . .
RUN dotnet publish Padel.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# La base SQLite vive aca. En el plan gratis de Render el disco es efimero:
# lo cargado se pierde en cada despliegue. Para que sobreviva hay que montar un
# disco o pasar a Postgres.
RUN mkdir -p /app/data
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/padel.db"

# Render inyecta PORT; el 8080 es para correrla a mano.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Padel.Api.dll"]
