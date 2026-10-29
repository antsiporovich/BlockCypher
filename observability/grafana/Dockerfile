# shortcut: same reasoning as observability/prometheus/Dockerfile - bake the
# provisioning folder in at build time instead of a runtime bind mount,
# which has proven unreliable here under Docker Desktop's WSL2 backend.
FROM grafana/grafana:latest
COPY provisioning /etc/grafana/provisioning
