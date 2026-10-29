# shortcut: config is baked in at build time rather than bind-mounted at
# runtime. A host bind mount of a file/directory created outside a native
# Windows process has proven unreliable under Docker Desktop's WSL2 backend
# in this environment (empty mount, "no such file or directory" inside the
# container even though the host file is present and correct). Baking it in
# via COPY goes through buildkit instead of the host-mount layer, so it
# always works; the trade-off is a rebuild (`docker compose up --build`)
# whenever prometheus.yml changes, which is fine for this assessment.
FROM prom/prometheus:latest
COPY prometheus.yml /etc/prometheus/prometheus.yml
