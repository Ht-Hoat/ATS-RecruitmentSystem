# ----- Build (.NET 10) -----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ITCareerPlatform.Web.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app

# ----- Runtime -----
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "ITCareerPlatform.Web.dll"]
