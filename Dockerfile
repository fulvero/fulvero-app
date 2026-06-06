FROM node:24-alpine AS client-build
WORKDIR /src/client
COPY client/package*.json ./
RUN npm install
COPY client/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server-build
WORKDIR /src
COPY server/*.csproj ./server/
RUN dotnet restore ./server/Fulvero.Api.csproj
COPY server/ ./server/
RUN dotnet publish ./server/Fulvero.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=server-build /app/publish ./
COPY --from=client-build /src/client/dist ./wwwroot
COPY landing/assets/fulvero-logo.png ./wwwroot/email-logo.png
COPY landing/assets/email-banner.jpg ./wwwroot/email-banner.jpg
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fulvero.Api.dll"]
