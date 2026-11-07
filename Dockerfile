# Build: dotnet SDK image publishes the app; runtime: slim ASP.NET image.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish "Help DeskTicket System/Help DeskTicket System.csproj" -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/App_Data && chown -R $APP_UID /app
USER $APP_UID

# Port 8080, trust the hosting proxy's HTTPS headers, keep memory low for small instances.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["dotnet", "Help DeskTicket System.dll"]
