# High Load User Login Service

A high-performance gRPC-based user authentication and authorization service designed to handle large volumes of concurrent requests.

## Features

- Rate limiting and DDoS protection
- Horizontal scaling capabilities
- Comprehensive metrics and monitoring
- High availability design
- Envoy load balancing for gRPC services

## Tech Stack

- C# .NET
- gRPC for service communication
- PostgreSQL for persistent storage
- Redis for caching and rate limiting
- Envoy for load balancing and service mesh
- Prometheus for metrics
- Grafana for monitoring dashboards
- Docker and Docker Compose for containerization and orchestration

## Getting Started

### Prerequisites

- .NET SDK 9.0+
- Docker and Docker Compose
- PostgreSQL
- Redis

### Running Locally

1. Clone the repository
2. Navigate to the project directory
3. Run `docker-compose up -d` to start all services
4. To test the service, use the included client: `docker-compose run userloginclient`

## Project Structure

```
.
├── UserLoginService                    # Main service project
│   ├── Data                           # Data access layer
│   ├── Protos                         # Protocol buffer definitions
│   └── Services                       # gRPC service implementations
├── UserLoginClient                     # Test client application
├── envoy                               # Envoy proxy configuration
├── prometheus                          # Prometheus configuration
└── scripts                             # Build and deployment scripts
```

## Performance Considerations

This service is designed to handle high loads with:
- Connection pooling with optimized PostgreSQL connections
- Efficient asynchronous programming with .NET Task Parallel Library
- Database query optimization with proper indexing
- Redis caching for frequently accessed data
- Envoy load balancing for distributing traffic across service instances
- Circuit breaking to prevent cascading failures
- Automatic retries for transient failures

## Load Balancing with Envoy

The project uses Envoy Proxy as a service mesh and load balancer with the following features:

### Load Balancing Features

- **Round-Robin Distribution**: Evenly distributes requests across service instances
- **Circuit Breaking**: Prevents cascading failures when service instances become unresponsive
- **Health Checking**: Continuously monitors service health and removes unhealthy instances
- **Retry Mechanisms**: Automatically retries failed requests with configurable backoff
- **Outlier Detection**: Identifies and ejects misbehaving instances

### Configuration Highlights

```yaml
# Round-robin load balancing across multiple service instances
lb_policy: ROUND_ROBIN

# Circuit breaker settings
circuit_breakers:
  thresholds:
    - max_connections: 1000
      max_pending_requests: 1000
      max_requests: 5000
      max_retries: 3

# Automatic retry policy
retry_policy:
  retry_on: connect-failure,refused-stream,unavailable
  num_retries: 3
  per_try_timeout: 5s
```

## Client Testing Tool

The included UserLoginClient offers both interactive and command-line modes:

### Interactive Mode

```
docker-compose run userloginclient interactive
```

### Load Testing Mode

```
docker-compose run userloginclient load-test -u 1000 -i 20 -c 5000 -p 50
```

## Metrics and Monitoring

The service exposes metrics including:
- Request rates and latencies
- Error rates
- Resource utilization
- Circuit breaker status
- Retry statistics

Access the monitoring dashboards:
- Grafana: http://localhost:3000 (admin/admin)
- Prometheus: http://localhost:9090
- pgAdmin: http://localhost:5050 (admin@example.com/admin)
- Envoy Admin: http://localhost:9901
