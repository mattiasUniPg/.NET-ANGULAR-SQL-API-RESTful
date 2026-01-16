# .NET-ANGULAR-SQL-API-RESTful
Architettura Completa del Sistema FRONTEND -Angular 17; RxJS Reactive Streams → NgRx State → Material UI Components; REST/SignalR BACKEND -.NET 8 Web API;Controllers→Services→Repository Pattern→EF Core;SQL Server, Azure SQL DB; AS/400 IBM i Legacy System; SAP ECC/S4 via RFC/OData; INFRASTRUCTURE -Docker K8s Containers Orchestration Monitoring CI/CD
Backend .NET 8 con Entity Framework Core
EnterpriseSolution/
├── src/
│   ├── EnterpriseAPI/              # API Layer
│   ├── EnterpriseAPI.Application/  # Business Logic
│   ├── EnterpriseAPI.Domain/       # Domain Models
│   ├── EnterpriseAPI.Infrastructure/ # Data Access
│   └── EnterpriseAPI.Integrations/ # AS/400, SAP
├── tests/
│   ├── EnterpriseAPI.UnitTests/
│   └── EnterpriseAPI.IntegrationTests/
└── docker/
    ├── Dockerfile.api
    └── docker-compose.yml
