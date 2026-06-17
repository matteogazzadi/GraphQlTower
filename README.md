# GraphQL Tower

A GraphQL aggregator gateway for Kubernetes. Add multiple upstream GraphQL services and expose them as a single stitched schema through one endpoint.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Kubernetes Cluster                        │
│                                                                  │
│  ┌──────────────────┐    REST API    ┌──────────────────────┐   │
│  │  GraphQlTower.Web │ ────────────► │  GraphQlTower.Api    │   │
│  │  (Blazor Server)  │               │  (HotChocolate GW)   │   │
│  │  Admin UI         │               │  /graphql            │   │
│  └──────────────────┘               └──────┬───────────────┘   │
│                                             │ Schema Stitching   │
│                                    ┌────────┼────────┐          │
│                                    ▼        ▼        ▼          │
│                               Service A  Service B  Service C   │
└─────────────────────────────────────────────────────────────────┘
```

## Projects

| Project | Description |
|---|---|
| `GraphQlTower.Shared` | Shared models, DTOs, interfaces |
| `GraphQlTower.Api` | Gateway API — HotChocolate schema stitching + REST management API |
| `GraphQlTower.Web` | Blazor Server admin UI |

## Features

- **Dynamic schema stitching** — add/remove upstream services at runtime; the gateway re-stitches automatically
- **Admin UI** — Blazor Server UI to manage services, view schemas, and check health
- **Health monitoring** — background checks every 30s, exposed at `/healthz/ready`
- **BananaCakePop** — built-in GraphQL IDE at `/graphql/ui`
- **Kubernetes-native** — Helm chart with liveness/readiness probes, PVC for SQLite, configurable HPA

## Quick Start (local)

```bash
docker-compose up --build
```

- Gateway GraphQL endpoint: http://localhost:5000/graphql
- BananaCakePop IDE: http://localhost:5000/graphql/ui
- Admin UI: http://localhost:5001
- Swagger: http://localhost:5000/swagger

## Kubernetes Deployment

```bash
# Set your image registry
helm upgrade --install graphql-tower ./k8s/helm/graphql-tower \
  --set api.image.repository=your-registry/graphql-tower-api \
  --set api.image.tag=1.0.0 \
  --set web.image.repository=your-registry/graphql-tower-web \
  --set web.image.tag=1.0.0 \
  --set ingress.hosts[0].host=graphql-tower.example.com
```

### Seed initial services via values.yaml

```yaml
initialServices:
  - name: products
    displayName: Products Service
    url: http://products-service/graphql
    enabled: true
  - name: orders
    displayName: Orders Service
    url: http://orders-service/graphql
    enabled: true
```

## Adding an Upstream Service (API)

```bash
curl -X POST http://localhost:5000/api/services \
  -H "Content-Type: application/json" \
  -d '{
    "name": "products",
    "displayName": "Products Service",
    "url": "http://products-service/graphql",
    "isEnabled": true,
    "headers": [
      { "key": "Authorization", "value": "Bearer <token>" }
    ]
  }'
```

The gateway will automatically pick up the new service and re-stitch the schema within seconds.

## Schema Name Rules

The `name` field is used as the GraphQL schema identifier in stitching:
- Must start with a letter
- Only letters, digits, and underscores (`[a-zA-Z][a-zA-Z0-9_]*`)
- Must be unique across all registered services

## SQLite vs PostgreSQL

The default persistence uses **SQLite** (stored on a PVC). This works fine with `replicaCount: 1`.

For multi-replica deployments, switch to PostgreSQL:
1. Add `Npgsql.EntityFrameworkCore.PostgreSQL` to `GraphQlTower.Api`
2. Change `UseSqlite` → `UseNpgsql` in `Program.cs`
3. Set `ConnectionStrings__Registry` to a PostgreSQL connection string

## Health Endpoints

| Endpoint | Purpose |
|---|---|
| `GET /healthz/live` | Kubernetes liveness probe (fast) |
| `GET /healthz/ready` | Kubernetes readiness probe (checks DB + upstream services) |

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `ConnectionStrings__Registry` | `Data Source=/data/registry.db` | SQLite path or PostgreSQL connection string |
| `Cors__AllowedOrigins__0` | *(none)* | CORS allowed origin for the admin UI |
| `HealthMonitor__IntervalSeconds` | `30` | Upstream health check interval |
| `GatewayApi__BaseUrl` | `http://graphql-tower-api:8080` | (Web only) URL of the API service |
| `TOWER_INITIAL_SERVICES` | *(none)* | JSON array of services to seed on first boot |
