# Kubernetes manifests

Deployment + Service + ConfigMap for the API, plus supporting Deployments/Services for SQL Server and
RabbitMQ, mirroring `docker-compose.yml`. These are **best-effort manifests, not cluster-tested** —
there's no Kubernetes cluster in this environment to apply them against. `docker-compose.yml` remains
the primary, verified run path; treat this folder as "here's the K8s shape this would take," not a
second supported deployment target.

## Apply order

```bash
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/configmap.yaml

# copy k8s/secret.example.yaml to k8s/secret.yaml, fill in real values
# (k8s/secret.yaml is already .gitignore'd), then:
kubectl apply -f k8s/secret.yaml

kubectl apply -f k8s/mssql.yaml
kubectl apply -f k8s/rabbitmq.yaml

# build the API image locally and load it into the cluster - kind:
docker build -t icmarkets-api:latest -f src/ICMarkets.Api/Dockerfile .
kind load docker-image icmarkets-api:latest
# minikube: minikube image load icmarkets-api:latest

kubectl apply -f k8s/api.yaml

kubectl -n icmarkets port-forward svc/icmarkets-api 8080:8080
# http://localhost:8080/swagger
```

## What's simplified vs. a production setup

- **RabbitMQ has a PVC (`rabbitmq-data`) and a pinned node name** (`RABBITMQ_NODENAME`), because the
  outbox marks a row processed once the broker has acked it - if the broker's store were replaced, acked
  but unread messages would be lost. Like the rest of these manifests this is untested on a cluster.
- **No PersistentVolumeClaim for SQL Server** — the Deployment is disposable, matching
  `docker-compose.yml`'s `db` service's role as a dev/demo instance, not production storage. A real
  deployment would use a StatefulSet + PVC.
- **No Ingress** — the API Service is `ClusterIP`, reached via `kubectl port-forward` or a `LoadBalancer`/
  `NodePort` override, rather than shipping an Ingress resource tied to one specific controller.
- **`imagePullPolicy: IfNotPresent`** — the image is built locally and loaded into the cluster
  (`kind load docker-image` / `minikube image load`), not pulled from a registry. A real deployment
  would push to a registry and set `imagePullPolicy: Always` with a proper tag.
- **One Secret for both the API and SQL Server** — `icmarkets-api-secrets` carries both the API's
  connection string and the `MSSQL_SA_PASSWORD` the database Deployment reads, rather than two
  narrower, per-service secrets.
- **No HorizontalPodAutoscaler, resource requests/limits, or NetworkPolicy** — out of scope for what
  the task asks for ("Kubernetes manifests (deployment, service, configmap)"); straightforward to add
  on top of this shape.
