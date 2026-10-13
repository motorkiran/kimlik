# Kimlik Helm chart

Installs [Kimlik](https://github.com/motorkiran/kimlik) on Kubernetes 1.25 or later. The chart brings no database: point it at a PostgreSQL 16 or later that you run and back up.

## Try it out

```sh
helm install kimlik ./charts/kimlik \
  --set publicUrl=https://id.example.com/ \
  --set secret.values.connectionString='Host=postgres;Database=kimlik;Username=kimlik;Password=…' \
  --set secret.values.masterKey="$(openssl rand -base64 32)" \
  --set secret.values.bootstrapAdminPassword='a password of your own' \
  --set settings.Kimlik__Bootstrap__AdminEmail=you@example.com
```

Kimlik creates its tables, its keys and the first administrator when it starts. Keep a copy of the master key: without it, a restored database cannot be read, and the Secret the chart made goes when the release is uninstalled.

`publicUrl` is the address people see. Route it to the service with TLS, or turn on `ingress.enabled`.

## In production

Keep the secrets out of the values. Make a Secret, or let your secret manager fill one, and name it:

```sh
kubectl create secret generic kimlik-secrets \
  --from-literal=connection-string='Host=…;Database=kimlik;Username=kimlik;Password=…' \
  --from-literal=master-key="$(openssl rand -base64 32)" \
  --from-literal=smtp-password='…' \
  --from-literal=bootstrap-admin-password='…'
```

```yaml
publicUrl: https://id.example.com/
replicaCount: 3
secret:
  existingSecret: kimlik-secrets
migration:
  job:
    enabled: true
settings:
  Kimlik__Email__FromAddress: no-reply@example.com
  Kimlik__Email__Smtp__Host: smtp.example.com
  Kimlik__Email__Smtp__Username: kimlik
  Kimlik__Bootstrap__AdminEmail: you@example.com
ingress:
  enabled: true
  className: nginx
  annotations:
    nginx.ingress.kubernetes.io/affinity: cookie
    nginx.ingress.kubernetes.io/session-cookie-name: kimlik-instance
  tls:
    secretName: kimlik-tls
```

[`ci/production-values.yaml`](ci/production-values.yaml) is a fuller example. Remove the bootstrap password from the Secret once the administrator has signed in.

## What to know

- **Settings.** Every setting in [docs/configuration.md](../../docs/configuration.md) goes under `settings`, by the name of its environment variable (`__` between levels). Settings that hold secrets of your own go in `extraEnv`, from a Secret.
- **Migrations.** By default each instance prepares the database when it starts, under a lock, so several can start at once. With `migration.job.enabled`, a Job does it before each install and upgrade instead, and a failed migration stops the rollout before any instance changes. The Job needs `secret.existingSecret`, because it runs before the release's own resources exist.
- **Provisioning.** A document under `provisioning` (permissions, roles, plans, API resources, clients) is applied whenever the database is prepared. With the migration Job, its ConfigMap is kept between runs and stays behind after an uninstall: delete `<release>-provisioning` yourself.
- **Several instances.** Instances share everything through PostgreSQL. The admin panel keeps a live connection per browser, so with more than one instance the ingress needs sticky sessions for `/admin` and `/_blazor`, as in the example above. Or install a second release with one instance for administrators, and set `admin.enabled: false` on the public one. Rate limits count per instance.
- **Behind the ingress.** Kimlik reads the client's address and the scheme from `X-Forwarded-For` and `X-Forwarded-Proto` (`forwardedHeaders`). Only your ingress controller should be able to reach the pods, or anyone could set them.
- **Pods.** They run as the image's non-root user, with a read-only root file system and no capabilities. Probes use `/health/live` and `/health/ready`.
- **Upgrades.** The chart's version is Kimlik's. `helm upgrade` to a new chart version rolls the instances to that image; read the [changelog](../../CHANGELOG.md) first.

## Values

| Value | Default | |
| --- | --- | --- |
| `publicUrl` | | Required. The address people reach Kimlik at. |
| `replicaCount` | `1` | Instances. |
| `image.repository`, `image.tag` | `ghcr.io/motorkiran/kimlik`, the chart's version | |
| `secret.existingSecret` | | A Secret with the keys under `secret.keys`. |
| `secret.keys.*` | `connection-string`, `master-key`, `smtp-password`, `bootstrap-admin-password` | The Secret's keys; the last two are optional. |
| `secret.values.*` | | The same four values, for the chart to keep, without `existingSecret`. |
| `settings` | `{}` | Other settings, by environment variable name. |
| `extraEnv`, `extraEnvFrom` | `[]` | More environment variables, as in a container. |
| `provisioning` | `{}` | A provisioning document. |
| `admin.enabled` | `true` | Whether these instances serve `/admin`. |
| `migration.job.enabled` | `false` | Migrate in a Job before each install and upgrade. |
| `forwardedHeaders` | `true` | Trust the proxy's forwarded headers. |
| `service.type`, `service.port` | `ClusterIP`, `80` | |
| `ingress.enabled`, `ingress.className`, `ingress.annotations` | `false`, `""`, `{}` | |
| `ingress.host` | the host of `publicUrl` | |
| `ingress.tls.enabled`, `ingress.tls.secretName` | `true`, `""` | |
| `resources` | `{}` | |
| `podDisruptionBudget.enabled`, `.maxUnavailable` | `true`, `1` | Applies with more than one instance. |
| `podSecurityContext`, `securityContext` | non-root, read-only, no capabilities | |
| `serviceAccount.create`, `.name`, `.annotations` | `true`, `""`, `{}` | The token is not mounted: Kimlik does not call the Kubernetes API. |
| `podAnnotations`, `podLabels`, `nodeSelector`, `tolerations`, `affinity`, `topologySpreadConstraints` | empty | |
| `imagePullSecrets`, `nameOverride`, `fullnameOverride`, `terminationGracePeriodSeconds` | | |

## Checking the chart

```sh
charts/kimlik/ci/check.sh
```

It lints the chart, renders it with each example in `ci/`, and checks that it refuses values that cannot work.
