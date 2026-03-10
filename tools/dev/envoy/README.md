# Envoy gRPC-Web Gateway

This gateway bridges Unity clients using HTTP/gRPC-Web to the backend gRPC server (`server-kit/app/grpc_server_main.py`).

## Ports
- Gateway listener: `8081`
- Envoy admin: `9901`
- Upstream gRPC server: `host.docker.internal:50051`

## Run (Docker)
```powershell
docker run --rm -it -p 8081:8081 -p 9901:9901 -v "${PWD}/tools/dev/envoy/envoy.yaml:/etc/envoy/envoy.yaml" envoyproxy/envoy:v1.31-latest
```

## Validate
- Start gRPC server first: `python server-kit/app/grpc_server_main.py`
- Start Envoy gateway (command above)
- Confirm admin endpoint: `http://localhost:9901/server_info`

## Android device note
For physical Android devices, use your machine LAN IP instead of `localhost` for the gateway URL.
