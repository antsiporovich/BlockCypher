# shortcut: config is baked in at build time rather than bind-mounted at
# runtime. A host bind mount of a file/directory created outside a native
# Windows process has proven unreliable under Docker Desktop's WSL2 backend
# in this environment (empty mount, "no such file or directory" inside the
# container even though the host file is present and correct). Baking it in
# via COPY goes through buildkit instead of the host-mount layer, so it
# always works; the trade-off is a rebuild (`docker compose up --build`)
# whenever prometheus.yml changes, which is fine for this assessment.
# pinned, not :latest - reproducibility, same reasoning as the SQL Server
# image in docker-compose.yml. v3.5.3 is the current Prometheus LTS line;
# verify it still resolves on Docker Hub before relying on it long-term.
FROM prom/prometheus:v3.5.3
COPY prometheus.yml /etc/prometheus/prometheus.yml
