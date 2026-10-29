# shortcut: same reasoning as observability/prometheus/Dockerfile - bake the
# provisioning folder in at build time instead of a runtime bind mount,
# which has proven unreliable here under Docker Desktop's WSL2 backend.
# pinned, not :latest - same reasoning as observability/prometheus/Dockerfile.
# Verify 12.4.0 still resolves on Docker Hub before relying on it long-term.
FROM grafana/grafana:12.4.0
COPY provisioning /etc/grafana/provisioning
