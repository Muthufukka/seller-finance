FROM node:24-alpine AS web-build
WORKDIR /web
COPY src/SellerFinance.Web/package.json src/SellerFinance.Web/pnpm-lock.yaml ./
RUN corepack enable && pnpm install --frozen-lockfile
COPY src/SellerFinance.Web/ ./
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY . .
RUN dotnet publish src/SellerFinance.Api/SellerFinance.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api-build /app/publish .
COPY --from=web-build /web/dist ./wwwroot
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
ENV DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "SellerFinance.Api.dll"]
